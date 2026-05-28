// <copyright file="AuthenticationState.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Session.Context
{
    /// <summary>
    /// Canonical authentication state for a node-local <see cref="SessionContext"/> row.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Distinct from wire-protocol substates in <c>Vector.NNTP.Sockets.Session.AuthenticationState</c>
    /// (USER pending, SASL in progress). Session state is the authoritative row lifecycle for admission,
    /// heartbeat, and quota enforcement.
    /// </para>
    /// </remarks>
    public enum AuthenticationState
    {
        /// <summary>
        /// TCP accepted; no successful authentication yet.
        /// </summary>
        Unauthenticated,

        /// <summary>
        /// Transient handshake or admission in progress.
        /// </summary>
        Authenticating,

        /// <summary>
        /// Credentials verified and distributed admission completed when required.
        /// </summary>
        Authenticated,
    }
}
