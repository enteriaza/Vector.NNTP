// <copyright file="NntpTransitPeerMatcher.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: immutable snapshot matcher for trusted transit peers.

using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vector.NNTP.Sockets.Configuration;
using Vector.NNTP.Sockets.Metrics;

namespace Vector.NNTP.Sockets.Policy
{
    /// <summary>
    /// Thread-safe transit peer matcher backed by an atomically swapped immutable source snapshot.
    /// </summary>
    public sealed partial class NntpTransitPeerMatcher : INntpTransitPeerMatcher
    {
        private readonly object _snapshotLock = new();
        private ImmutableArray<NntpTransitPeerSourceEntry> _sources = ImmutableArray<NntpTransitPeerSourceEntry>.Empty;
        private readonly IOptionsMonitor<NntpServerOptions> _options;
        private readonly ILogger<NntpTransitPeerMatcher> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="NntpTransitPeerMatcher"/> class and builds the initial snapshot.
        /// </summary>
        /// <param name="options">Server options monitor.</param>
        /// <param name="logger">Logger.</param>
        public NntpTransitPeerMatcher(IOptionsMonitor<NntpServerOptions> options, ILogger<NntpTransitPeerMatcher> logger)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _ = TryRebuildSnapshot(logSuccess: false, out _);
        }

        /// <inheritdoc />
        public bool TryMatch(IPAddress clientAddress, out NntpTransitPeerMatchResult result)
        {
            ArgumentNullException.ThrowIfNull(clientAddress);
            ImmutableArray<NntpTransitPeerSourceEntry> sources;
            lock (_snapshotLock)
            {
                sources = _sources;
            }
            NntpTransitPeerMatchResult? found = null;
            foreach (NntpTransitPeerSourceEntry entry in sources)
            {
                if (!entry.Source.Contains(clientAddress))
                {
                    continue;
                }

                if (found is not null)
                {
                    result = default;
                    return false;
                }

                found = new NntpTransitPeerMatchResult(
                    entry.Name,
                    entry.ConfigEntry,
                    entry.MaxConnections);
            }

            if (found is null)
            {
                result = default;
                return false;
            }

            result = found.Value;
            return true;
        }

        /// <summary>
        /// Rebuilds the matcher snapshot from current configuration.
        /// </summary>
        /// <param name="logSuccess">When true, logs information and records refresh success metrics.</param>
        /// <param name="error">Failure reason when this method returns false.</param>
        /// <returns><see langword="true"/> when the snapshot was rebuilt and swapped.</returns>
        internal bool TryRebuildSnapshot(bool logSuccess, out string? error)
        {
            NntpTransitPeersOptions transitPeers = _options.CurrentValue.TransitPeers;
            NntpTransitPeerOptions[] peers = transitPeers.Peers ?? Array.Empty<NntpTransitPeerOptions>();
            if (peers.Length == 0)
            {
                lock (_snapshotLock)
                {
                    _sources = ImmutableArray<NntpTransitPeerSourceEntry>.Empty;
                }

                NntpTransitPeerMetrics.UpdateConfiguredCapacity(Array.Empty<NntpTransitPeerOptions>());
                error = null;
                return true;
            }

            if (!NntpTransitPeerSnapshotBuilder.TryBuild(peers, resolveHostnames: true, out ImmutableArray<NntpTransitPeerSourceEntry> snapshot, out error))
            {
                if (logSuccess)
                {
                    NntpTransitPeerMetrics.RecordRefreshFailure(ClassifyRefreshFailure(error));
                }

                return false;
            }

            lock (_snapshotLock)
            {
                _sources = snapshot;
            }

            NntpTransitPeerMetrics.UpdateConfiguredCapacity(peers);
            if (logSuccess)
            {
                LogSnapshotRebuilt(_logger, snapshot.Length, peers.Length);
                NntpTransitPeerMetrics.RecordRefreshSuccess();
            }

            error = null;
            return true;
        }

        private static string ClassifyRefreshFailure(string? error)
        {
            if (string.IsNullOrEmpty(error))
            {
                return "unknown";
            }

            return error.Contains("overlap", StringComparison.OrdinalIgnoreCase)
                ? "overlap"
                : error.Contains("DNS", StringComparison.OrdinalIgnoreCase) ? "dns" : "parse";
        }
    }
}
