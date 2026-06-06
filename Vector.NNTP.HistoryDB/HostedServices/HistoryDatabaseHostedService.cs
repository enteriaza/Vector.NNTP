// <copyright file="HistoryDatabaseHostedService.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Vector.NNTP.HistoryDB.Configuration;
using Vector.NNTP.HistoryDB.Encoding;
using Vector.NNTP.HistoryDB.Memory;
using Vector.NNTP.HistoryDB.Metrics;
using Vector.NNTP.HistoryDB.Redis;
using Vector.NNTP.HistoryDB.Rocks;
using Vector.NNTP.HistoryDB.Services;
using Vector.NNTP.HistoryDB.Telemetry;

namespace Vector.NNTP.HistoryDB.HostedServices
{
    /// <summary>
    /// Runs full Rocks→Redis rebuild on every process start before HistoryDB accepts CHECK.
    /// </summary>
    /// <param name="logger">Logger for source-generated <c>[LoggerMessage]</c> methods.</param>
    internal sealed partial class HistoryDatabaseHostedService(
        ILogger<HistoryDatabaseHostedService> logger) : IHostedService
    {
        /// <summary>
        /// The history service.
        /// </summary>
        private readonly HistoryDatabaseService _history = null!;

        /// <summary>
        /// The Rocks store.
        /// </summary>
        private readonly RocksHistoryStore _rocks = null!;

        /// <summary>
        /// The Redis store.
        /// </summary>
        private readonly HistoryRedisStore _redis = null!;

        /// <summary>
        /// The memory cache.
        /// </summary>
        private readonly HistoryMemoryCache _memory = null!;

        /// <summary>
        /// The options.
        /// </summary>
        private readonly HistoryDbOptions _options = null!;

        /// <summary>
        /// The metrics.
        /// </summary>
        private readonly HistoryMetrics _metrics = null!;

        /// <summary>
        /// The generation store.
        /// </summary>
        private readonly HistoryGenerationStore _generations = null!;

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
        /// <param name="logger">Logger for source-generated <c>[LoggerMessage]</c> methods.</param>
        public HistoryDatabaseHostedService(
            HistoryDatabaseService history,
            RocksHistoryStore rocks,
            HistoryRedisStore redis,
            HistoryMemoryCache memory,
            IOptions<HistoryDbOptions> options,
            HistoryMetrics metrics,
            HistoryGenerationStore generations,
            ILogger<HistoryDatabaseHostedService> logger)
            : this(logger)
        {
            _history = history;
            _rocks = rocks;
            _redis = redis;
            _memory = memory;
            _options = options.Value;
            _metrics = metrics;
            _generations = generations;
        }

        /// <summary>
        /// Starts the history database hosted service.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        /// <exception cref="OperationCanceledException">Thrown when startup is canceled.</exception>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            ulong generation = await ResolveGenerationAsync(cancellationToken).ConfigureAwait(false);
            byte[]? resumeKey = await TryGetResumeKeyAsync(generation, cancellationToken).ConfigureAwait(false);
            long total = 0;
            ulong now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long rebuildStartTimestamp = Stopwatch.GetTimestamp();

            using Activity? activity = HistoryDbTelemetry.ActivitySource.StartActivity("history.rebuild");
            _ = (activity?.SetTag("history.generation", generation));
            _ = (activity?.SetTag("history.resume", resumeKey is not null));

            try
            {
                await _redis.SetRebuildStateAsync(
                    [
                        new HashEntry("status", "in_progress"),
                        new HashEntry("generation", generation.ToString()),
                        new HashEntry("keysProcessed", "0"),
                        new HashEntry("startedAt", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()),
                    ],
                    cancellationToken).ConfigureAwait(false);

                LogRebuildStarted(generation, resumeKey is not null);

                long processed = await _rocks.RebuildForwardAsync(
                    now,
                    resumeKey,
                    async (batch, ct) =>
                    {
                        await _redis.PipelineSetBatchAsync(batch, now, ct).ConfigureAwait(false);
                        total += batch.Count;
                        _metrics.SetRebuildKeysProcessed(total);
                        if (total > 0 && total % _options.RebuildCheckpointInterval == 0)
                        {
                            byte[] lastKey = batch[^1].ExpirationKey;
                            await _redis.SetRebuildStateAsync(
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
                await _redis.SetMetaAsync(generation, cancellationToken).ConfigureAwait(false);
                await _redis.SetRebuildStateAsync(
                    [
                        new HashEntry("status", "completed"),
                        new HashEntry("generation", generation.ToString()),
                        new HashEntry("keysProcessed", total.ToString()),
                        new HashEntry("updatedAt", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()),
                    ],
                    cancellationToken).ConfigureAwait(false);

                if (_options.EnableMemoryPreloadOnStartup)
                {
                    _ = _rocks.PreloadReverse(
                        now,
                        (digest, exp) =>
                        {
                            DigestKey digestKey = new(digest);
                            _memory.InsertOrUpdate(in digestKey, exp);
                        },
                        _options.MemoryLimitBytes);
                }

                double rebuildMs = Stopwatch.GetElapsedTime(rebuildStartTimestamp).TotalMilliseconds;
                _metrics.RecordRebuildDurationMilliseconds(rebuildMs);
                _ = (activity?.SetTag("history.keys_processed", total));
                _ = (activity?.SetTag("history.rebuild.duration_ms", rebuildMs));

                _history.SetOperational();
                LogRebuildCompleted(total);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogRebuildFailed(ex, generation, total);
                _ = (activity?.SetStatus(ActivityStatusCode.Error, ex.Message));
                throw;
            }
        }

        /// <summary>
        /// Stops the history database hosted service.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

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
            HashEntry[] state = await _redis.GetRebuildStateAsync(cancellationToken).ConfigureAwait(false);
            string status = GetField(state, "status");
            string gen = GetField(state, "generation");
            return string.Equals(status, "in_progress", StringComparison.Ordinal) &&
                   ulong.TryParse(gen, out ulong resumeGen)
                ? resumeGen
                : _generations.AllocateGeneration();
        }

        /// <summary>
        /// Reads resume cursor from Redis rebuild state when applicable.
        /// </summary>
        /// <param name="generation">Expected generation.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Last expiration key bytes or null.</returns>
        private async Task<byte[]?> TryGetResumeKeyAsync(ulong generation, CancellationToken cancellationToken)
        {
            HashEntry[] state = await _redis.GetRebuildStateAsync(cancellationToken).ConfigureAwait(false);
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
            catch (FormatException ex)
            {
                LogResumeKeyHexParseFailed(ex);
                return Convert.FromBase64String(lastKey);
            }
        }
    }
}
