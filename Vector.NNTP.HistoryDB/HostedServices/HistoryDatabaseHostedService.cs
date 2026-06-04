// <copyright file="HistoryDatabaseHostedService.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Vector.NNTP.HistoryDB.Configuration;
using Vector.NNTP.HistoryDB.Encoding;
using Vector.NNTP.HistoryDB.Memory;
using Vector.NNTP.HistoryDB.Metrics;
using Vector.NNTP.HistoryDB.Redis;
using Vector.NNTP.HistoryDB.Rocks;
using Vector.NNTP.HistoryDB.Services;

namespace Vector.NNTP.HistoryDB.HostedServices
{
    /// <summary>
    /// Runs full Rocks→Redis rebuild on every process start before HistoryDB accepts CHECK.
    /// </summary>
    internal sealed class HistoryDatabaseHostedService : IHostedService
    {
        /// <summary>
        /// The history service.
        /// </summary>
        private readonly HistoryDatabaseService _history;

        /// <summary>
        /// The Rocks store.
        /// </summary>
        private readonly RocksHistoryStore _rocks;

        /// <summary>
        /// The Redis store.
        /// </summary>
        private readonly HistoryRedisStore _redis;

        /// <summary>
        /// The memory cache.
        /// </summary>
        private readonly HistoryMemoryCache _memory;

        /// <summary>
        /// The options.
        /// </summary>
        private readonly HistoryDbOptions _options;

        /// <summary>
        /// The metrics.
        /// </summary>
        private readonly HistoryMetrics _metrics;

        /// <summary>
        /// The generation store.
        /// </summary>
        private readonly HistoryGenerationStore _generations;

        /// <summary>
        /// The logger.
        /// </summary>
        private readonly ILogger<HistoryDatabaseHostedService> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="HistoryDatabaseHostedService"/> class.
        /// </summary>
        /// <param name="history">History service.</param>
        /// <param name="rocks">Rocks store.</param>
        /// <param name="redis">Redis store.</param>
        /// <param name="memory">Memory cache.</param>
        /// <param name="options">Options.</param>
        /// <param name="metrics">Metrics.</param>
        /// <param name="generations">Generation store.</param>
        /// <param name="logger">Logger.</param>
        public HistoryDatabaseHostedService(
            HistoryDatabaseService history,
            RocksHistoryStore rocks,
            HistoryRedisStore redis,
            HistoryMemoryCache memory,
            IOptions<HistoryDbOptions> options,
            HistoryMetrics metrics,
            HistoryGenerationStore generations,
            ILogger<HistoryDatabaseHostedService> logger)
        {
            this._history = history;
            this._rocks = rocks;
            this._redis = redis;
            this._memory = memory;
            this._options = options.Value;
            this._metrics = metrics;
            this._generations = generations;
            this._logger = logger;
        }

        /// <summary>
        /// Starts the history database hosted service.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            ulong generation = await this.ResolveGenerationAsync(cancellationToken).ConfigureAwait(false);
            byte[]? resumeKey = await this.TryGetResumeKeyAsync(generation, cancellationToken).ConfigureAwait(false);
            long total = 0;
            ulong now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            await this._redis.SetRebuildStateAsync(
                [
                    new HashEntry("status", "in_progress"),
                    new HashEntry("generation", generation.ToString()),
                    new HashEntry("keysProcessed", "0"),
                    new HashEntry("startedAt", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()),
                ],
                cancellationToken).ConfigureAwait(false);

            this._logger.LogInformation(
                "HistoryDB rebuild started (generation={Generation}, resume={Resume})",
                generation,
                resumeKey is not null);

