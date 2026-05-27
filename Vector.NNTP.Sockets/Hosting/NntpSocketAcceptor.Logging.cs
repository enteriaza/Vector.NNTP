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
    }
}
