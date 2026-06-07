// <copyright file="HistoryPersistItem.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.HistoryDB.Services
{
    /// <summary>
    /// Queue payload for RocksDB backfill after Redis reserve.
    /// </summary>
    internal readonly struct HistoryPersistItem
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="HistoryPersistItem"/> struct.
        /// </summary>
        /// <param name="digest">32-byte BLAKE3 digest key copied to the Rocks persist queue.</param>
        /// <param name="expirationEpochSeconds">UTC epoch seconds stored in the Rocks <c>by_expiration</c> index.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="digest"/> length is not 32 bytes.</exception>
        internal HistoryPersistItem(byte[] digest, ulong expirationEpochSeconds)
        {
            ArgumentOutOfRangeException.ThrowIfNotEqual(digest.Length, 32);
            Digest = digest;
            ExpirationEpochSeconds = expirationEpochSeconds;
        }

        /// <summary>
        /// 32-byte BLAKE3 digest used as the Rocks <c>by_digest</c> key.
        /// </summary>
        public byte[] Digest { get; }

        /// <summary>
        /// UTC expiration epoch seconds paired with <see cref="Digest"/> in Rocks column families.
        /// </summary>
        public ulong ExpirationEpochSeconds { get; }
    }
}
