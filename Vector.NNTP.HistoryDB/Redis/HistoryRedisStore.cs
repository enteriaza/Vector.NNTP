// <copyright file="HistoryRedisStore.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Vector.NNTP.HistoryDB.Configuration;
using Vector.NNTP.HistoryDB.Encoding;
using Vector.NNTP.Session.Redis.Coordination;
using Vector.NNTP.Session.Redis.Exceptions;

namespace Vector.NNTP.HistoryDB.Redis
{
    /// <summary>
    /// Redis CHECK probe, record, metadata, rebuild state, and rebuild pipelining.
    /// </summary>
    internal sealed class HistoryRedisStore
    {
        /// <summary>
        /// The options.
        /// </summary>
        private readonly HistoryDbOptions _options;

        /// <summary>
        /// The Redis accessor.
        /// </summary>
        private readonly IRedisConnectionAccessor _redis;

        /// <summary>
        /// The logger.
        /// </summary>
        private readonly ILogger<HistoryRedisStore> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="HistoryRedisStore"/> class.
        /// </summary>
        /// <param name="options">Options.</param>
        /// <param name="redis">Redis accessor.</param>
        /// <param name="logger">Logger.</param>
        public HistoryRedisStore(
            IOptions<HistoryDbOptions> options,
            IRedisConnectionAccessor redis,
            ILogger<HistoryRedisStore> logger)
        {
            this._options = options.Value;
            this._redis = redis;
            this._logger = logger;
        }

        /// <summary>
        /// Builds the Redis key for a digest.
        /// </summary>
        /// <param name="digest">32-byte digest.</param>
        /// <returns>Redis key.</returns>
        public RedisKey BuildHistoryKey(byte[] digest)
        {
            ArgumentOutOfRangeException.ThrowIfNotEqual(digest.Length, HistoryKeyEncoder.DigestLength);
            byte[] keyBytes = GC.AllocateUninitializedArray<byte>(this._options.KeyPrefix.Length + 8 + digest.Length);
            int offset = 0;
            if (this._options.KeyPrefix.Length > 0)
            {
                offset = System.Text.Encoding.UTF8.GetBytes(this._options.KeyPrefix, keyBytes);
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
        public async ValueTask<int> CheckProbeAsync(
            byte[] digest,
            ulong nowEpoch,
            CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            IDatabase db = this._redis.GetDatabase();
            RedisKey key = this.BuildHistoryKey(digest);
            RedisKey[] keys = [key];
            RedisValue[] args = [nowEpoch.ToString()];

            Stopwatch sw = Stopwatch.StartNew();
            RedisResult result = await db.ScriptEvaluateAsync(HistoryRedisScripts.HistoryCheckV1, keys, args)
                .ConfigureAwait(false);
            if (sw.ElapsedMilliseconds > 50)
            {
                this._redis.SignalScaleUp();
            }

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
        public async ValueTask<int> TryRecordAsync(
            byte[] digest,
            ulong nowEpoch,
            ulong newExpiration,
            int ttlSeconds,
            CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            IDatabase db = this._redis.GetDatabase();
            RedisKey key = this.BuildHistoryKey(digest);
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
            if (sw.ElapsedMilliseconds > 50)
            {
                this._redis.SignalScaleUp();
            }

            return (int)(long)result;
        }

        /// <summary>
        /// Pipelines SET for rebuild batch.
        /// </summary>
        /// <param name="items">Batch items.</param>
        /// <param name="nowEpoch">Now epoch.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        public async Task PipelineSetBatchAsync(
            IReadOnlyList<(byte[] ExpirationKey, ulong Expiration, byte[] Digest)> items,
            ulong nowEpoch,
            CancellationToken cancellationToken)
        {
            IDatabase db = this._redis.GetDatabase();
            IBatch batch = db.CreateBatch();
            List<Task> tasks = new(items.Count);
            foreach ((byte[] _, ulong expiration, byte[] digest) in items)
            {
                int ttl = (int)Math.Max(1, expiration - nowEpoch);
                RedisKey key = this.BuildHistoryKey(digest);
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
        public async Task<HashEntry[]> GetRebuildStateAsync(CancellationToken cancellationToken)
        {
            IDatabase db = this._redis.GetDatabase();
            return await db.HashGetAllAsync(this.MetaKey("rebuild_state")).WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Writes rebuild checkpoint state.
        /// </summary>
        /// <param name="fields">Hash fields.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        public async Task SetRebuildStateAsync(HashEntry[] fields, CancellationToken cancellationToken)
        {
            IDatabase db = this._redis.GetDatabase();
            await db.HashSetAsync(this.MetaKey("rebuild_state"), fields).WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Updates history meta after rebuild.
        /// </summary>
        /// <param name="generation">Generation stamp.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        public async Task SetMetaAsync(ulong generation, CancellationToken cancellationToken)
        {
            IDatabase db = this._redis.GetDatabase();
            HashEntry[] fields =
            [
                new HashEntry("generation", generation.ToString()),
                new HashEntry("schemaVersion", HistoryDbOptions.CurrentSchemaVersion.ToString()),
            ];
            await db.HashSetAsync(this.MetaKey("meta"), fields).WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Builds a Redis metadata key.
        /// </summary>
        /// <param name="suffix">Metadata suffix.</param>
        /// <returns>Redis key.</returns>
        private RedisKey MetaKey(string suffix) => $"{this._options.KeyPrefix}history:{suffix}";
    }
}
