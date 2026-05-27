// <copyright file="INntpHostProfile.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: host capability and command policy surface.

namespace Vector.NNTP.Sockets.HostProfile
{
    using Commands;
    /// <summary>
    /// Host-specific NNTP capability advertisement and command allow/deny policy (reader vs transit).
    /// </summary>
    public interface INntpHostProfile
    {
        /// <summary>
        /// Gets the deployment role served by this profile.
        /// </summary>
        NntpHostRole Role { get; }

        /// <summary>
        /// Gets a value indicating whether RFC 3977 reader data commands are permitted after authentication.
        /// </summary>
        bool AllowsReaderCommands { get; }

        /// <summary>
        /// Gets a value indicating whether RFC 4644 streaming commands are permitted.
        /// </summary>
        bool AllowsStreamingCommands { get; }

        /// <summary>
        /// Gets a value indicating whether POST is advertised when authenticated policy allows posting.
        /// </summary>
        bool AdvertisePost { get; }

        /// <summary>
        /// Gets a value indicating whether MODE READER is advertised and accepted.
        /// </summary>
        bool AdvertiseModeReader { get; }

        /// <summary>
        /// Gets a value indicating whether MODE STREAM is advertised and accepted.
        /// </summary>
        bool AdvertiseModeStream { get; }

        /// <summary>
        /// Appends profile-specific capability lines (after core lines) to <paramref name="writer"/>.
        /// </summary>
        /// <param name="writer">Capability list builder.</param>
        /// <param name="session">Active session for TLS/auth-dependent lines.</param>
        void AppendCapabilities(NntpCapabilitiesWriter writer, Session.NntpSession session);
    }
}
