// <copyright file="IScramCredentialStore.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: host-supplied SCRAM secret lookup.

namespace Vector.NNTP.Sockets.Authentication
{
    /// <summary>
    /// Supplies SCRAM stored credentials for SASL SCRAM-SHA-256 and SCRAM-SHA-1.
    /// </summary>
    public interface IScramCredentialStore
    {
        /// <summary>
        /// Attempts to retrieve SCRAM material for <paramref name="username"/>.
        /// </summary>
        /// <param name="username">NNTP username.</param>
        /// <param name="credential">SCRAM stored credential when found.</param>
        /// <returns><see langword="true"/> when the user exists.</returns>
        public bool TryGetScramCredential(string username, [NotNullWhen(true)] out ScramStoredCredential? credential);
    }
}
