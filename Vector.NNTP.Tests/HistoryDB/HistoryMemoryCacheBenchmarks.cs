// <copyright file="HistoryMemoryCacheBenchmarks.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: BenchmarkDotNet comparison of single-shard vs 64-shard HistoryMemoryCache.

using BenchmarkDotNet.Attributes;
using Vector.NNTP.HistoryDB.Encoding;
using Vector.NNTP.HistoryDB.Memory;
using Vector.NNTP.HistoryDB.Metrics;

namespace Vector.NNTP.Tests.HistoryDB
{
    /// <summary>
    /// Benchmarks <see cref="HistoryMemoryCache"/> at transit-like CHECK/TAKETHIS concurrency.
    /// </summary>
    [MemoryDiagnoser]
    [Category("Benchmark")]
    public sealed class HistoryMemoryCacheBenchmarks
    {
        private const int KeyCount = 10_000;
        private const int OpsPerInvoke = 100_000;

        private HistoryMetrics _metrics = null!;
        private HistoryMemoryCache _cache = null!;
        private DigestKey[] _keys = null!;
        private ulong _now;

        /// <summary>
        /// Gets or sets shard count under test (1 = single monitor baseline).
        /// </summary>
        [Params(1, 64)]
        public int ShardCount { get; set; }

        /// <summary>
        /// Configures cache and seeded keys.
        /// </summary>
        [GlobalSetup]
        public void Setup()
        {
            this._metrics = new HistoryMetrics();
            this._cache = new HistoryMemoryCache(1_073_741_824, this.ShardCount, this._metrics);
            this._now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            this._keys = new DigestKey[KeyCount];
            byte[] digest = new byte[HistoryKeyEncoder.DigestLength];
            for (int i = 0; i < KeyCount; i++)
            {
                Span<byte> digestSpan = digest;
                _ = BitConverter.TryWriteBytes(digestSpan, (ulong)i);
                _ = BitConverter.TryWriteBytes(digestSpan[8..], (ulong)(i * 31));
                _ = BitConverter.TryWriteBytes(digestSpan[16..], (ulong)(i * 17));
                _ = BitConverter.TryWriteBytes(digestSpan[24..], (ulong)(i * 13));
                this._keys[i] = new DigestKey(digestSpan);
                if (i % 10 != 0)
                {
                    this._cache.InsertOrUpdate(in this._keys[i], this._now + 3600);
                }
            }
        }

        /// <summary>
        /// Parallel read-heavy workload (~90% cache hits).
        /// </summary>
        [Benchmark(OperationsPerInvoke = OpsPerInvoke)]
        public void TryGetDuplicate_MixedHits()
        {
            Parallel.For(0, OpsPerInvoke, i =>
            {
                DigestKey key = this._keys[i % KeyCount];
                _ = this._cache.TryGetDuplicate(in key, this._now);
            });
        }

        /// <summary>
        /// Parallel sustained inserts with unique digests.
        /// </summary>
        [Benchmark(OperationsPerInvoke = OpsPerInvoke)]
        public void InsertOrUpdate_Sustained()
        {
            Parallel.For(
                0,
                OpsPerInvoke,
                () => new byte[HistoryKeyEncoder.DigestLength],
                (i, loopState, digestBytes) =>
                {
                    Span<byte> digest = digestBytes;
                    _ = BitConverter.TryWriteBytes(digest, (ulong)i);
                    _ = BitConverter.TryWriteBytes(digest[8..], (ulong)(i >> 16));
                    var key = new DigestKey(digest);
                    this._cache.InsertOrUpdate(in key, this._now + (ulong)(i % 10_000));
                    return digestBytes;
                },
                static _ => { });
        }

        /// <summary>
        /// Transit-shaped mix: 80% CHECK reads, 20% TAKETHIS writes.
        /// </summary>
        [Benchmark(OperationsPerInvoke = OpsPerInvoke)]
        public void MixedReadWrite_80_20()
        {
            Parallel.For(
                0,
                OpsPerInvoke,
                () => new byte[HistoryKeyEncoder.DigestLength],
                (i, loopState, digestBytes) =>
                {
                    if ((i & 3) == 0)
                    {
                        Span<byte> digest = digestBytes;
                        _ = BitConverter.TryWriteBytes(digest, (ulong)(i + KeyCount));
                        _ = BitConverter.TryWriteBytes(digest[8..], (ulong)((i + KeyCount) >> 8));
                        var key = new DigestKey(digest);
                        this._cache.InsertOrUpdate(in key, this._now + (ulong)(i % 5000));
                    }
                    else
                    {
                        DigestKey key = this._keys[i % KeyCount];
                        _ = this._cache.TryGetDuplicate(in key, this._now);
                    }

                    return digestBytes;
                },
                static _ => { });
        }
    }
}
