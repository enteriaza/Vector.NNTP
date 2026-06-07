// <copyright file="HistoryReleaseTombstoneSet.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Collections.Concurrent;
using Vector.NNTP.HistoryDB.Encoding;

namespace Vector.NNTP.HistoryDB.Services
{
    /// <summary>
    /// Process-wide digest tombstones that suppress Rocks persist for released message-ids still queued.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Registered as a singleton. <see cref="HistoryDatabaseService.TryReleaseAsync"/> registers tombstones before
    /// Redis/memory delete; <see cref="HistoryRocksPersistPump"/> skips tombstoned items.
    /// </para>
    /// </remarks>
    internal sealed class HistoryReleaseTombstoneSet
    {
        /// <summary>
        /// Tombstoned digests keyed by <see cref="DigestKey"/> equality.
        /// </summary>
        private readonly ConcurrentDictionary<DigestKey, byte> _tombstones = new();

        /// <summary>
        /// Registers a digest tombstone before cross-tier release begins.
        /// </summary>
        /// <param name="digestKey">Digest key to tombstone.</param>
        internal void Add(in DigestKey digestKey)
        {
            _ = _tombstones.TryAdd(digestKey, 0);
        }

        /// <summary>
        /// Returns whether <paramref name="digestKey"/> is tombstoned and must not be persisted.
        /// </summary>
        /// <param name="digestKey">Digest key to test.</param>
        /// <returns><see langword="true"/> when persist must be skipped.</returns>
        internal bool IsTombstoned(in DigestKey digestKey)
        {
            return _tombstones.ContainsKey(digestKey);
        }

        /// <summary>
        /// Returns whether <paramref name="digest"/> bytes are tombstoned.
        /// </summary>
        /// <param name="digest">32-byte digest.</param>
        /// <returns><see langword="true"/> when persist must be skipped.</returns>
        internal bool IsTombstoned(ReadOnlySpan<byte> digest)
        {
            return IsTombstoned(new DigestKey(digest));
        }

        /// <summary>
        /// Clears a tombstone after Rocks delete completes or when release determines the digest was absent.
        /// </summary>
        /// <param name="digestKey">Digest key to clear.</param>
        internal void Remove(in DigestKey digestKey)
        {
            _ = _tombstones.TryRemove(digestKey, out _);
        }
    }
}
