// <copyright file="HistoryRocksKeyEncodingTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.HistoryDB.Encoding;

namespace Vector.NNTP.Tests.HistoryDB
{
    /// <summary>
    /// Unit tests for <see cref="HistoryRocksKeyEncoding"/>.
    /// </summary>
    [TestFixture]
    public sealed class HistoryRocksKeyEncodingTests
    {
        /// <summary>
        /// Verifies expiration key round-trip encoding.
        /// </summary>
        [Test]
        public void ExpirationKey_RoundTrips_BigEndianExpiration()
        {
            Span<byte> digest = stackalloc byte[32];
            digest.Fill(0xAB);
            ulong expiration = 1_700_000_000UL;
            Span<byte> key = stackalloc byte[HistoryRocksKeyEncoding.ExpirationKeyLength];
            HistoryRocksKeyEncoding.EncodeExpirationKey(expiration, digest, key);
            Assert.That(HistoryRocksKeyEncoding.TryDecodeExpirationKey(key, out ulong decoded, stackalloc byte[32]), Is.True);
            Assert.That(decoded, Is.EqualTo(expiration));
        }

        /// <summary>
        /// Verifies digest value little-endian encoding.
        /// </summary>
        [Test]
        public void DigestValue_RoundTrips_LittleEndian()
        {
            ulong expiration = 1_800_000_000UL;
            Span<byte> value = stackalloc byte[8];
            HistoryRocksKeyEncoding.EncodeDigestValue(expiration, value);
            Assert.That(HistoryRocksKeyEncoding.DecodeDigestValue(value), Is.EqualTo(expiration));
        }
    }
}
