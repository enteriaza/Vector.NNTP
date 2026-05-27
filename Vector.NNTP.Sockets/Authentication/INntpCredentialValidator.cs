// <copyright file="INntpCredentialValidator.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: host-supplied password validation contract.

namespace Vector.NNTP.Sockets.Authentication
{
    /// <summary>
    /// Validates AUTHINFO PASS and SASL PLAIN/LOGIN passwords; implemented by the host (not RADIUS in this assembly).
    /// </summary>
    public interface INntpCredentialValidator
    {
        /// <summary>
        /// Validates a username and password for the connecting client.
        /// </summary>
        /// <param name="username">NNTP username.</param>
        /// <param name="password">Cleartext password from AUTHINFO or SASL.</param>
        /// <param name="clientIp">Effective client IP (post-PROXY).</param>
        /// <param name="isTls">Whether the connection is TLS-protected.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Validation outcome and optional session policy.</returns>
        public ValueTask<NntpAuthResult> ValidatePasswordAsync(
            string username,
            string password,
            IPAddress clientIp,
            bool isTls,
            CancellationToken cancellationToken);
    }
}
