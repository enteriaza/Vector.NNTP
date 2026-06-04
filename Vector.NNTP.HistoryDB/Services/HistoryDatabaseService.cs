// <copyright file="HistoryDatabaseService.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vector.NNTP.HistoryDB.Abstractions;
using Vector.NNTP.HistoryDB.Configuration;
using Vector.NNTP.HistoryDB.Encoding;
using Vector.NNTP.HistoryDB.Memory;
using Vector.NNTP.HistoryDB.Metrics;
using Vector.NNTP.HistoryDB.Redis;
using Vector.NNTP.Session.Redis.Exceptions;

namespace Vector.NNTP.HistoryDB.Services
{
    /// <summary>
    /// Transit history: read-only CHECK probe and TAKETHIS/IHAVE record with async Rocks backfill.
    /// </summary>
    internal sealed class HistoryDatabaseService : IHistoryDatabase
    {
        /// <summary>
        /// The options.
        /// </summary>
        private readonly HistoryDbOptions _options;

        /// <summary>
        /// The memory cache.
        /// </summary>
        private readonly HistoryMemoryCache _memory;

        /// <summary>
        /// The Redis store.
        /// </summary>
        private readonly HistoryRedisStore _redis;

        /// <summary>
        /// The metrics.
        /// </summary>
        private readonly HistoryMetrics _metrics;

        /// <summary>
        /// The persist pump.
        /// </summary>
        private readonly HistoryRocksPersistPump _persistPump;

        /// <summary>
        /// The persist queue.
        /// </summary>
        private readonly Channel<HistoryPersistItem> _queue;

        /// <summary>
        /// The host lifetime.
        /// </summary>
        private readonly IHostApplicationLifetime _lifetime;

        /// <summary>
        /// The logger.
        /// </summary>
        private readonly ILogger<HistoryDatabaseService> _logger;

        /// <summary>
        /// Whether the database is operational.
        /// </summary>
        private volatile bool _operational;

        /// <summary>
        /// Initializes a new instance of the <see cref="HistoryDatabaseService"/> class.
        /// </summary>
        /// <param name="options">Options.</param>
        /// <param name="memory">Memory tier.</param>
        /// <param name="redis">Redis tier.</param>
        /// <param name="metrics">Metrics.</param>
        /// <param name="persistPump">Rocks persist pump.</param>
        /// <param name="lifetime">Host lifetime for queue completion on shutdown.</param>
        /// <param name="logger">Logger.</param>
        public HistoryDatabaseService(
            IOptions<HistoryDbOptions> options,
            HistoryMemoryCache memory,
            HistoryRedisStore redis,
            HistoryMetrics metrics,
            HistoryRocksPersistPump persistPump,
            IHostApplicationLifetime lifetime,
            ILogger<HistoryDatabaseService> logger)
        {
            this._options = options.Value;
            this._memory = memory;
            this._redis = redis;
            this._metrics = metrics;
            this._persistPump = persistPump;
            this._lifetime = lifetime;
            this._logger = logger;
            BoundedChannelOptions channelOptions = new BoundedChannelOptions(this._options.QueueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
                SingleWriter = false,
            };
            this._queue = Channel.CreateBounded<HistoryPersistItem>(channelOptions);
            this._lifetime.ApplicationStopping.Register(() => this._queue.Writer.TryComplete());
        }

        /// <summary>
        /// Gets a value indicating whether CHECK may proceed against Redis.
        /// </summary>
        public bool IsOperational => this._operational;

        /// <summary>
        /// Gets the persist queue for the background worker.
        /// </summary>
        public ChannelReader<HistoryPersistItem> PersistReader => this._queue.Reader;

        /// <summary>
        /// Marks the database operational after startup rebuild and starts the Rocks persist pump.
        /// </summary>
        public void SetOperational()
        {
            this._operational = true;
            this._persistPump.Start(this._queue.Reader, this._lifetime.ApplicationStopping);
        }

