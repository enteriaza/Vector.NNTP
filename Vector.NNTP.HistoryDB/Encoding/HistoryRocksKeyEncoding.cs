// <copyright file="HistoryRocksKeyEncoding.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Buffers.Binary;

namespace Vector.NNTP.HistoryDB.Encoding
{
    /// <summary>
    /// Centralized RocksDB key/value encoding for history column families.
    /// </summary>
    internal static class HistoryRocksKeyEncoding
    {
        /// <summary>
        /// Length of a <c>by_expiration</c> key.
        /// </summary>
        public const int ExpirationKeyLength = 8 + HistoryKeyEncoder.DigestLength;

        /// <summary>
        /// Length of a <c>by_digest</c> value.
        /// </summary>
        public const int DigestValueLength = 8;

        /// <summary>
        /// Encodes <c>by_expiration</c> key: big-endian expiration + digest.
        /// </summary>
        /// <param name="expirationEpochSeconds">Expiration epoch (UTC seconds).</param>
        /// <param name="digest">32-byte digest.</param>
        /// <param name="destination">40-byte destination.</param>
        public static void EncodeExpirationKey(ulong expirationEpochSeconds, ReadOnlySpan<byte> digest, Span<byte> destination)
        {
            ArgumentOutOfRangeException.ThrowIfNotEqual(digest.Length, HistoryKeyEncoder.DigestLength);
            ArgumentOutOfRangeException.ThrowIfNotEqual(destination.Length, ExpirationKeyLength);
            BinaryPrimitives.WriteUInt64BigEndian(destination, expirationEpochSeconds);
            digest.CopyTo(destination[8..]);
        }

        /// <summary>
        /// Decodes a <c>by_expiration</c> key.
        /// </summary>
        /// <param name="key">Encoded key (40 bytes).</param>
        /// <param name="expirationEpochSeconds">Decoded expiration epoch.</param>
        /// <param name="digestDestination">Optional 32-byte buffer for digest copy.</param>
        /// <returns><see langword="true"/> when decoded.</returns>
        public static bool TryDecodeExpirationKey(
            ReadOnlySpan<byte> key,
            out ulong expirationEpochSeconds,
            Span<byte> digestDestination)
        {
            expirationEpochSeconds = 0;
            if (key.Length != ExpirationKeyLength)
            {
                return false;
            }

            expirationEpochSeconds = BinaryPrimitives.ReadUInt64BigEndian(key);
            if (digestDestination.Length >= HistoryKeyEncoder.DigestLength)
            {
                key[8..].CopyTo(digestDestination);
            }

            return true;
        }

        /// <summary>
        /// Encodes <c>by_digest</c> value as little-endian expiration epoch.
        /// </summary>
        /// <param name="expirationEpochSeconds">Expiration epoch.</param>
        /// <param name="destination">8-byte destination.</param>
        public static void EncodeDigestValue(ulong expirationEpochSeconds, Span<byte> destination)
        {
            ArgumentOutOfRangeException.ThrowIfNotEqual(destination.Length, DigestValueLength);
            BinaryPrimitives.WriteUInt64LittleEndian(destination, expirationEpochSeconds);
        }

        /// <summary>
        /// Decodes <c>by_digest</c> value.
        /// </summary>
        /// <param name="value">8-byte value.</param>
        /// <returns>Expiration epoch seconds.</returns>
        public static ulong DecodeDigestValue(ReadOnlySpan<byte> value)
        {
            ArgumentOutOfRangeException.ThrowIfNotEqual(value.Length, DigestValueLength);
            return BinaryPrimitives.ReadUInt64LittleEndian(value);
        }
    }
}
