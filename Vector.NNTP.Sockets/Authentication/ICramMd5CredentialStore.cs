// <copyright file="ICramMd5CredentialStore.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: host-supplied CRAM-MD5 shared secret lookup.

namespace Vector.NNTP.Sockets.Authentication
{
    /// <summary>
    /// Supplies shared secrets for SASL CRAM-MD5 (RFC 2195).
    /// </summary>
    public interface ICramMd5CredentialStore
    {
        /// <summary>
        /// Attempts to retrieve the shared secret for <paramref name="username"/>.
        /// </summary>
        /// <param name="username">NNTP username.</param>
        /// <param name="secret">Shared secret bytes when found.</param>
        /// <returns><see langword="true"/> when the user exists.</returns>
        public bool TryGetCramSecret(string username, out ReadOnlyMemory<byte> secret);
    }
}