        /// <summary>
        /// Read-only Redis CHECK probe (no writes on wanted).
        /// </summary>
        /// <param name="messageId">Message ID.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>CHECK result.</returns>
        public ValueTask<HistoryCheckResult> CheckAsync(string messageId, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(messageId);
            if (!this._operational)
            {
                this._metrics.RecordUnavailable();
                return new ValueTask<HistoryCheckResult>(HistoryCheckResult.Unavailable);
            }

            Span<byte> digest = stackalloc byte[HistoryKeyEncoder.DigestLength];
            if (!HistoryKeyEncoder.TryComputeDigest(messageId, digest))
            {
                this._metrics.RecordTryAgain();
                return new ValueTask<HistoryCheckResult>(HistoryCheckResult.TryAgainLater);
            }

            DigestKey digestKey = new DigestKey(digest);
            ulong now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (this._memory.TryGetDuplicate(in digestKey, now))
            {
                this._metrics.RecordDuplicate();
                return new ValueTask<HistoryCheckResult>(HistoryCheckResult.Duplicate);
            }

            return this.CheckRedisProbeAsync(digestKey, now, cancellationToken);
        }

        /// <summary>
        /// Atomic Redis record on TAKETHIS/IHAVE accept.
        /// </summary>
        /// <param name="messageId">Message ID.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Record result.</returns>
        public ValueTask<HistoryRecordResult> TryRecordAsync(string messageId, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(messageId);
            if (!this._operational)
            {
                this._metrics.RecordRecordUnavailable();
                return new ValueTask<HistoryRecordResult>(HistoryRecordResult.Unavailable);
            }

            Span<byte> digest = stackalloc byte[HistoryKeyEncoder.DigestLength];
            if (!HistoryKeyEncoder.TryComputeDigest(messageId, digest))
            {
                this._metrics.RecordRecordTryAgain();
                return new ValueTask<HistoryRecordResult>(HistoryRecordResult.TryAgainLater);
            }

            var digestKey = new DigestKey(digest);
            ulong now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (this._memory.TryGetDuplicate(in digestKey, now))
            {
                this._metrics.RecordRecordDuplicate();
                return new ValueTask<HistoryRecordResult>(HistoryRecordResult.Duplicate);
            }

            return this.RecordRedisAsync(digestKey, now, cancellationToken);
        }

        /// <summary>
        /// Enqueues a persist item for tests (same path as <see cref="TryRecordAsync"/> success).
        /// </summary>
        /// <param name="digest">32-byte digest.</param>
        /// <param name="expirationEpochSeconds">Expiration epoch.</param>
        /// <returns><see langword="true"/> when the item was queued.</returns>
        internal bool TryEnqueuePersist(ReadOnlySpan<byte> digest, ulong expirationEpochSeconds)
        {
            byte[] digestBytes = digest.ToArray();
            return this._queue.Writer.TryWrite(new HistoryPersistItem(digestBytes, expirationEpochSeconds));
        }

        /// <summary>
        /// Read-only Redis CHECK probe (no writes on wanted).
        /// </summary>
        /// <param name="digestKey">Digest key.</param>
        /// <param name="now">Current UTC epoch seconds.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>CHECK result.</returns>
        private async ValueTask<HistoryCheckResult> CheckRedisProbeAsync(
            DigestKey digestKey,
            ulong now,
            CancellationToken cancellationToken)
        {
            try
            {
                Stopwatch sw = Stopwatch.StartNew();
                byte[] digestBytes = new byte[HistoryKeyEncoder.DigestLength];
                digestKey.CopyTo(digestBytes);
                int code = await this._redis.CheckProbeAsync(digestBytes, now, cancellationToken)
                    .ConfigureAwait(false);
                this._metrics.RecordRedisMilliseconds(sw.Elapsed.TotalMilliseconds);
                return this.MapCheckProbeResult(code);
            }
            catch (RedisUnavailableException ex)
            {
                this._logger.LogWarning(ex, "History Redis unavailable for CHECK");
                this._metrics.RecordTryAgain();
                return HistoryCheckResult.TryAgainLater;
            }
            catch (TimeoutException ex)
            {
                this._logger.LogWarning(ex, "History Redis timeout for CHECK");
                this._metrics.RecordTryAgain();
                return HistoryCheckResult.TryAgainLater;
            }
        }

