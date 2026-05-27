// <copyright file="NntpSessionState.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: mutable protocol session state per Docs/session-state.md.

namespace Vector.NNTP.Sockets.Session
{
    /// <summary>
    /// Mutable NNTP protocol state: mode, TLS/compress, auth pending, selected group/article, multi-line bodies.
    /// </summary>
    public sealed class NntpSessionState
    {
        /// <summary>
        /// Gets or sets the active session mode (reader, stream, or none).
        /// </summary>
        public NntpSessionMode Mode { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the transport is TLS-protected (implicit or STARTTLS).
        /// </summary>
        public bool IsTlsActive { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether COMPRESS DEFLATE is active.
        /// </summary>
        public bool IsCompressionActive { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether STARTTLS completed successfully.
        /// </summary>
        public bool StartTlsCompleted { get; set; }

        /// <summary>
        /// Gets or sets coordinated authentication progress.
        /// </summary>
        public AuthenticationState AuthenticationState { get; set; }

        /// <summary>
        /// Gets or sets the username pending AUTHINFO PASS after USER.
        /// </summary>
        public string? PendingAuthInfoUser { get; set; }

        /// <summary>
        /// Gets or sets the active SASL mechanism name during multi-step exchange.
        /// </summary>
        public string? PendingSaslMechanism { get; set; }

        /// <summary>
        /// Gets or sets opaque SASL server state for SCRAM/CRAM continuations.
        /// </summary>
        public object? SaslServerState { get; set; }

        /// <summary>
        /// Gets or sets the currently selected newsgroup name (RFC 3977 GROUP).
        /// </summary>
        public string? SelectedGroup { get; set; }

        /// <summary>
        /// Gets or sets the low water mark reported for the selected group (RFC 3977 GROUP response).
        /// </summary>
        /// <remarks>
        /// Populated when <see cref="SelectedGroup"/> is set via GROUP; cleared when the group selection changes.
        /// </remarks>
        public long? SelectedGroupLowWater { get; set; }

        /// <summary>
        /// Gets or sets the high water mark reported for the selected group (RFC 3977 GROUP response).
        /// </summary>
        /// <remarks>
        /// Populated when <see cref="SelectedGroup"/> is set via GROUP; used for ARTICLE range and NEXT/LAST bounds.
        /// </remarks>
        public long? SelectedGroupHighWater { get; set; }

        /// <summary>
        /// Gets or sets the estimated article count reported for the selected group (RFC 3977 GROUP response).
        /// </summary>
        public long? SelectedGroupEstimatedCount { get; set; }

        /// <summary>
        /// Gets or sets the current article number in the selected group.
        /// </summary>
        public long? CurrentArticleNumber { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether a multi-line body (POST, IHAVE) is being read.
        /// </summary>
        public bool MultiLineBodyPending { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether reader command pipelining is enabled after MODE READER.
        /// </summary>
        public bool ReaderPipeliningEnabled { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the session should close after the next response.
        /// </summary>
        public bool QuitRequested { get; set; }
    }
}
