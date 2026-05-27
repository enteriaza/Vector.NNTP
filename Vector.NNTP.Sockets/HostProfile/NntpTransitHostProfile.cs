// <copyright file="NntpTransitHostProfile.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: NNTPD transit profile defaults.

namespace Vector.NNTP.Sockets.HostProfile
{
    using Commands;

    /// <summary>
    /// NNTPD-style transit host profile: MODE STREAM and RFC 4644 streaming commands only.
    /// </summary>
    public sealed class NntpTransitHostProfile : INntpHostProfile
    {
        /// <inheritdoc />
        public NntpHostRole Role => NntpHostRole.Transit;

        /// <inheritdoc />
        public bool AllowsReaderCommands => false;

        /// <inheritdoc />
        public bool AllowsStreamingCommands => true;

        /// <inheritdoc />
        public bool AdvertisePost => false;

        /// <inheritdoc />
        public bool AdvertiseModeReader => false;

        /// <inheritdoc />
        public bool AdvertiseModeStream => true;

        /// <inheritdoc />
        public void AppendCapabilities(NntpCapabilitiesWriter writer, Session.NntpSession session)
        {
            ArgumentNullException.ThrowIfNull(writer);
            ArgumentNullException.ThrowIfNull(session);
        }
    }
}
