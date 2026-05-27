// <copyright file="AcmeCertificateProvider.ChallengeOrchestration.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// AcmeCertificateProvider.ChallengeOrchestration.cs — DNS-01 challenge creation, authoritative DNS polling, and
// challenge validation with Let's Encrypt.
//
// Orchestrates the per-domain challenge flow: computes the TXT value, delegates record creation to the Cloudflare
// partial, waits for DNS propagation via authoritative polling or a fixed fallback delay, then validates the challenge
// with Let's Encrypt and polls until terminal status.
//
// Methods:
//   ProcessDns01ChallengeAsync -- Per-domain entry point: TXT record creation -> DNS propagation wait -> ACME
//                                 validation.  Called once per authorization in the ACME order.
//   ValidateChallengeAsync     -- Triggers challenge validation with Let's Encrypt, then polls the challenge resource
//                                 until a terminal status (Valid or Invalid) is reached or the retry budget is
//                                 exhausted.
//
// Cancellation:
//   The Certes library methods (IAuthorizationContext.Dns, IAuthorizationContext.Resource, IChallengeContext.Validate,
//   IChallengeContext.Resource) do not accept a CancellationToken.  Explicit CancellationToken.ThrowIfCancellationRequested
//   calls before each outbound ACME request ensure a host shutdown that occurs during the preceding await propagates
//   promptly rather than initiating a new network call.  The Task.Delay calls between poll iterations provide additional
//   cancellation check points.
//
// Exception safety:
//   The record ID is added to the caller's records list immediately after CreateCloudflareTxtRecordAsync returns --
//   before any further awaits -- to guarantee cleanup in the caller's finally block even if a subsequent operation
//   throws.
//
// Cross-platform:
//   Fully portable.  All methods use BCL APIs and the Certes library, both available on all .NET 8 runtimes
//   (Windows x64, Linux x64).  No P/Invoke, no OS-specific APIs, no architecture-specific intrinsics.
//
// SIMD applicability:
//   Not applicable.  This file orchestrates HTTP API calls, ACME protocol interactions, and polling loops.  There
//   are no contiguous memory buffers, byte-level pattern searches, or bulk numeric operations that would benefit
//   from vector instructions.
//
// Callers:
//   AcmeCertificateProvider.RequestCertificateAsync -- sole consumer via ProcessDns01ChallengeAsync (once per
//   authorization in the ACME order).

using Certes.Acme;
using Certes.Acme.Resource;
using Vector.NNTP.Encryption.Acme;

namespace Vector.NNTP.Encryption.Certificates.Acme
{
    /// <summary>
    /// Provides functionality for managing ACME DNS-01 challenges.
    /// </summary>
    internal sealed partial class AcmeCertificateProvider
    {
        /// <summary>
        /// DNS-01 challenge state collected before batch TXT publication and quorum polling.
        /// </summary>
        /// <param name="Dns01">Certes DNS-01 challenge context.</param>
        /// <param name="Domain">Domain identifier being validated.</param>
        /// <param name="RecordName">Cloudflare TXT record name.</param>
        /// <param name="DnsTxt">Expected TXT digest value.</param>
        private readonly record struct PendingDns01Challenge(
            IChallengeContext Dns01,
            string Domain,
            string RecordName,
            string DnsTxt);

