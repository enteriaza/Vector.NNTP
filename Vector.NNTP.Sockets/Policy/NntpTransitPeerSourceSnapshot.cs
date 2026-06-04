// <copyright file="NntpTransitPeerSourceSnapshot.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: immutable expanded transit peer sources for hot-path matching.

using System.Collections.Immutable;
using Vector.NNTP.Sockets.Configuration;

namespace Vector.NNTP.Sockets.Policy
{
    /// <summary>
    /// One expanded network source tagged with peer identity for linear matching.
    /// </summary>
    internal readonly struct NntpTransitPeerSourceEntry
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NntpTransitPeerSourceEntry"/> struct.
        /// </summary>
        /// <param name="peerId">Stable peer identifier.</param>
        /// <param name="displayName">Display name.</param>
        /// <param name="configEntry">Original configuration entry text.</param>
        /// <param name="maxConnections">Configured connection cap.</param>
        /// <param name="source">Parsed network source.</param>
        public NntpTransitPeerSourceEntry(
            string peerId,
            string displayName,
            string configEntry,
            int maxConnections,
            NntpNetworkSource source)
        {
            PeerId = peerId;
            DisplayName = displayName;
            ConfigEntry = configEntry;
            MaxConnections = maxConnections;
            Source = source;
        }

        /// <summary>Gets the stable peer identifier.</summary>
        internal string PeerId { get; }

        /// <summary>Gets the display name.</summary>
        internal string DisplayName { get; }

        /// <summary>Gets the configuration entry text.</summary>
        internal string ConfigEntry { get; }

        /// <summary>Gets the connection cap.</summary>
        internal int MaxConnections { get; }

        /// <summary>Gets the parsed source.</summary>
        internal NntpNetworkSource Source { get; }
    }

    /// <summary>
    /// Builds and validates immutable transit peer source snapshots from configuration.
    /// </summary>
    internal static class NntpTransitPeerSnapshotBuilder
    {
        /// <summary>
        /// Builds a snapshot from <paramref name="peers"/> options, resolving hostnames when <paramref name="resolveHostnames"/> is true.
        /// </summary>
        /// <param name="peers">Peer definitions.</param>
        /// <param name="resolveHostnames">When true, performs blocking DNS for hostname entries.</param>
        /// <param name="snapshot">Built snapshot.</param>
        /// <param name="error">Failure description.</param>
        /// <returns><see langword="true"/> on success.</returns>
        internal static bool TryBuild(
            IReadOnlyList<NntpTransitPeerOptions> peers,
            bool resolveHostnames,
            out ImmutableArray<NntpTransitPeerSourceEntry> snapshot,
            out string? error)
        {
            var entries = new List<NntpTransitPeerSourceEntry>();
            foreach (NntpTransitPeerOptions peer in peers)
            {
                if (peer.AcceptFrom is null || peer.AcceptFrom.Length == 0)
                {
                    continue;
                }

                foreach (string rawEntry in peer.AcceptFrom)
                {
                    if (string.IsNullOrWhiteSpace(rawEntry))
                    {
                        error = $"Peer '{peer.PeerId}' has an empty AcceptFrom entry.";
                        snapshot = default;
                        return false;
                    }

                    string entry = rawEntry.Trim();
                    if (NntpNetworkSource.TryParse(entry, out NntpNetworkSource source))
                    {
                        entries.Add(new NntpTransitPeerSourceEntry(
                            peer.PeerId,
                            peer.Name,
                            entry,
                            peer.AcceptMaxConnections,
                            source));
                        continue;
                    }

                    if (!resolveHostnames)
                    {
                        error = $"Peer '{peer.PeerId}' entry '{entry}' is not a valid IP/CIDR and hostname resolution was not requested.";
                        snapshot = default;
                        return false;
                    }

                    try
                    {
                        IPHostEntry hostEntry = Dns.GetHostEntry(entry);
                        if (hostEntry.AddressList.Length == 0)
                        {
                            error = $"Peer '{peer.PeerId}' hostname '{entry}' did not resolve.";
                            snapshot = default;
                            return false;
                        }

                        foreach (IPAddress ip in hostEntry.AddressList)
                        {
                            if (!NntpNetworkSource.TryParse(ip.ToString(), out NntpNetworkSource resolved))
                            {
                                continue;
                            }

                            entries.Add(new NntpTransitPeerSourceEntry(
                                peer.PeerId,
                                peer.Name,
                                entry,
                                peer.AcceptMaxConnections,
                                resolved));
                        }
                    }
                    catch (Exception ex)
                    {
                        error = $"Peer '{peer.PeerId}' DNS resolution for '{entry}' failed: {ex.Message}";
                        snapshot = default;
                        return false;
                    }
                }
            }

            if (!TryValidateNoOverlap(entries, out error))
            {
                snapshot = default;
                return false;
            }

            snapshot = entries.ToImmutableArray();
            error = null;
            return true;
        }

        private static bool TryValidateNoOverlap(List<NntpTransitPeerSourceEntry> entries, out string? error)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                for (int j = i + 1; j < entries.Count; j++)
                {
                    if (string.Equals(entries[i].PeerId, entries[j].PeerId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (entries[i].Source.Overlaps(entries[j].Source))
                    {
                        error =
                            $"Transit peer address overlap: peer '{entries[i].PeerId}' entry '{entries[i].ConfigEntry}' " +
                            $"overlaps peer '{entries[j].PeerId}' entry '{entries[j].ConfigEntry}'.";
                        return false;
                    }
                }
            }

            error = null;
            return true;
        }
    }
}
