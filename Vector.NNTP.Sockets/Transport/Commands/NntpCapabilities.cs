// <copyright file="NntpCapabilities.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: CAPABILITIES command handler.

namespace Vector.NNTP.Sockets.Transport.Commands
{
    using Authentication;
    using HostProfile;
    using Vector.NNTP.Sockets.Commands;
    using Session;
    using Tls;
    using CapabilitiesWriter = Vector.NNTP.Sockets.Commands.NntpCapabilitiesWriter;

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
            CapabilitiesWriter writer = new CapabilitiesWriter();
            bool dedicatedReader = IsDedicatedReader(session.Profile);
            bool isAuthenticated = session.Connection.IsAuthenticated;

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

            if (ShouldAdvertiseAuthInfoUser(session, dedicatedReader, isAuthenticated))
            {
                writer.AppendLine("AUTHINFO USER");
            }

            if (ShouldAdvertiseSasl(session, dedicatedReader, isAuthenticated))
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

        private static bool IsDedicatedReader(INntpHostProfile profile) =>
            profile.Role == NntpHostRole.Reader && profile.AllowsReaderCommands;

        private static bool ShouldAdvertiseAuthInfoUser(NntpSession session, bool dedicatedReader, bool isAuthenticated)
        {
            if (!dedicatedReader || isAuthenticated)
            {
                return false;
            }

            return session.IsAuthInfoPermitted;
        }

        private static bool ShouldAdvertiseSasl(NntpSession session, bool dedicatedReader, bool isAuthenticated)
        {
            if (!dedicatedReader || isAuthenticated)
            {
                return false;
            }

            return session.IsAuthInfoPermitted;
        }

        private static bool ShouldAdvertiseCompressDeflate(NntpSession session, bool dedicatedReader)
        {
            if (!session.Options.EnableCompressDeflate || session.State.IsCompressionActive)
            {
                return false;
            }

            if (dedicatedReader && session.Options.RequireTlsForAuthInfo && !session.State.IsTlsActive)
            {
                return false;
            }

            return true;
        }

        private static string BuildSaslLine(IScramCredentialStore? scramStore)
        {
            if (scramStore is null)
            {
                return "SASL PLAIN LOGIN CRAM-MD5";
            }

            return "SASL PLAIN LOGIN SCRAM-SHA-256 SCRAM-SHA-1 CRAM-MD5";
        }

        private static async ValueTask<bool> ShouldAdvertiseStartTlsAsync(NntpSession session, CancellationToken cancellationToken)
        {
            if (!session.Options.EnableStartTls || session.State.IsTlsActive || session.State.IsCompressionActive)
            {
                return false;
            }

            if (session.TlsCertificateSource is null)
            {
                return false;
            }

            return await session.TlsCertificateSource.GetServerCertificateAsync(cancellationToken).ConfigureAwait(false) is not null;
        }
    }
}
