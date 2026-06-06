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
        /// The first 8 bytes of the digest.
        /// </summary>
        private readonly ulong _w0;

        /// <summary>
        /// The second 8 bytes of the digest.
        /// </summary>
        private readonly ulong _w1;

        /// <summary>
        /// The third 8 bytes of the digest.
        /// </summary>
        private readonly ulong _w2;

        /// <summary>
        /// The fourth 8 bytes of the digest.
        /// </summary>
        private readonly ulong _w3;

        /// <summary>
        /// Initializes a new instance of the <see cref="DigestKey"/> struct.
        /// </summary>
        /// <param name="digest">BLAKE3 digest bytes.</param>
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
        /// <returns>The hash code for the current instance.</returns>
        public override int GetHashCode()
        {
            return HashCode.Combine(_w0, _w1, _w2, _w3);
        }

        /// <summary>
        /// Copies digest bytes into <paramref name="destination"/>.
        /// </summary>
        /// <param name="destination">32-byte span.</param>
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
