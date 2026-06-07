// <copyright file="DigestKey.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.HistoryDB.Encoding
{
    /// <summary>
    /// Fixed-size 32-byte digest key for in-memory lookup without heap allocation.
    /// </summary>
    internal readonly struct DigestKey : IEquatable<DigestKey>
    {
        /// <summary>
        /// Little-endian digest words 0–7 stored as a <see cref="ulong"/> for equality and sharding.
        /// </summary>
        private readonly ulong _w0;

        /// <summary>
        /// Little-endian digest words 8–15 stored as a <see cref="ulong"/>.
        /// </summary>
        private readonly ulong _w1;

        /// <summary>
        /// Little-endian digest words 16–23 stored as a <see cref="ulong"/>.
        /// </summary>
        private readonly ulong _w2;

        /// <summary>
        /// Little-endian digest words 24–31 stored as a <see cref="ulong"/>.
        /// </summary>
        private readonly ulong _w3;

        /// <summary>
        /// Initializes a new instance of the <see cref="DigestKey"/> struct from a 32-byte BLAKE3 digest.
        /// </summary>
        /// <param name="digest">Exactly <see cref="HistoryKeyEncoder.DigestLength"/> BLAKE3 digest bytes.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="digest"/> length is not 32.</exception>
        internal DigestKey(ReadOnlySpan<byte> digest)
        {
            ArgumentOutOfRangeException.ThrowIfNotEqual(digest.Length, HistoryKeyEncoder.DigestLength);
            _w0 = BitConverter.ToUInt64(digest);
            _w1 = BitConverter.ToUInt64(digest[8..]);
            _w2 = BitConverter.ToUInt64(digest[16..]);
            _w3 = BitConverter.ToUInt64(digest[24..]);
        }

        /// <summary>
        /// Checks if the current instance is equal to another instance.
        /// </summary>
        /// <param name="other">The other instance to compare to.</param>
        /// <returns>True if the instances are equal, false otherwise.</returns>
        public bool Equals(DigestKey other)
        {
            return _w0 == other._w0 &&
            _w1 == other._w1 &&
            _w2 == other._w2 &&
            _w3 == other._w3;
        }

        /// <summary>
        /// Checks if the current instance is equal to another instance.
        /// </summary>
        /// <param name="obj">The other instance to compare to.</param>
        /// <returns>True if the instances are equal, false otherwise.</returns>
        public override bool Equals(object? obj)
        {
            return obj is DigestKey other && Equals(other);
        }

        /// <summary>
        /// Gets the hash code for the current instance.
        /// </summary>
        /// <returns>Combined hash of the four little-endian digest words.</returns>
        public override int GetHashCode()
        {
            return HashCode.Combine(_w0, _w1, _w2, _w3);
        }

        /// <summary>
        /// Gets a shard index from the low bits of the digest (power-of-2 <paramref name="shardMask"/>).
        /// </summary>
        /// <param name="shardMask"><c>shardCount - 1</c> where <c>shardCount</c> is a power of two.</param>
        /// <returns>Shard index in <c>[0, shardMask]</c>.</returns>
        internal int GetShardIndex(int shardMask)
        {
            return (int)(_w0 & (uint)shardMask);
        }

        /// <summary>
        /// Copies the 32-byte digest into <paramref name="destination"/> in little-endian word order.
        /// </summary>
        /// <param name="destination">Exactly <see cref="HistoryKeyEncoder.DigestLength"/> bytes.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="destination"/> length is not 32.</exception>
        public void CopyTo(Span<byte> destination)
        {
            ArgumentOutOfRangeException.ThrowIfNotEqual(destination.Length, HistoryKeyEncoder.DigestLength);
            _ = BitConverter.TryWriteBytes(destination, _w0);
            _ = BitConverter.TryWriteBytes(destination[8..], _w1);
            _ = BitConverter.TryWriteBytes(destination[16..], _w2);
            _ = BitConverter.TryWriteBytes(destination[24..], _w3);
        }
    }
}
