// <copyright file="AuthenticationState.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: coordinated multi-step authentication state.

namespace Vector.NNTP.Sockets.Session
{
    /// <summary>
    /// Coordinated authentication progress for AUTHINFO USER/PASS and multi-step SASL mechanisms.
    /// </summary>
    public enum AuthenticationState
    {
        /// <summary>
        /// No authentication exchange in progress.
        /// </summary>
        None = 0,

        /// <summary>
        /// AUTHINFO USER succeeded; awaiting PASS.
        /// </summary>
        AuthInfoUserPending = 1,

        /// <summary>
        /// SASL mechanism selected; awaiting client continuation(s).
        /// </summary>
        SaslInProgress = 2,
    }
}
