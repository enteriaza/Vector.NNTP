// <copyright file="AuthenticatingPhase.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Session.Context
{
    /// <summary>
    /// Optional sub-phase while a connection is in <see cref="AuthenticationState.Authenticating"/>.
    /// </summary>
    /// <remarks>
    /// Used for structured logs and metrics; wire protocol detail remains in the Sockets layer.
    /// </remarks>
    public enum AuthenticatingPhase
    {
        /// <summary>
        /// No specific phase recorded.
        /// </summary>
        None,

        /// <summary>
        /// AUTHINFO USER/PASS or SASL multi-step exchange in progress.
        /// </summary>
        SaslContinuation,

        /// <summary>
        /// Credential proof accepted; distributed admission (<see cref="INntpSessionCoordinator.TryAdmitAsync"/>) in flight.
        /// </summary>
        PendingAdmission,

        /// <summary>
        /// STARTTLS upgrade in progress when policy requires encryption before AUTH.
        /// </summary>
        TlsUpgrade,
    }
}
