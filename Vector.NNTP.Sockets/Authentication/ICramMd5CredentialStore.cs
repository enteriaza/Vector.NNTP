// <copyright file="ICramMd5CredentialStore.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: host-supplied CRAM-MD5 shared secret lookup.

namespace Vector.NNTP.Sockets.Authentication
{
    /// <summary>
    /// Supplies shared secrets for SASL CRAM-MD5 (RFC 2195).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Backend failures:</b> Implementors may throw <see cref="NntpCredentialStoreTransientException"/> when the
    /// backing store is unreachable, times out, or otherwise fails transiently. Callers map that to NNTP
    /// <c>503 Temporary authentication failure</c>.
    /// </para>
    /// <para>
    /// <b>Auth negatives:</b> Return <see langword="false"/> when the user is not found, disabled, or not permitted for
    /// the mechanism — not when the database is down.
    /// </para>
    /// </remarks>
    public interface ICramMd5CredentialStore
    {
        /// <summary>
        /// Attempts to retrieve the shared secret for <paramref name="username"/>.
        /// </summary>
        /// <param name="username">NNTP username.</param>
        /// <param name="secret">Shared secret bytes when found.</param>
        /// <returns><see langword="true"/> when the user exists and a secret was returned.</returns>
        /// <exception cref="NntpCredentialStoreTransientException">
        /// The backing credential store failed transiently (for example connection timeout or query timeout).
        /// </exception>
        public bool TryGetCramSecret(string username, out ReadOnlyMemory<byte> secret);
    }
}
