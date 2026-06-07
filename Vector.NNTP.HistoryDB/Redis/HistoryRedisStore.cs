// <copyright file="HistoryRedisStore.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Diagnostics;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Vector.NNTP.HistoryDB.Configuration;
using Vector.NNTP.HistoryDB.Encoding;
using Vector.NNTP.HistoryDB.Metrics;
using Vector.NNTP.Session.Redis.Coordination;

namespace Vector.NNTP.HistoryDB.Redis
{
    /// <summary>
    /// Redis CHECK probe, record, metadata, rebuild state, and rebuild pipelining.
    /// </summary>
    /// <param name="logger">Logger for source-generated <c>[LoggerMessage]</c> methods.</param>
    internal sealed partial class HistoryRedisStore(
        ILogger<HistoryRedisStore> logger)
    {
        /// <summary>
        /// Bound <see cref="HistoryDbOptions"/> including key prefix and rebuild batch sizing.
        /// </summary>
        private readonly HistoryDbOptions _options = null!;

        /// <summary>
        /// Session Redis accessor for Lua script evaluation and pipelined rebuild writes.
        /// </summary>
        private readonly IRedisConnectionAccessor _redis = null!;

        /// <summary>
        /// HistoryDB metrics recorder for slow Redis operations and rebuild counters.
        /// </summary>
        private readonly HistoryMetrics _metrics = null!;

        /// <summary>
        /// Initializes a new instance of the <see cref="HistoryRedisStore"/> class.
        /// </summary>
        /// <param name="options">Options.</param>
        /// <param name="redis">Redis accessor.</param>
        /// <param name="metrics">Metrics.</param>
        /// <param name="logger">Logger for source-generated <c>[LoggerMessage]</c> methods.</param>
        public HistoryRedisStore(
            IOptions<HistoryDbOptions> options,
            IRedisConnectionAccessor redis,
            HistoryMetrics metrics,
            ILogger<HistoryRedisStore> logger)
            : this(logger)
        {
            _options = options.Value;
            _redis = redis;
            _metrics = metrics;
        }

        /// <summary>
        /// Builds the Redis key for a digest.
        /// </summary>
        /// <param name="digest">32-byte digest.</param>
        /// <returns>Prefixed Redis key in the form <c>{KeyPrefix}history:{digest}</c>.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="digest"/> length is not 32 bytes.</exception>
        internal RedisKey BuildHistoryKey(byte[] digest)
        {
            ArgumentOutOfRangeException.ThrowIfNotEqual(digest.Length, HistoryKeyEncoder.DigestLength);
            byte[] keyBytes = GC.AllocateUninitializedArray<byte>(_options.KeyPrefix.Length + 8 + digest.Length);
            int offset = 0;
            if (_options.KeyPrefix.Length > 0)
            {
                offset = System.Text.Encoding.UTF8.GetBytes(_options.KeyPrefix, keyBytes);
            }

            "history:"u8.CopyTo(keyBytes.AsSpan(offset));
            offset += 8;
            digest.CopyTo(keyBytes.AsSpan(offset));
            return keyBytes;
        }

        /// <summary>
        /// Evaluates read-only CHECK probe script.
        /// </summary>
        /// <param name="digest">Digest bytes.</param>
        /// <param name="nowEpoch">Now epoch seconds.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Lua result code (0 wanted, 1 duplicate).</returns>
        /// <remarks>
        /// <paramref name="cancellationToken"/> is not passed to StackExchange.Redis today; cancellation applies only
        /// before the call is made.
        /// </remarks>
        internal async ValueTask<int> CheckProbeAsync(
            byte[] digest,
            ulong nowEpoch,
            CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            IDatabase db = _redis.GetDatabase();
            RedisKey key = BuildHistoryKey(digest);
            RedisKey[] keys = [key];
            RedisValue[] args = [nowEpoch.ToString()];

            Stopwatch sw = Stopwatch.StartNew();
            RedisResult result = await db.ScriptEvaluateAsync(HistoryRedisScripts.HistoryCheckV1, keys, args)
                .ConfigureAwait(false);
            MaybeLogSlowRedis("check", sw.ElapsedMilliseconds);
            return (int)(long)result;
        }

        /// <summary>
        /// Evaluates atomic record script on TAKETHIS/IHAVE accept.
        /// </summary>
        /// <param name="digest">Digest bytes.</param>
        /// <param name="nowEpoch">Now epoch seconds.</param>
        /// <param name="newExpiration">New expiration epoch.</param>
        /// <param name="ttlSeconds">TTL seconds.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Lua result code (0 recorded, 1 duplicate, 2 try-again).</returns>
        /// <remarks>
        /// <paramref name="cancellationToken"/> is not passed to StackExchange.Redis today; cancellation applies only
        /// before the call is made.
        /// </remarks>
        internal async ValueTask<int> TryRecordAsync(
            byte[] digest,
            ulong nowEpoch,
            ulong newExpiration,
            int ttlSeconds,
            CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            IDatabase db = _redis.GetDatabase();
            RedisKey key = BuildHistoryKey(digest);
            RedisKey[] keys = [key];
            RedisValue[] args =
            [
                nowEpoch.ToString(),
                newExpiration.ToString(),
                ttlSeconds.ToString(),
            ];

            Stopwatch sw = Stopwatch.StartNew();
            RedisResult result = await db.ScriptEvaluateAsync(HistoryRedisScripts.HistoryRecordV1, keys, args)
                .ConfigureAwait(false);
            MaybeLogSlowRedis("record", sw.ElapsedMilliseconds);
            return (int)(long)result;
        }

        /// <summary>
        /// Deletes a history key on spool failure release.
        /// </summary>
        /// <param name="digest">Digest bytes.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Lua result code (0 released, 1 not found).</returns>
        /// <remarks>
        /// <paramref name="cancellationToken"/> is not passed to StackExchange.Redis today; cancellation applies only
        /// before the call is made.
        /// </remarks>
        internal async ValueTask<int> TryReleaseAsync(
            byte[] digest,
            CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            IDatabase db = _redis.GetDatabase();
            RedisKey key = BuildHistoryKey(digest);
            RedisKey[] keys = [key];

            Stopwatch sw = Stopwatch.StartNew();
            RedisResult result = await db.ScriptEvaluateAsync(HistoryRedisScripts.HistoryReleaseV1, keys)
                .ConfigureAwait(false);
            MaybeLogSlowRedis("release", sw.ElapsedMilliseconds);
            return (int)(long)result;
        }

        /// <summary>
        /// Pipelines SET for rebuild batch.
        /// </summary>
        /// <param name="items">Batch items.</param>
        /// <param name="nowEpoch">Now epoch.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that completes when all pipelined <c>SET</c> operations finish.</returns>
        /// <exception cref="OperationCanceledException">Propagated when <paramref name="cancellationToken"/> is canceled during batch wait.</exception>
        internal async Task PipelineSetBatchAsync(
            IReadOnlyList<(byte[] ExpirationKey, ulong Expiration, byte[] Digest)> items,
            ulong nowEpoch,
            CancellationToken cancellationToken)
        {
            IDatabase db = _redis.GetDatabase();
            IBatch batch = db.CreateBatch();
            List<Task> tasks = new(items.Count);
            foreach ((byte[] _, ulong expiration, byte[] digest) in items)
            {
                int ttl = (int)Math.Max(1, expiration - nowEpoch);
                RedisKey key = BuildHistoryKey(digest);
                Task t = batch.StringSetAsync(key, expiration.ToString(), TimeSpan.FromSeconds(ttl));
                tasks.Add(t);
            }

            batch.Execute();
            await Task.WhenAll(tasks).WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets rebuild state hash.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>All fields from the <c>rebuild_state</c> meta hash, or an empty array when the key is absent.</returns>
        /// <exception cref="OperationCanceledException">Propagated when <paramref name="cancellationToken"/> is canceled.</exception>
        internal async Task<HashEntry[]> GetRebuildStateAsync(CancellationToken cancellationToken)
        {
            IDatabase db = _redis.GetDatabase();
            return await db.HashGetAllAsync(MetaKey("rebuild_state")).WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Writes rebuild checkpoint state.
        /// </summary>
        /// <param name="fields">Hash fields.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that completes when the rebuild state hash is written.</returns>
        /// <exception cref="OperationCanceledException">Propagated when <paramref name="cancellationToken"/> is canceled.</exception>
        internal async Task SetRebuildStateAsync(HashEntry[] fields, CancellationToken cancellationToken)
        {
            IDatabase db = _redis.GetDatabase();
            await db.HashSetAsync(MetaKey("rebuild_state"), fields).WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Updates history meta after rebuild.
        /// </summary>
        /// <param name="generation">Generation stamp.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that completes when generation and schema version metadata are written.</returns>
        /// <exception cref="OperationCanceledException">Propagated when <paramref name="cancellationToken"/> is canceled.</exception>
        internal async Task SetMetaAsync(ulong generation, CancellationToken cancellationToken)
        {
            IDatabase db = _redis.GetDatabase();
            HashEntry[] fields =
            [
                new HashEntry("generation", generation.ToString()),
                new HashEntry("schemaVersion", HistoryDbOptions.CurrentSchemaVersion.ToString()),
            ];
            await db.HashSetAsync(MetaKey("meta"), fields).WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Logs and records metrics when a Redis Lua call exceeds the slow threshold.
        /// </summary>
        /// <param name="operation">Operation label.</param>
        /// <param name="elapsedMs">Elapsed milliseconds.</param>
        private void MaybeLogSlowRedis(string operation, long elapsedMs)
        {
            if (elapsedMs <= SlowRedisThresholdMs)
            {
                return;
            }

            _metrics.RecordRedisSlowCall();
            _redis.SignalScaleUp();
            LogSlowRedisCall(operation, elapsedMs);
        }

        /// <summary>
        /// Builds a Redis metadata key.
        /// </summary>
        /// <param name="suffix">Metadata suffix.</param>
        /// <returns>Redis key.</returns>
        private RedisKey MetaKey(string suffix)
        {
            return $"{_options.KeyPrefix}history:{suffix}";
        }
    }
}
