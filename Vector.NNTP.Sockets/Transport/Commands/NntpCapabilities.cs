// <copyright file="NntpCapabilities.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: CAPABILITIES command handler.

using Vector.NNTP.Encryption.Certificates;
using Vector.NNTP.Sockets.Authentication;
using Vector.NNTP.Sockets.Commands;
using Vector.NNTP.Sockets.HostProfile;
using Vector.NNTP.Sockets.Session;
using CapabilitiesWriter = Vector.NNTP.Sockets.Commands.NntpCapabilitiesWriter;

namespace Vector.NNTP.Sockets.Transport.Commands
{
    /// <summary>
    /// Handles the NNTP CAPABILITIES command (RFC 4643 / RFC 3977).
    /// </summary>
    internal static class NntpCapabilities
    {
        /// <summary>
        /// RFC 3977 §3.3.2: single <c>LIST</c> capability line for authenticated reader sessions.
        /// </summary>
        private const string ReaderListCapabilityLine =
            "LIST COUNTS ACTIVE NEWSGROUPS ACTIVE.TIMES OVERVIEW.FMT HEADERS";

        /// <summary>
        /// SASL mechanisms advertised when no SCRAM credential store is registered.
        /// </summary>
        private static readonly string[] SaslWithoutScram =
        [
            "PLAIN",
            "LOGIN",
            "CRAM-MD5",
        ];

        /// <summary>
        /// SASL mechanisms advertised when a SCRAM credential store is registered.
        /// </summary>
        private static readonly string[] SaslWithScram =
        [
            "PLAIN",
            "LOGIN",
            "SCRAM-SHA-256",
            "CRAM-MD5",
        ];

        /// <summary>
        /// Sends the multi-line capability list for this session and host profile.
        /// </summary>
        /// <remarks>
        /// <c>IMPLEMENTATION</c> is always emitted last, after host-profile extensions from
        /// <see cref="INntpHostProfile.AppendCapabilities(Commands.NntpCapabilitiesWriter, NntpSession)"/>.
        /// </remarks>
        /// <param name="session">Active session.</param>
        /// <param name="scramStore">Optional SCRAM credential store; SCRAM mechanisms are omitted when null.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns><see langword="true"/> to continue the session.</returns>
        internal static async ValueTask<bool> DispatchAsync(
            NntpSession session,
            IScramCredentialStore? scramStore,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(session);
            CapabilitiesWriter writer = new();
            bool dedicatedReader = IsDedicatedReader(session.Profile);
            bool isAuthenticated = session.Connection.IsAuthenticated;
            bool isAuthAdvertised = session.Profile.AllowsAuthentication;
            bool advertiseAuthentication =
                isAuthAdvertised && !isAuthenticated && session.IsAuthInfoPermitted;

            writer.AppendLine("VERSION 2");

            if (session.Profile.AllowsStreamingCommands)
            {
                writer.AppendLine("IHAVE");
                writer.AppendLine("STREAM");
            }

            if (dedicatedReader)
            {
                writer.AppendLine("READER");
                if (!isAuthenticated)
                {
                    writer.AppendLine("MODE-READER");
                }
            }

            if (await ShouldAdvertiseStartTlsAsync(session, cancellationToken).ConfigureAwait(false))
            {
                writer.AppendLine("STARTTLS");
            }

            if (advertiseAuthentication)
            {
                writer.AppendLine("AUTHINFO USER");
                writer.AppendLine(BuildSaslLine(scramStore));
            }

            if (dedicatedReader && isAuthenticated)
            {
                if (NntpPostingPolicy.IsPostingPermitted(session))
                {
                    writer.AppendLine("POST");
                }

                writer.AppendLine(ReaderListCapabilityLine);
                writer.AppendLine("OVER");
                writer.AppendLine("HDR");
            }

            if (ShouldAdvertiseCompressDeflate(session, dedicatedReader))
            {
                writer.AppendLine("COMPRESS DEFLATE");
            }

            // Dedicated reader hosts withdraw PIPELINING after MODE READER; transit and pre-reader sessions keep it.
            if (!(dedicatedReader && session.State.Mode == NntpSessionMode.Reader))
            {
                writer.AppendLine("PIPELINING");
            }

            session.Profile.AppendCapabilities(writer, session);
            writer.AppendLine(NntpImplementationCapability.GetLine());
            await session.Writer.WriteMultiLineAsync("101 Capability list:", writer.ToLines(), cancellationToken).ConfigureAwait(false);
            return true;
        }

        /// <summary>
        /// Returns <see langword="true"/> when the host profile is a dedicated reader deployment.
        /// </summary>
        /// <param name="profile">Host profile for the accepted connection.</param>
        /// <returns><see langword="true"/> for reader role with reader commands enabled.</returns>
        private static bool IsDedicatedReader(INntpHostProfile profile)
        {
            return profile.Role == NntpHostRole.Reader && profile.AllowsReaderCommands;
        }

        /// <summary>
        /// Returns <see langword="true"/> when COMPRESS DEFLATE should appear in the capability list.
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="dedicatedReader">Whether the host is a dedicated reader deployment.</param>
        /// <returns><see langword="true"/> when compression may be negotiated.</returns>
        private static bool ShouldAdvertiseCompressDeflate(NntpSession session, bool dedicatedReader)
        {
            bool compressionEnabled =
                session.Options.EnableCompressDeflate && !session.State.IsCompressionActive;
            bool securityRequirementsSatisfied =
                !dedicatedReader ||
                !session.Options.RequireTlsForAuthInfo ||
                session.State.IsTlsActive;
            return compressionEnabled && securityRequirementsSatisfied;
        }

        /// <summary>
        /// Builds the <c>SASL</c> capability line from the registered mechanism list.
        /// </summary>
        /// <param name="scramStore">SCRAM credential store; when present, SCRAM-SHA-256 is included.</param>
        /// <returns>Single <c>SASL …</c> capability line without CRLF.</returns>
        private static string BuildSaslLine(IScramCredentialStore? scramStore)
        {
            string[] mechanisms = scramStore is null ? SaslWithoutScram : SaslWithScram;
            return "SASL " + string.Join(' ', mechanisms);
        }

        /// <summary>
        /// Returns <see langword="true"/> when STARTTLS should appear in the capability list.
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns><see langword="true"/> when a server certificate is available and TLS is not already active.</returns>
        /// <remarks>
        /// <see cref="Tls.ITlsCertificateSource.GetServerCertificateAsync"/> is expected to return an in-process cached
        /// certificate (for example via <see cref="ICertificateRenewalPublisher.GetCurrentCertificate"/>), not reload from disk on
        /// every CAPABILITIES invocation.
        /// </remarks>
        private static async ValueTask<bool> ShouldAdvertiseStartTlsAsync(NntpSession session, CancellationToken cancellationToken)
        {
            return session.Options.EnableStartTls &&
                !session.State.IsTlsActive &&
                !session.State.IsCompressionActive &&
                session.TlsCertificateSource is not null &&
                await session.TlsCertificateSource.GetServerCertificateAsync(cancellationToken).ConfigureAwait(false) is not null;
        }
    }
}
