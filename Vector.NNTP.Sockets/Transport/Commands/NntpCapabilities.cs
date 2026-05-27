// <copyright file="NntpCapabilities.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: CAPABILITIES command handler.

using Vector.NNTP.Sockets.Authentication;
using Vector.NNTP.Sockets.HostProfile;
using Vector.NNTP.Sockets.Commands;
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
        /// Sends the multi-line capability list for this session and host profile.
        /// </summary>
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

            writer.AppendLine("VERSION 2");
            writer.AppendLine(NntpImplementationCapability.GetLine());

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

            if (ShouldAdvertiseAuthInfoUser(session, isAuthAdvertised, isAuthenticated))
            {
                writer.AppendLine("AUTHINFO USER");
            }

            if (ShouldAdvertiseSasl(session, isAuthAdvertised, isAuthenticated))
            {
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

            if (!(dedicatedReader && session.State.Mode == NntpSessionMode.Reader))
            {
                writer.AppendLine("PIPELINING");
            }

            session.Profile.AppendCapabilities(writer, session);
            await session.Writer.WriteMultiLineAsync("101 Capability list:", writer.ToLines(), cancellationToken).ConfigureAwait(false);
            return true;
        }

        private static bool IsDedicatedReader(INntpHostProfile profile)
        {
            return profile.Role == NntpHostRole.Reader && profile.AllowsReaderCommands;
        }

        private static bool ShouldAdvertiseAuthInfoUser(NntpSession session, bool isAuthAdvertised, bool isAuthenticated)
        {
            return isAuthAdvertised && !isAuthenticated && session.IsAuthInfoPermitted;
        }

        private static bool ShouldAdvertiseSasl(NntpSession session, bool isAuthAdvertised, bool isAuthenticated)
        {
            return isAuthAdvertised && !isAuthenticated && session.IsAuthInfoPermitted;
        }

        private static bool ShouldAdvertiseCompressDeflate(NntpSession session, bool dedicatedReader)
        {
            return session.Options.EnableCompressDeflate && !session.State.IsCompressionActive && (!dedicatedReader || !session.Options.RequireTlsForAuthInfo || session.State.IsTlsActive);
        }

        private static string BuildSaslLine(IScramCredentialStore? scramStore)
        {
            return scramStore is null ? "SASL PLAIN LOGIN CRAM-MD5" : "SASL PLAIN LOGIN SCRAM-SHA-256 CRAM-MD5";
        }

        private static async ValueTask<bool> ShouldAdvertiseStartTlsAsync(NntpSession session, CancellationToken cancellationToken)
        {
            return session.Options.EnableStartTls && !session.State.IsTlsActive && !session.State.IsCompressionActive && session.TlsCertificateSource is not null && await session.TlsCertificateSource.GetServerCertificateAsync(cancellationToken).ConfigureAwait(false) is not null;
        }
    }
}
