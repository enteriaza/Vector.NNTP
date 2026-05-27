// <copyright file="NntpCmdStartTls.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: STARTTLS command handler (RFC 4642).

namespace Vector.NNTP.Sockets.Transport.Commands
{
    using Session;
    using Tls;

    /// <summary>
    /// Handles the NNTP STARTTLS command.
    /// </summary>
    internal static class NntpCmdStartTls
    {
        /// <summary>
        /// Upgrades the session transport to TLS when configured and permitted.
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="certificates">Certificate source for the server credential.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>
        /// <see langword="true"/> when TLS upgrade succeeded and the session should continue;
        /// <see langword="false"/> when STARTTLS was rejected or failed before upgrade.
        /// </returns>
        internal static async ValueTask<bool> DispatchAsync(
            NntpSession session,
            ITlsCertificateSource certificates,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(certificates);
            if (!session.Options.EnableStartTls)
            {
                await session.Writer.WriteLineAsync("502 STARTTLS not available", cancellationToken).ConfigureAwait(false);
                return false;
            }

            if (session.State.StartTlsCompleted)
            {
                await session.Writer.WriteLineAsync("502 STARTTLS already active", cancellationToken).ConfigureAwait(false);
                return false;
            }

            if (session.State.IsCompressionActive)
            {
                await session.Writer.WriteLineAsync("502 STARTTLS not permitted after COMPRESS", cancellationToken).ConfigureAwait(false);
                return false;
            }

            System.Security.Cryptography.X509Certificates.X509Certificate2? cert =
                await certificates.GetServerCertificateAsync(cancellationToken).ConfigureAwait(false);
            if (cert is null)
            {
                await session.Writer.WriteLineAsync("503 TLS not configured", cancellationToken).ConfigureAwait(false);
                return false;
            }

            await session.Writer.WriteLineAsync("382 Continue with TLS negotiation", cancellationToken).ConfigureAwait(false);
            await session.Transport.UpgradeToTlsAsync(cert, cancellationToken).ConfigureAwait(false);
            session.State.IsTlsActive = true;
            session.State.StartTlsCompleted = true;
            session.RebindTransportIo();
            return true;
        }
    }
}
