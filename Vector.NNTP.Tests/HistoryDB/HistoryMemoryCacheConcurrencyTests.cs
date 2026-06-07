// <copyright file="HistoryMemoryCacheConcurrencyTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.HistoryDB.Encoding;
using Vector.NNTP.HistoryDB.Memory;
using Vector.NNTP.HistoryDB.Metrics;

namespace Vector.NNTP.Tests.HistoryDB
{
    /// <summary>
    /// Verifies <see cref="HistoryMemoryCache"/> survives concurrent CHECK and record access.
    /// </summary>
    [TestFixture]
    public sealed class HistoryMemoryCacheConcurrencyTests
    {
        /// <summary>
        /// Parallel readers and writers must not corrupt the non-concurrent dictionary backing store.
        /// </summary>
        [Test]
        public void ConcurrentTryGetDuplicateAndInsertOrUpdate_DoesNotThrow()
        {
            var metrics = new HistoryMetrics();
            var cache = new HistoryMemoryCache(1_073_741_824, metrics);
            ulong now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            const int threadCount = 16;
            const int iterationsPerThread = 5_000;
            using var startGate = new Barrier(threadCount);
            Exception? fault = null;

            Thread[] threads = new Thread[threadCount];
            for (int t = 0; t < threadCount; t++)
            {
                int threadIndex = t;
                threads[t] = new Thread(() =>
                {
                    try
                    {
                        startGate.SignalAndWait();
                        for (int i = 0; i < iterationsPerThread; i++)
                        {
                            DigestKey key = CreateKey((byte)(threadIndex + 1), (byte)(i & 0xFF));
                            if ((i & 3) == 0)
                            {
                                cache.InsertOrUpdate(in key, now + (ulong)(threadIndex + i + 1));
                            }
                            else
                            {
                                _ = cache.TryGetDuplicate(in key, now);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Interlocked.CompareExchange(ref fault, ex, null);
                    }
                });
                threads[t].Start();
            }

            foreach (Thread thread in threads)
            {
                thread.Join();
            }

            Assert.That(fault, Is.Null, fault?.ToString());
            Assert.That(cache.Count, Is.GreaterThan(0));
        }

        /// <summary>
        /// Builds a digest key from thread and sequence bytes.
        /// </summary>
        /// <param name="threadByte">Thread discriminator.</param>
        /// <param name="sequenceByte">Iteration discriminator.</param>
        /// <returns>Digest key.</returns>
        private static DigestKey CreateKey(byte threadByte, byte sequenceByte)
        {
            Span<byte> digest = stackalloc byte[32];
            digest.Fill(threadByte);
            digest[31] = sequenceByte;
            return new DigestKey(digest);
        }
    }
}