        #region Private Methods -- DNS-01 Challenge Orchestration
        /// <summary>
        /// Tells Let's Encrypt to validate the DNS-01 challenge, then polls the challenge resource until the status
        /// transitions to <see cref="ChallengeStatus.Valid"/> or <see cref="ChallengeStatus.Invalid"/>.
        /// </summary>
        /// <remarks>
        /// <para><b>Cancellation guard:</b> <see cref="IChallengeContext.Validate"/> does not accept a
        /// <see cref="CancellationToken"/>.  A <see cref="CancellationToken.ThrowIfCancellationRequested"/> call before
        /// <c>Validate()</c> avoids initiating an outbound ACME request when the host is already shutting down.
        /// Similarly, <see cref="IChallengeContext.Resource"/> lacks cancellation support -- each poll iteration checks
        /// <paramref name="ct"/> via the <see cref="Task.Delay(TimeSpan, CancellationToken)"/> call between fetches.</para>
        ///
        /// <para><b>Poll strategy:</b> Polls at <see cref="ChallengeValidationPollInterval"/> intervals for up to
        /// <see cref="ChallengeValidationMaxAttempts"/> attempts.  Each iteration checks for terminal states
        /// (<see cref="ChallengeStatus.Valid"/> or <see cref="ChallengeStatus.Invalid"/>) via the
        /// <c>CheckTerminalStatus</c> local function, which returns on success or throws on failure.</para>
        ///
        /// <para><b>Final check after last fetch:</b> After the loop exhausts all attempts, a final call to
        /// <c>CheckTerminalStatus</c> ensures a late status transition on the last <see cref="IChallengeContext.Resource"/>
        /// fetch is not missed and reported as a spurious timeout.</para>
        ///
        /// <para><b>Error diagnostics:</b> When the challenge transitions to <see cref="ChallengeStatus.Invalid"/>, both
        /// the ACME error <c>Type</c> URI (e.g. <c>urn:ietf:params:acme:error:dns</c>) and the human-readable
        /// <c>Detail</c> are included in the <see cref="InvalidOperationException"/> message.  The <c>Type</c> URI
        /// enables immediate classification of the failure cause (DNS propagation, unauthorized, rate limit) without
        /// consulting ACME server logs.</para>
        ///
        /// <para><b>Timeout formatting:</b> The timeout duration in <see cref="TimeoutException"/> is computed as a
        /// <see cref="TimeSpan"/> from ticks (<c>ChallengeValidationPollInterval.Ticks x ChallengeValidationMaxAttempts</c>)
        /// rather than integer seconds.  This avoids truncation from an <c>(int)</c> cast on
        /// <see cref="TimeSpan.TotalSeconds"/> and produces a standard <c>hh:mm:ss</c> format (e.g.
        /// <c>00:01:00</c>).</para>
        ///
        /// <para><b>Static local function:</b> <c>CheckTerminalStatus</c> is declared <see langword="static"/> to prevent
        /// closure capture.  This function is called on every poll iteration and must not allocate a delegate that captures
        /// <see langword="this"/> or local variables.  The two required values (<c>challenge</c> and <c>domain</c>) are
        /// passed as explicit parameters.</para>
        /// </remarks>
        /// <param name="dns01">The DNS-01 challenge context.</param>
        /// <param name="domain">The domain being validated (for error and log messages).</param>
        /// <param name="ct">Cancellation token for host shutdown.</param>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the challenge status transitions to <see cref="ChallengeStatus.Invalid"/>.  The message includes
        /// the ACME error type URI and detail for diagnostics.
        /// </exception>
        /// <exception cref="TimeoutException">
        /// Thrown when the challenge does not reach <see cref="ChallengeStatus.Valid"/> within
        /// <see cref="ChallengeValidationMaxAttempts"/> x <see cref="ChallengeValidationPollInterval"/>.
        /// </exception>
        /// <exception cref="OperationCanceledException">Thrown when <paramref name="ct"/> is cancelled (host
        /// shutdown).</exception>
        private async Task ValidateChallengeAsync(IChallengeContext dns01, string domain, CancellationToken ct)
        {
            // Certes' Validate() does not accept a CancellationToken -- check before initiating the outbound ACME request.
            ct.ThrowIfCancellationRequested();
            Challenge challenge = await AcmeTransientRetry.ExecuteAsync(
                dns01.Validate,
                logger,
                "Acme.Validate",
                options.AcmeTransientRetryMaxAttempts,
                ct).ConfigureAwait(false);

            for (int attempt = 1; attempt <= ChallengeValidationMaxAttempts; attempt++)
            {
                if (CheckTerminalStatus(challenge, domain))
                    return;

                if (logger.IsEnabled(LogLevel.Debug))
                    LogChallengePollingStatus(domain, challenge.Status ?? default, attempt, ChallengeValidationMaxAttempts);

                await Task.Delay(ChallengeValidationPollInterval, ct).ConfigureAwait(false);

                // Certes' Resource() does not accept a CancellationToken -- the preceding Task.Delay provides the
                // cancellation check for this iteration.  If Resource() hangs, the underlying HttpClient.Timeout
                // (Certes default: 100 s) will eventually surface as an exception.
                challenge = await AcmeTransientRetry.ExecuteAsync(
                    dns01.Resource,
                    logger,
                    "Acme.ChallengeResource",
                    options.AcmeTransientRetryMaxAttempts,
                    ct).ConfigureAwait(false);
            }

            // Final status check after the last Resource() fetch -- a late Valid/Invalid transition on the final iteration
            // must not be missed and reported as a timeout.
            if (CheckTerminalStatus(challenge, domain))
                return;

            TimeSpan timeout = TimeSpan.FromTicks(ChallengeValidationPollInterval.Ticks * ChallengeValidationMaxAttempts);

            throw new TimeoutException(
                $"DNS-01 challenge for {domain} did not complete within {timeout} (final status: {challenge.Status})");

            // Local function: returns true if Valid, throws if Invalid, returns false if still pending.
            // Static to prevent closure capture -- this function is called on every poll iteration and must not
            // allocate a delegate that captures 'this' or local variables.
            static bool CheckTerminalStatus(Challenge ch, string dom)
            {
                if (ch.Status == ChallengeStatus.Valid)
                    return true;

                if (ch.Status == ChallengeStatus.Invalid)
                {
                    // Include both the ACME error type URI (e.g. urn:ietf:params:acme:error:dns) and the
                    // human-readable detail.  The type URI enables immediate failure classification without
                    // consulting ACME server logs.
                    throw new InvalidOperationException(
                        $"DNS-01 challenge failed for {dom}: {ch.Error?.Type} -- {ch.Error?.Detail ?? "unknown error"}");
                }

                return false;
            }
        }

        #endregion
    }

}