        /// <summary>
        /// Atomic Redis record on TAKETHIS/IHAVE accept.
        /// </summary>
        /// <param name="digestKey">Digest key for memory insert on record.</param>
        /// <param name="now">Current UTC epoch seconds.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Record result.</returns>
        private async ValueTask<HistoryRecordResult> RecordRedisAsync(
            DigestKey digestKey,
            ulong now,
            CancellationToken cancellationToken)
        {
            ulong expiration = now + ((ulong)this._options.RememberDays * 86_400UL);
            int ttl = (int)Math.Max(1, expiration - now);
            try
            {
                Stopwatch sw = Stopwatch.StartNew();
                byte[] digestBytes = new byte[HistoryKeyEncoder.DigestLength];
                digestKey.CopyTo(digestBytes);
                int code = await this._redis.TryRecordAsync(digestBytes, now, expiration, ttl, cancellationToken)
                    .ConfigureAwait(false);
                this._metrics.RecordRecordRedisMilliseconds(sw.Elapsed.TotalMilliseconds);
                return this.MapRecordResult(code, digestBytes, expiration, digestKey);
            }
            catch (RedisUnavailableException ex)
            {
                this._logger.LogWarning(ex, "History Redis unavailable for record");
                this._metrics.RecordRecordTryAgain();
                return HistoryRecordResult.TryAgainLater;
            }
            catch (TimeoutException ex)
            {
                this._logger.LogWarning(ex, "History Redis timeout for record");
                this._metrics.RecordRecordTryAgain();
                return HistoryRecordResult.TryAgainLater;
            }
        }

        /// <summary>
        /// Maps CHECK probe Lua code to <see cref="HistoryCheckResult"/>.
        /// </summary>
        /// <param name="code">Lua return code.</param>
        /// <returns>Mapped result.</returns>
        private HistoryCheckResult MapCheckProbeResult(int code)
        {
            switch (code)
            {
                case 1:
                    this._metrics.RecordDuplicate();
                    return HistoryCheckResult.Duplicate;
                case 0:
                    this._metrics.RecordWanted();
                    return HistoryCheckResult.Wanted;
                default:
                    this._metrics.RecordTryAgain();
                    return HistoryCheckResult.TryAgainLater;
            }
        }

        /// <summary>
        /// Maps record Lua code to <see cref="HistoryRecordResult"/> and enqueues Rocks backfill.
        /// </summary>
        /// <param name="code">Lua return code.</param>
        /// <param name="digest">Digest bytes.</param>
        /// <param name="expiration">Expiration epoch.</param>
        /// <param name="digestKey">Digest key for memory insert.</param>
        /// <returns>Mapped result.</returns>
        private HistoryRecordResult MapRecordResult(
            int code,
            byte[] digest,
            ulong expiration,
            DigestKey digestKey)
        {
            switch (code)
            {
                case 1:
                    this._metrics.RecordRecordDuplicate();
                    return HistoryRecordResult.Duplicate;
                case 2:
                    this._metrics.RecordRecordTryAgain();
                    return HistoryRecordResult.TryAgainLater;
                case 0:
                    this._memory.InsertOrUpdate(in digestKey, expiration);
                    var item = new HistoryPersistItem(digest, expiration);
                    if (!this._queue.Writer.TryWrite(item))
                    {
                        this._metrics.RecordQueueDropped();
                        this._logger.LogError(
                            "History persist queue full; Rocks backfill dropped after Redis record (queue capacity {Capacity})",
                            this._options.QueueCapacity);
                    }

                    this._metrics.RecordRecorded();
                    return HistoryRecordResult.Recorded;
                default:
                    this._metrics.RecordRecordTryAgain();
                    return HistoryRecordResult.TryAgainLater;
            }
        }
    }
}
