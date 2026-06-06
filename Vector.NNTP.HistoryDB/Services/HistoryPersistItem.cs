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
        /// <param name="digest">32-byte digest.</param>
        /// <param name="expirationEpochSeconds">Expiration epoch.</param>
        internal HistoryPersistItem(byte[] digest, ulong expirationEpochSeconds)
        {
            ArgumentOutOfRangeException.ThrowIfNotEqual(digest.Length, 32);
            Digest = digest;
            ExpirationEpochSeconds = expirationEpochSeconds;
        }

        /// <summary>
        /// Gets the digest bytes.
        /// </summary>
        public byte[] Digest { get; }

        /// <summary>
        /// Gets the expiration epoch seconds.
        /// </summary>
        public ulong ExpirationEpochSeconds { get; }
    }
}
