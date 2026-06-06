// <copyright file="HistoryDatabaseService.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Vector.NNTP.HistoryDB.Abstractions;
using Vector.NNTP.HistoryDB.Configuration;
using Vector.NNTP.HistoryDB.Encoding;
using Vector.NNTP.HistoryDB.Memory;
using Vector.NNTP.HistoryDB.Metrics;
using Vector.NNTP.HistoryDB.Redis;
using Vector.NNTP.HistoryDB.Telemetry;
using Vector.NNTP.Session.Redis.Exceptions;

namespace Vector.NNTP.HistoryDB.Services
{
    /// <summary>
    /// Transit history: read-only CHECK probe and TAKETHIS/IHAVE record with async Rocks backfill.
    /// </summary>
    internal sealed partial class HistoryDatabaseService : IHistoryDatabase
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
        /// Approximate persist queue depth for metrics.
        /// </summary>
        private long _queueDepth;

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
        internal HistoryDatabaseService(
            IOptions<HistoryDbOptions> options,
            HistoryMemoryCache memory,
            HistoryRedisStore redis,
            HistoryMetrics metrics,
            HistoryRocksPersistPump persistPump,
            IHostApplicationLifetime lifetime,
            ILogger<HistoryDatabaseService> logger)
        {
            _options = options.Value;
            _memory = memory;
            _redis = redis;
            _metrics = metrics;
            _persistPump = persistPump;
            _lifetime = lifetime;
            _logger = logger;
            BoundedChannelOptions channelOptions = new(_options.QueueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
                SingleWriter = false,
            };
            _queue = Channel.CreateBounded<HistoryPersistItem>(channelOptions);
            _ = _lifetime.ApplicationStopping.Register(() => _queue.Writer.TryComplete());
            _metrics.SetOperational(false);
            _metrics.SetQueueDepth(0);
        }

        /// <summary>
        /// Gets a value indicating whether CHECK may proceed against Redis.
        /// </summary>
        internal bool IsOperational => _operational;

        /// <summary>
        /// Gets the persist queue for the background worker.
        /// </summary>
        internal ChannelReader<HistoryPersistItem> PersistReader => _queue.Reader;

        /// <summary>
        /// Marks the database operational after startup rebuild and starts the Rocks persist pump.
        /// </summary>
        internal void SetOperational()
        {
            _operational = true;
            _metrics.SetOperational(true);
            _persistPump.Start(_queue.Reader, _lifetime.ApplicationStopping, this);
        }

        /// <summary>
        /// Notifies the service that a persist queue item was written to RocksDB.
        /// </summary>
        internal void NotifyPersistDequeued()
        {
            long depth = Interlocked.Decrement(ref _queueDepth);
            if (depth < 0)
            {
                _ = Interlocked.Exchange(ref _queueDepth, 0);
                depth = 0;
            }

            _metrics.SetQueueDepth(depth);
        }

        /// <summary>
        /// Checks if the message ID is a duplicate.
        /// </summary>
        /// <param name="messageId">The message ID to check.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The check result.</returns>
        public ValueTask<HistoryCheckResult> CheckAsync(string messageId, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(messageId);
            if (!_operational)
            {
                _metrics.RecordUnavailable();
                LogCheckNotOperational();
                return new ValueTask<HistoryCheckResult>(HistoryCheckResult.Unavailable);
            }

            Span<byte> digest = stackalloc byte[HistoryKeyEncoder.DigestLength];
            if (!HistoryKeyEncoder.TryComputeDigest(messageId, digest))
            {
                _metrics.RecordTryAgain();
                return new ValueTask<HistoryCheckResult>(HistoryCheckResult.TryAgainLater);
            }

            DigestKey digestKey = new(digest);
            ulong now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (_memory.TryGetDuplicate(in digestKey, now))
            {
                RecordCheckDuplicate();
                return new ValueTask<HistoryCheckResult>(HistoryCheckResult.Duplicate);
            }

            return CheckRedisProbeAsync(digestKey, now, cancellationToken);
        }

