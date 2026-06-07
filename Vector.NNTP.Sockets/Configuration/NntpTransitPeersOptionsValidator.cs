// <copyright file="NntpTransitPeersOptionsValidator.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: startup validation for trusted transit peer configuration.

using Microsoft.Extensions.Options;
using Vector.NNTP.Sockets.Policy;
using Vector.NNTP.Utilities.Validation;

namespace Vector.NNTP.Sockets.Configuration
{
    /// <summary>
    /// Validates <see cref="NntpTransitPeersOptions"/> nested under <see cref="NntpServerOptions"/>.
    /// </summary>
    public static class NntpTransitPeersOptionsValidator
    {
        /// <summary>
        /// Validates transit peer configuration.
        /// </summary>
        /// <param name="transitPeers">Transit peers options.</param>
        /// <returns>Validation result.</returns>
        public static ValidateOptionsResult Validate(NntpTransitPeersOptions transitPeers)
        {
            ArgumentNullException.ThrowIfNull(transitPeers);
            if (transitPeers.Peers is null || transitPeers.Peers.Length == 0)
            {
                return ValidateOptionsResult.Success;
            }

            if (transitPeers.RefreshIntervalMinutes is < 1 or > 1440)
            {
                return ValidateOptionsResult.Fail(
                    $"{nameof(NntpTransitPeersOptions.RefreshIntervalMinutes)} must be between 1 and 1440.");
            }

            var peerNames = new HashSet<string>(StringComparer.Ordinal);
            var errors = new List<string>();
            foreach (NntpTransitPeerOptions peer in transitPeers.Peers)
            {
                if (string.IsNullOrWhiteSpace(peer.Name))
                {
                    errors.Add("Each transit peer requires a non-empty Name.");
                    continue;
                }

                if (!peerNames.Add(peer.Name))
                {
                    errors.Add($"Duplicate peer Name '{peer.Name}'.");
                }

                if (peer.MaxConnections < 0)
                {
                    errors.Add($"Peer '{peer.Name}' MaxConnections must be non-negative.");
                }

                if (peer.AcceptFrom is null || peer.AcceptFrom.Length == 0)
                {
                    errors.Add($"Peer '{peer.Name}' requires at least one AcceptFrom entry.");
                    continue;
                }

                foreach (string entry in peer.AcceptFrom)
                {
                    if (string.IsNullOrWhiteSpace(entry))
                    {
                        errors.Add($"Peer '{peer.Name}' has an empty AcceptFrom entry.");
                        continue;
                    }

                    string trimmed = entry.Trim();
                    if (trimmed.Contains(':', StringComparison.Ordinal) && !trimmed.Contains('/', StringComparison.Ordinal))
                    {
                        errors.Add($"Peer '{peer.Name}' entry '{trimmed}' must not include a port.");
                        continue;
                    }

                    if (NntpNetworkSource.TryParse(trimmed, out _))
                    {
                        continue;
                    }

                    if (!DnsValidationUtilities.TryValidateHost(trimmed, out string? dnsError))
                    {
                        errors.Add($"Peer '{peer.Name}' entry '{trimmed}': {dnsError}");
                    }
                }
            }

            if (errors.Count > 0)
            {
                return ValidateOptionsResult.Fail(errors);
            }

            if (!NntpTransitPeerSnapshotBuilder.TryBuild(
                    transitPeers.Peers,
                    resolveHostnames: true,
                    out _,
                    out string? overlapError))
            {
                return ValidateOptionsResult.Fail(overlapError ?? "Transit peer snapshot build failed.");
            }

            return ValidateOptionsResult.Success;
        }
    }
}
