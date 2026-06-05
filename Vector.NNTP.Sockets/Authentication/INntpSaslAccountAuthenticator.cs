// <copyright file="INntpSaslAccountAuthenticator.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: host-supplied SASL account finalization after cryptographic verification.

namespace Vector.NNTP.Sockets.Authentication
{
    /// <summary>
    /// Finalizes a successful SASL exchange (SCRAM or CRAM-MD5) by issuing session policy, enforcing admission,
    /// and emitting host-side audit logs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Cryptographic verification is performed by <see cref="NntpAuthenticationService"/> before this contract is
    /// invoked. Implementations should treat a call as "SASL proof already validated" and focus on account state,
    /// limits, and logging.
    /// </para>
    /// </remarks>
    public interface INntpSaslAccountAuthenticator
    {
        /// <summary>
        /// Completes SASL authentication for an account whose mechanism proof was verified on the wire.
        /// </summary>
        /// <param name="mechanism">
        /// Mechanism label (for example <see cref="NntpAuthMechanisms.SaslScramSha256"/> or
        /// <see cref="NntpAuthMechanisms.SaslCramMd5"/>).
        /// </param>
        /// <param name="username">Authenticated NNTP username from the SASL exchange.</param>
        /// <param name="clientIp">Effective client IP (post-PROXY).</param>
        /// <param name="isTls">Whether the connection is TLS-protected.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Authentication outcome and optional session policy.</returns>
        public ValueTask<NntpAuthResult> CompleteSaslAccountAsync(
            string mechanism,
            string username,
            IPAddress clientIp,
            bool isTls,
            CancellationToken cancellationToken);

        /// <summary>
        /// Discards any host-side user-record material stashed between SASL credential lookup and completion.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Contract:</b> <see cref="NntpAuthenticationService"/> invokes this from session auth reset paths (client
        /// cancel, cryptographic failure, unsupported continuation) so a prior credential-store lookup does not leave
        /// transient authentication state attached to the logical async call when
        /// <see cref="CompleteSaslAccountAsync"/> never runs.
        /// </para>
        /// <para><b>Idempotence:</b> Implementations should treat repeated calls as safe no-ops.</para>
        /// </remarks>
        public void AbandonSaslExchange();
    }
}
