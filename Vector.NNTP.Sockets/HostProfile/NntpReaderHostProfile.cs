// <copyright file="NntpReaderHostProfile.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: NNRPD reader profile defaults.

using Vector.NNTP.Sockets.Commands;

namespace Vector.NNTP.Sockets.HostProfile
{
    /// <summary>
    /// NNRPD-style reader host profile: MODE READER, reader commands, POST when policy allows.
    /// </summary>
    public sealed class NntpReaderHostProfile : INntpHostProfile
    {
        /// <summary>
        /// Gets the host role.
        /// </summary>
        /// <returns>The host role.</returns>
        public NntpHostRole Role => NntpHostRole.Reader;

        /// <summary>
        /// Gets a value indicating whether reader commands are allowed.
        /// </summary>
        /// <returns>A value indicating whether reader commands are allowed.</returns>
        public bool AllowsReaderCommands => true;

        /// <summary>
        /// Gets a value indicating whether authentication is allowed.
        /// </summary>
        /// <returns>A value indicating whether authentication is allowed.</returns>
        public bool AllowsAuthentication => true;

        /// <summary>
        /// Gets a value indicating whether streaming commands are allowed.
        /// </summary>
        /// <returns>A value indicating whether streaming commands are allowed.</returns>
        public bool AllowsStreamingCommands => false;

        /// <summary>
        /// Gets a value indicating whether posting is allowed.
        /// </summary>
        /// <returns>A value indicating whether posting is allowed.</returns>
        public bool AdvertisePost => true;

        /// <summary>
        /// Gets a value indicating whether mode reader is allowed.
        /// </summary>
        /// <returns>A value indicating whether mode reader is allowed.</returns>
        public bool AdvertiseModeReader => true;

        /// <summary>
        /// Gets a value indicating whether mode stream is allowed.
        /// </summary>
        /// <returns>A value indicating whether mode stream is allowed.</returns>
        public bool AdvertiseModeStream => false;

        /// <summary>
        /// Appends the capabilities to the writer.
        /// </summary>
        /// <param name="writer">The writer.</param>
        /// <param name="session">The session.</param>
        public void AppendCapabilities(NntpCapabilitiesWriter writer, Session.NntpSession session)
        {
            ArgumentNullException.ThrowIfNull(writer);
            ArgumentNullException.ThrowIfNull(session);
        }
    }
}
