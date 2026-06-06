// <copyright file="IScramCredentialStore.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: host-supplied SCRAM secret lookup.

namespace Vector.NNTP.Sockets.Authentication
{
    /// <summary>
    /// Supplies SCRAM stored credentials for SASL SCRAM-SHA-256 and SCRAM-SHA-1.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Backend failures:</b> Implementors may throw <see cref="NntpCredentialStoreTransientException"/> when the
    /// backing store is unreachable, times out, or otherwise fails transiently. Callers map that to NNTP
    /// <c>503 Temporary authentication failure</c>.
    /// </para>
    /// <para>
    /// <b>Auth negatives:</b> Return <see langword="false"/> when the user is not found, disabled, not permitted, or
    /// SCRAM material is missing — not when the database is down.
    /// </para>
    /// </remarks>
    public interface IScramCredentialStore
    {
        /// <summary>
        /// Attempts to retrieve SCRAM material for <paramref name="username"/>.
        /// </summary>
        /// <param name="username">NNTP username.</param>
        /// <param name="credential">SCRAM stored credential when found.</param>
        /// <returns><see langword="true"/> when the user exists and SCRAM material was returned.</returns>
        /// <exception cref="NntpCredentialStoreTransientException">
        /// The backing credential store failed transiently (for example connection timeout or query timeout).
        /// </exception>
        public bool TryGetScramCredential(string username, [NotNullWhen(true)] out ScramStoredCredential? credential);
    }
}
