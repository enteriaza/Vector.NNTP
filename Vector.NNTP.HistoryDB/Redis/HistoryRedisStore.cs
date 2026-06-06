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
    /// <param name="options">Options.</param>
    /// <param name="redis">Redis accessor.</param>
    /// <param name="metrics">Metrics.</param>
    /// <param name="logger">Logger for source-generated <c>[LoggerMessage]</c> methods.</param>
    internal sealed partial class HistoryRedisStore(
        IOptions<HistoryDbOptions> options,
        IRedisConnectionAccessor redis,
        HistoryMetrics metrics,
        ILogger<HistoryRedisStore> logger)
    {
        /// <summary>
        /// The options.
        /// </summary>
        private readonly HistoryDbOptions _options = options.Value;

        /// <summary>
        /// The Redis accessor.
        /// </summary>
        private readonly IRedisConnectionAccessor _redis = redis;

        /// <summary>
        /// The metrics.
        /// </summary>
        private readonly HistoryMetrics _metrics = metrics;

        /// <summary>
        /// Builds the Redis key for a digest.
        /// </summary>
        /// <param name="digest">32-byte digest.</param>
        /// <returns>Redis key.</returns>
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
        /// <returns>Lua result code (0 recorded, 1 duplicate).</returns>
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
        /// Pipelines SET for rebuild batch.
        /// </summary>
        /// <param name="items">Batch items.</param>
        /// <param name="nowEpoch">Now epoch.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
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
        /// <returns>Hash entries or empty.</returns>
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
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
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
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
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
