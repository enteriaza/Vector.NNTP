// <copyright file="HistoryKeyEncoderTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.HistoryDB.Encoding;

namespace Vector.NNTP.Tests.HistoryDB
{
    /// <summary>
    /// Unit tests for <see cref="HistoryKeyEncoder"/>.
    /// </summary>
    [TestFixture]
    public sealed class HistoryKeyEncoderTests
    {
        /// <summary>
        /// Verifies stable BLAKE3 digest for the same message-id.
        /// </summary>
        [Test]
        public void ComputeDigest_IsStable_ForSameMessageId()
        {
            const string messageId = "<stable@test.local>";
            Span<byte> a = stackalloc byte[32];
            Span<byte> b = stackalloc byte[32];
            Assert.That(HistoryKeyEncoder.TryComputeDigest(messageId, a), Is.True);
            Assert.That(HistoryKeyEncoder.TryComputeDigest(messageId, b), Is.True);
            Assert.That(a.ToArray(), Is.EqualTo(b.ToArray()));
        }

        /// <summary>
        /// Verifies lowercase hex encoding length and stability.
        /// </summary>
        [Test]
        public void EncodeHexLower_ProducesStableLowercaseHex()
        {
            const string messageId = "<hex@test.local>";
            string hex = HistoryKeyEncoder.EncodeHexLower(messageId);
            Assert.That(hex.Length, Is.EqualTo(HistoryKeyEncoder.DigestHexLength));
            Assert.That(hex, Is.EqualTo(HistoryKeyEncoder.EncodeHexLower(messageId)));
            Assert.That(hex, Is.EqualTo(hex.ToLowerInvariant()));
        }
    }
}
