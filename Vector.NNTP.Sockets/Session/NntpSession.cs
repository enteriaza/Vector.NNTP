// <copyright file="NntpSession.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: pairs connection context with protocol state for dispatch.

using Microsoft.Extensions.Options;
using Vector.NNTP.Sockets.Configuration;
using Vector.NNTP.Sockets.HostProfile;
using Vector.NNTP.Sockets.Responses;
using Vector.NNTP.Sockets.Tls;
using Vector.NNTP.Sockets.Transport;

namespace Vector.NNTP.Sockets.Session
{
    /// <summary>
    /// Active NNTP session: connection accounting plus protocol state, passed through command dispatch.
    /// </summary>
    public sealed class NntpSession
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NntpSession"/> class.
        /// </summary>
        /// <param name="connection">Per-connection context.</param>
        /// <param name="state">Mutable protocol state.</param>
        /// <param name="profile">Host profile (reader or transit).</param>
        /// <param name="options">Server options snapshot.</param>
        /// <param name="transport">Socket transport (cleartext or TLS).</param>
        /// <param name="tlsCertificateSource">TLS certificate source for STARTTLS advertisement.</param>
        public NntpSession(
            NntpConnectionContext connection,
            NntpSessionState state,
            INntpHostProfile profile,
            IOptions<NntpServerOptions> options,
            INntpSessionTransport transport,
            ITlsCertificateSource? tlsCertificateSource)
        {
            Connection = connection ?? throw new ArgumentNullException(nameof(connection));
            State = state ?? throw new ArgumentNullException(nameof(state));
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            Options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            Transport = transport ?? throw new ArgumentNullException(nameof(transport));
            TlsCertificateSource = tlsCertificateSource;
            RebindTransportIo();
        }

        /// <summary>
        /// Gets the connection context.
        /// </summary>
        public NntpConnectionContext Connection { get; }

        /// <summary>
        /// Gets the protocol state.
        /// </summary>
        public NntpSessionState State { get; }

        /// <summary>
        /// Gets the host profile.
        /// </summary>
        public INntpHostProfile Profile { get; }

        /// <summary>
        /// Gets the server options snapshot.
        /// </summary>
        public NntpServerOptions Options { get; }

        /// <summary>
        /// Gets the socket transport for this session.
        /// </summary>
        public INntpSessionTransport Transport { get; }

        /// <summary>
        /// Gets the TLS certificate source (when Encryption is registered).
        /// </summary>
        internal ITlsCertificateSource? TlsCertificateSource { get; }

        /// <summary>
        /// Gets the response writer for this connection.
        /// </summary>
        public NntpResponseWriter Writer { get; private set; } = null!;

        /// <summary>
        /// Gets the line reader for this connection.
        /// </summary>
        internal NntpLineReader LineReader { get; private set; } = null!;

        /// <summary>
        /// Gets a value indicating whether AUTHINFO/SASL may proceed (TLS gating).
        /// </summary>
        public bool IsAuthInfoPermitted =>
            !Options.RequireTlsForAuthInfo || State.IsTlsActive;

        /// <summary>
        /// Rebinds <see cref="LineReader"/> and <see cref="Writer"/> after transport upgrade (STARTTLS).
        /// </summary>
        internal void RebindTransportIo()
        {
            LineReader = new NntpLineReader(Transport.Input, Connection);
            Writer = new NntpResponseWriter(Transport.Output, Connection);
        }
    }
}
