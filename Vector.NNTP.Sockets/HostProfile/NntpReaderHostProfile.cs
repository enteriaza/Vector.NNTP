// <copyright file="NntpReaderHostProfile.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: NNRPD reader profile defaults.

namespace Vector.NNTP.Sockets.HostProfile
{
    using Commands;

    /// <summary>
    /// NNRPD-style reader host profile: MODE READER, reader commands, POST when policy allows.
    /// </summary>
    public sealed class NntpReaderHostProfile : INntpHostProfile
    {
        /// <inheritdoc />
        public NntpHostRole Role => NntpHostRole.Reader;

        /// <inheritdoc />
        public bool AllowsReaderCommands => true;

        /// <inheritdoc />
        public bool AllowsAuthentication => true;

        /// <inheritdoc />
        public bool AllowsStreamingCommands => false;

        /// <inheritdoc />
        public bool AdvertisePost => true;

        /// <inheritdoc />
        public bool AdvertiseModeReader => true;

        /// <inheritdoc />
        public bool AdvertiseModeStream => false;

        /// <inheritdoc />
        public void AppendCapabilities(NntpCapabilitiesWriter writer, Session.NntpSession session)
        {
            ArgumentNullException.ThrowIfNull(writer);
            ArgumentNullException.ThrowIfNull(session);
        }
    }
}