            long processed = await this._rocks.RebuildForwardAsync(
                now,
                resumeKey,
                async (batch, ct) =>
                {
                    await this._redis.PipelineSetBatchAsync(batch, now, ct).ConfigureAwait(false);
                    total += batch.Count;
                    this._metrics.SetRebuildKeysProcessed(total);
                    if (total > 0 && total % this._options.RebuildCheckpointInterval == 0)
                    {
                        byte[] lastKey = batch[^1].ExpirationKey;
                        await this._redis.SetRebuildStateAsync(
                            [
                                new HashEntry("status", "in_progress"),
                                new HashEntry("generation", generation.ToString()),
                                new HashEntry("lastExpirationKey", Convert.ToHexString(lastKey)),
                                new HashEntry("keysProcessed", total.ToString()),
                                new HashEntry("updatedAt", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()),
                            ],
                            ct).ConfigureAwait(false);
                    }
                },
                cancellationToken).ConfigureAwait(false);

            _ = processed;
            await this._redis.SetMetaAsync(generation, cancellationToken).ConfigureAwait(false);
            await this._redis.SetRebuildStateAsync(
                [
                    new HashEntry("status", "completed"),
                    new HashEntry("generation", generation.ToString()),
                    new HashEntry("keysProcessed", total.ToString()),
                    new HashEntry("updatedAt", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()),
                ],
                cancellationToken).ConfigureAwait(false);

            if (this._options.EnableMemoryPreloadOnStartup)
            {
                _ = this._rocks.PreloadReverse(
                    now,
                    (digest, exp) =>
                    {
                        var digestKey = new DigestKey(digest);
                        this._memory.InsertOrUpdate(in digestKey, exp);
                    },
                    this._options.MemoryLimitBytes);
            }

            this._history.SetOperational();
            this._logger.LogInformation(
                "HistoryDB rebuild completed ({Keys} keys); CHECK operational. RocksDB receives new entries asynchronously after CHECK returns 238 (Wanted) via the persist queue — not at open or on 438 duplicate.",
                total);
        }

        /// <summary>
        /// Stops the history database hosted service.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        /// <summary>
        /// Reads a hash field by name.
        /// </summary>
        /// <param name="entries">Hash entries.</param>
        /// <param name="name">Field name.</param>
        /// <returns>Field value or empty string.</returns>
        private static string GetField(HashEntry[] entries, string name)
        {
            foreach (HashEntry entry in entries)
            {
                if (entry.Name == name)
                {
                    return entry.Value.ToString() ?? string.Empty;
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Resolves rebuild generation (resume in-progress or allocate new).
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Generation stamp.</returns>
        private async Task<ulong> ResolveGenerationAsync(CancellationToken cancellationToken)
        {
            HashEntry[] state = await this._redis.GetRebuildStateAsync(cancellationToken).ConfigureAwait(false);
            string status = GetField(state, "status");
            string gen = GetField(state, "generation");
            if (string.Equals(status, "in_progress", StringComparison.Ordinal) &&
                ulong.TryParse(gen, out ulong resumeGen))
            {
                return resumeGen;
            }

            return this._generations.AllocateGeneration();
        }

        /// <summary>
        /// Reads resume cursor from Redis rebuild state when applicable.
        /// </summary>
        /// <param name="generation">Expected generation.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Last expiration key bytes or null.</returns>
        private async Task<byte[]?> TryGetResumeKeyAsync(ulong generation, CancellationToken cancellationToken)
        {
            HashEntry[] state = await this._redis.GetRebuildStateAsync(cancellationToken).ConfigureAwait(false);
            if (state.Length == 0)
            {
                return null;
            }

            string status = GetField(state, "status");
            string gen = GetField(state, "generation");
            string lastKey = GetField(state, "lastExpirationKey");
            if (!string.Equals(status, "in_progress", StringComparison.Ordinal) ||
                !string.Equals(gen, generation.ToString(), StringComparison.Ordinal) ||
                string.IsNullOrEmpty(lastKey))
            {
                return null;
            }

            try
            {
                return Convert.FromHexString(lastKey);
            }
            catch (FormatException)
            {
                return Convert.FromBase64String(lastKey);
            }
        }
    }
}
