// <copyright file="NntpSocketAcceptor.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: source-generated LoggerMessage methods for NntpSocketAcceptor.

using Microsoft.Extensions.Logging;

namespace Vector.NNTP.Sockets.Hosting
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> partial methods for <see cref="NntpSocketAcceptor"/>.
    /// </summary>
    internal sealed partial class NntpSocketAcceptor
    {
        /// <summary>
        /// Logs that a listener has started on the configured bind address and port.
        /// </summary>
        /// <param name="address">Bind address.</param>
        /// <param name="port">TCP port.</param>
        /// <param name="mode">Listener mode label (cleartext or TLS).</param>
        [LoggerMessage(
            EventId = 1,
            Level = LogLevel.Information,
            Message = "NNTP listening on {Address}:{Port} ({Mode})")]
        private partial void LogListening(string address, int port, string mode);

        /// <summary>
        /// Logs rejection of an implicit-TLS connection when no certificate is available yet.
        /// </summary>
        [LoggerMessage(
            EventId = 2,
            Level = LogLevel.Debug,
            Message = "Rejecting TLS connection: no server certificate available yet")]
        private partial void LogRejectTlsNoCertificate();

        /// <summary>
        /// Logs an unexpected connection teardown (excluding cancellation).
        /// </summary>
        /// <param name="exception">Observed exception.</param>
        [LoggerMessage(
            EventId = 3,
            Level = LogLevel.Debug,
            Message = "Connection closed with error")]
        private partial void LogConnectionClosedWithError(Exception exception);

        /// <summary>
        /// Logs hot reload of the TLS handshake certificate.
        /// </summary>
        /// <param name="thumbprint">Certificate thumbprint.</param>
        [LoggerMessage(
            EventId = 4,
            Level = LogLevel.Information,
            Message = "TLS handshake certificate updated (thumbprint {Thumbprint})")]
        private partial void LogTlsCertificateUpdated(string thumbprint);

        /// <summary>
        /// Logs successful trusted transit peer admission.
        /// </summary>
        /// <param name="logger">Target logger for the structured event.</param>
        /// <param name="connectionPrefix">Connection log prefix.</param>
        /// <param name="peerName">Configured peer name.</param>
        /// <param name="matchedEntry">Matched AcceptFrom entry.</param>
        [LoggerMessage(
            EventId = 5,
            Level = LogLevel.Information,
            Message = "{ConnectionPrefix} Accepted transit peer (peer {PeerName}, matched {MatchedEntry})")]
        private static partial void LogAcceptedTransitPeer(
            ILogger logger,
            string connectionPrefix,
            string peerName,
            string matchedEntry);

        /// <summary>
        /// Logs rejection when the peer is at cluster capacity.
        /// </summary>
        /// <param name="logger">Target logger for the structured event.</param>
        /// <param name="connectionPrefix">Connection log prefix.</param>
        /// <param name="peerName">Configured peer name.</param>
        /// <param name="occupied">Current occupied slots.</param>
        /// <param name="maxConnections">Configured cap (0 = unlimited).</param>
        [LoggerMessage(
            EventId = 6,
            Level = LogLevel.Information,
            Message = "{ConnectionPrefix} Rejected transit peer: peer {PeerName} at capacity ({Occupied}/{MaxConnections}, 0=max unlimited)")]
        private static partial void LogTransitPeerAtCapacity(
            ILogger logger,
            string connectionPrefix,
            string peerName,
            long occupied,
            int maxConnections);

        /// <summary>
        /// Logs Redis admission backend failure for a matched transit peer.
        /// </summary>
        /// <param name="logger">Target logger for the structured event.</param>
        /// <param name="connectionPrefix">Connection log prefix.</param>
        /// <param name="peerName">Configured peer name.</param>
        [LoggerMessage(
            EventId = 7,
            Level = LogLevel.Warning,
            Message = "{ConnectionPrefix} Rejected transit peer: Redis admission failed for peer {PeerName}")]
        private static partial void LogTransitPeerAdmissionBackendFailure(
            ILogger logger,
            string connectionPrefix,
            string peerName);

        /// <summary>
        /// Logs CPU overload rejection at accept time (RFC 3977 §5.1.1).
        /// </summary>
        /// <param name="connectionPrefix">Connection log prefix.</param>
        /// <param name="effectiveCpuUtilizationPercent">Effective EWMA percent driving the gate.</param>
        /// <param name="dominantSignal">Signal with the highest EWMA.</param>
        /// <param name="processEwmaPercent">Process EWMA percent when enabled.</param>
        /// <param name="hostEwmaPercent">Host EWMA percent when enabled.</param>
        /// <param name="cgroupEwmaPercent">Cgroup EWMA percent when available.</param>
        /// <param name="gateState">Gate state label.</param>
        /// <param name="rejectThresholdPercent">Reject threshold.</param>
        /// <param name="resumeThresholdPercent">Resume threshold.</param>
        [LoggerMessage(
            EventId = 8,
            Level = LogLevel.Information,
            Message = "{ConnectionPrefix} Rejecting connection due to CPU overload. EffectiveCpuUtilizationPercent={EffectiveCpuUtilizationPercent} DominantSignal={DominantSignal} ProcessEwmaPercent={ProcessEwmaPercent} HostEwmaPercent={HostEwmaPercent} CgroupEwmaPercent={CgroupEwmaPercent} GateState={GateState} RejectThresholdPercent={RejectThresholdPercent} ResumeThresholdPercent={ResumeThresholdPercent}")]
        private partial void LogCpuOverloadRejectAccept(
            string connectionPrefix,
            double effectiveCpuUtilizationPercent,
            string dominantSignal,
            double? processEwmaPercent,
            double? hostEwmaPercent,
            double? cgroupEwmaPercent,
            string gateState,
            double rejectThresholdPercent,
            double resumeThresholdPercent);
    }
}