        /// <summary>
        /// Tries to record the message ID as a TAKETHIS/IHAVE record.
        /// </summary>
        /// <param name="messageId">The message ID to record.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The record result.</returns>
        public ValueTask<HistoryRecordResult> TryRecordAsync(string messageId, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(messageId);
            if (!_operational)
            {
                _metrics.RecordRecordUnavailable();
                return new ValueTask<HistoryRecordResult>(HistoryRecordResult.Unavailable);
            }

            Span<byte> digest = stackalloc byte[HistoryKeyEncoder.DigestLength];
            if (!HistoryKeyEncoder.TryComputeDigest(messageId, digest))
            {
                _metrics.RecordRecordTryAgain();
                return new ValueTask<HistoryRecordResult>(HistoryRecordResult.TryAgainLater);
            }

            DigestKey digestKey = new(digest);
            ulong now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (_memory.TryGetDuplicate(in digestKey, now))
            {
                _metrics.RecordRecordDuplicate();
                return new ValueTask<HistoryRecordResult>(HistoryRecordResult.Duplicate);
            }

            return RecordRedisAsync(digestKey, now, cancellationToken);
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
            if (!_queue.Writer.TryWrite(new HistoryPersistItem(digestBytes, expirationEpochSeconds)))
            {
                return false;
            }

            IncrementQueueDepth();
            return true;
        }

        /// <summary>
        /// Records a terminal CHECK duplicate and increments the total processed counter.
        /// </summary>
        private void RecordCheckDuplicate()
        {
            _metrics.RecordDuplicate();
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
            _metrics.RecordRedisProbe();
            using Activity? activity = HistoryDbTelemetry.ActivitySource.StartActivity("history.check.redis");
            try
            {
                Stopwatch sw = Stopwatch.StartNew();
                byte[] digestBytes = new byte[HistoryKeyEncoder.DigestLength];
                digestKey.CopyTo(digestBytes);
                int code = await _redis.CheckProbeAsync(digestBytes, now, cancellationToken)
                    .ConfigureAwait(false);
                _metrics.RecordRedisMilliseconds(sw.Elapsed.TotalMilliseconds);
                return MapCheckProbeResult(code);
            }
            catch (RedisUnavailableException ex)
            {
                LogCheckRedisUnavailable(ex);
                _metrics.RecordTryAgain();
                return HistoryCheckResult.TryAgainLater;
            }
            catch (TimeoutException ex)
            {
                LogCheckRedisTimeout(ex);
                _metrics.RecordTryAgain();
                return HistoryCheckResult.TryAgainLater;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _metrics.RecordTryAgain();
                throw;
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
            ulong expiration = now + ((ulong)_options.RememberDays * 86_400UL);
            int ttl = (int)Math.Max(1, expiration - now);
            using Activity? activity = HistoryDbTelemetry.ActivitySource.StartActivity("history.record.redis");
            try
            {
                Stopwatch sw = Stopwatch.StartNew();
                byte[] digestBytes = new byte[HistoryKeyEncoder.DigestLength];
                digestKey.CopyTo(digestBytes);
                int code = await _redis.TryRecordAsync(digestBytes, now, expiration, ttl, cancellationToken)
                    .ConfigureAwait(false);
                _metrics.RecordRecordRedisMilliseconds(sw.Elapsed.TotalMilliseconds);
                return MapRecordResult(code, digestBytes, expiration, digestKey);
            }
            catch (RedisUnavailableException ex)
            {
                LogRecordRedisUnavailable(ex);
                _metrics.RecordRecordTryAgain();
                return HistoryRecordResult.TryAgainLater;
            }
            catch (TimeoutException ex)
            {
                LogRecordRedisTimeout(ex);
                _metrics.RecordRecordTryAgain();
                return HistoryRecordResult.TryAgainLater;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _metrics.RecordRecordTryAgain();
                throw;
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
                    _metrics.RecordRedisDuplicate();
                    RecordCheckDuplicate();
                    return HistoryCheckResult.Duplicate;
                case 0:
                    _metrics.RecordRedisWanted();
                    _metrics.RecordWanted();
                    return HistoryCheckResult.Wanted;
                default:
                    _metrics.RecordTryAgain();
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
                    _metrics.RecordRecordDuplicate();
                    return HistoryRecordResult.Duplicate;
                case 2:
                    _metrics.RecordRecordTryAgain();
                    return HistoryRecordResult.TryAgainLater;
                case 0:
                    _memory.InsertOrUpdate(in digestKey, expiration);
                    HistoryPersistItem item = new(digest, expiration);
                    if (!_queue.Writer.TryWrite(item))
                    {
                        _metrics.RecordQueueDropped();
                        LogPersistQueueFull(_options.QueueCapacity);
                    }
                    else
                    {
                        IncrementQueueDepth();
                    }

                    _metrics.RecordRecorded();
                    return HistoryRecordResult.Recorded;
                default:
                    _metrics.RecordRecordTryAgain();
                    return HistoryRecordResult.TryAgainLater;
            }
        }

        /// <summary>
        /// Increments the approximate persist queue depth gauge.
        /// </summary>
        private void IncrementQueueDepth()
        {
            long depth = Interlocked.Increment(ref _queueDepth);
            _metrics.SetQueueDepth(depth);
        }
    }
}
