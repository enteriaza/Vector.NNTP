// <copyright file="MySqlNntpCredentialValidator.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Buffers;
using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Vector.NNTP.Auth.MySql.Configuration;
using Vector.NNTP.Auth.MySql.Records;
using Vector.NNTP.Auth.MySql.Telemetry;
using Vector.NNTP.Session.Accounts;
using Vector.NNTP.Session.Policy;
using Vector.NNTP.Sockets.Authentication;
using Vector.NNTP.Utilities.Diagnostics;
using Vector.NNTP.Utilities.Encoding;

namespace Vector.NNTP.Auth.MySql.Credentials
{
    /// <summary>
    /// Production MySQL credential validator and SASL account finalizer for reader NNTP authentication.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Role:</b> Single singleton implementing both <see cref="INntpCredentialValidator"/> and
    /// <see cref="INntpSaslAccountAuthenticator"/>, registered by
    /// <see cref="DependencyInjection.ServiceCollectionExtensions.AddNntpMySqlAuth"/>. Validates AUTHINFO and SASL password
    /// mechanisms against <c>nntpusers</c> and finalizes SCRAM-SHA-256 and CRAM-MD5 after wire-level proof verification in
    /// the sockets layer.
    /// </para>
    /// <para>
    /// <b>Outcomes:</b> Policy and credential failures return <see cref="NntpAuthResult.InvalidCredentials"/> (typically 481
    /// on the wire). Database and transport faults return <see cref="NntpAuthResult.TransientFailure"/> (503-class semantics).
    /// Success returns <see cref="NntpAuthResult.Success"/> with <see cref="NntpSessionPolicy"/>; session admission and quota
    /// enforcement happen later in <c>Vector.NNTP.Session</c>, not in this type.
    /// </para>
    /// <para>
    /// <b>Burst cache:</b> Successful paths call <see cref="MySqlUserRecordCache.Put"/> with either a password fingerprint
    /// (AUTHINFO) or <see cref="MySqlUserRecordCache.UsernameOnlyFingerprint"/> (SASL). Payloads are AES-256-GCM protected with
    /// a short TTL for concurrent duplicate logons.
    /// </para>
    /// <para>
    /// <b>SASL staging:</b> <see cref="MySqlScramCredentialStore"/> and <see cref="MySqlCramMd5CredentialStore"/> stage rows in
    /// <see cref="MySqlUserRecordSaslCache"/> during secret retrieval; finalize consumes or falls back to async store lookup.
    /// <see cref="INntpSaslAccountAuthenticator.AbandonSaslExchange"/> clears the per-exchange slot on auth reset.
    /// </para>
    /// <para>
    /// <b>Observability:</b> Structured logs from <c>MySqlNntpCredentialValidator.Logging.cs</c> (EventIds <c>200</c>–<c>210</c>),
    /// <see cref="AuthMySqlMetrics.RecordValidate"/> / <see cref="AuthMySqlMetrics.RecordLookup"/>, and
    /// <see cref="AuthMySqlTelemetry"/> spans <c>auth.mysql.validate.password</c> and <c>auth.mysql.validate.sasl</c>.
    /// </para>
    /// <para><b>Thread safety:</b> Singleton safe for concurrent NNTP sessions; per-call state uses AsyncLocal SASL staging only.</para>
    /// </remarks>
    internal sealed partial class MySqlNntpCredentialValidator : INntpCredentialValidator, INntpSaslAccountAuthenticator
    {
        /// <summary>
        /// Decorated user-record store for MySQL lookups (production: <see cref="CachingMySqlUserRecordStore"/>).
        /// </summary>
        /// <remarks>Never null after construction.</remarks>
        private readonly INntpUserRecordStore _recordStore;

        /// <summary>
        /// BLAKE3 account-key normalizer used when building <see cref="NntpSessionPolicy"/>.
        /// </summary>
        /// <remarks>Supplied by session DI (<see cref="IAccountKeyNormalizer"/>).</remarks>
        private readonly IAccountKeyNormalizer _accountKeyNormalizer;

        /// <summary>
        /// Shared burst deduplication cache also consulted for password-fingerprint hits in <c>ValidatePasswordAsync</c>.
        /// </summary>
        /// <remarks>Same singleton instance registered for the caching record-store decorator.</remarks>
        private readonly MySqlUserRecordCache _authCache;

        /// <summary>
        /// Auth MySQL metrics recorder for validation and password cache-hit counters.
        /// </summary>
        private readonly AuthMySqlMetrics _metrics;

        /// <summary>
        /// Category logger for validation lifecycle events (EventIds <c>200</c>–<c>210</c>).
        /// </summary>
        private readonly ILogger<MySqlNntpCredentialValidator> _logger;

        /// <summary>
        /// Creates the production credential validator with store, policy, cache, metrics, and logging dependencies.
        /// </summary>
        /// <param name="recordStore">
        /// <see cref="INntpUserRecordStore"/> from DI. Must not be <see langword="null"/>.
        /// </param>
        /// <param name="accountKeyNormalizer">
        /// <see cref="IAccountKeyNormalizer"/> from session registration. Must not be <see langword="null"/>.
        /// </param>
        /// <param name="authCache">
        /// Shared <see cref="MySqlUserRecordCache"/> singleton. Must not be <see langword="null"/>.
        /// </param>
        /// <param name="metrics">
        /// <see cref="AuthMySqlMetrics"/> singleton. Must not be <see langword="null"/>.
        /// </param>
        /// <param name="logger">
        /// Logger for <see cref="MySqlNntpCredentialValidator"/>. Must not be <see langword="null"/>.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when any parameter is <see langword="null"/>.
        /// </exception>
        /// <remarks>Registered once in DI; exposed as both credential-validator interfaces on the same instance.</remarks>
        internal MySqlNntpCredentialValidator(
            INntpUserRecordStore recordStore,
            IAccountKeyNormalizer accountKeyNormalizer,
            MySqlUserRecordCache authCache,
            AuthMySqlMetrics metrics,
            ILogger<MySqlNntpCredentialValidator> logger)
        {
            _recordStore = recordStore ?? throw new ArgumentNullException(nameof(recordStore));
            _accountKeyNormalizer = accountKeyNormalizer ?? throw new ArgumentNullException(nameof(accountKeyNormalizer));
            _authCache = authCache ?? throw new ArgumentNullException(nameof(authCache));
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Finalizes SCRAM-SHA-256 or CRAM-MD5 authentication after the wire proof has already been verified.
        /// </summary>
        /// <param name="mechanism">
        /// SASL mechanism label: <see cref="NntpAuthMechanisms.SaslScramSha256"/> or
        /// <see cref="NntpAuthMechanisms.SaslCramMd5"/> only.
        /// </param>
        /// <param name="username">
        /// Plaintext account name from the SASL exchange. Whitespace-only values yield
        /// <see cref="NntpAuthResult.InvalidCredentials"/> without throwing.
        /// </param>
        /// <param name="clientIp">
        /// Client IP for logging and policy materialisation. Must not be <see langword="null"/>.
        /// </param>
        /// <param name="isTls">Whether the NNTP session uses TLS at completion time.</param>
        /// <param name="cancellationToken">
        /// Honoured when async store lookup runs on per-exchange cache miss.
        /// </param>
        /// <returns>
        /// <see cref="NntpAuthResult.Success"/> with policy when enabled and mechanism permitted;
        /// <see cref="NntpAuthResult.InvalidCredentials"/> for missing row, disabled account, or policy denial;
        /// <see cref="NntpAuthResult.TransientFailure"/> on backend fault.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="mechanism"/> is not SCRAM-SHA-256 or CRAM-MD5.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="clientIp"/> is <see langword="null"/> (via <c>FormatClientIp</c>).
        /// </exception>
        /// <remarks>
        /// <para>
        /// Implements <see cref="INntpSaslAccountAuthenticator.CompleteSaslAccountAsync"/>. Delegates to
        /// <c>FinalizeAuthenticationAsync</c> with policy delegate <see cref="MySqlUserRecord.AllowAuthScram256"/> for SCRAM
        /// or <see cref="MySqlUserRecord.AllowAuthPlain"/> for CRAM-MD5. Cryptographic verification is not repeated here.
        /// </para>
        /// <para>
        /// <see cref="MySqlUserRecordSaslCache.Clear"/> runs in <c>finally</c> inside finalize so the AsyncLocal slot never
        /// leaks across exchanges. <see cref="OperationCanceledException"/> propagates without being mapped to transient failure.
        /// </para>
        /// </remarks>
        async ValueTask<NntpAuthResult> INntpSaslAccountAuthenticator.CompleteSaslAccountAsync(
            string mechanism,
            string username,
            IPAddress clientIp,
            bool isTls,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                return NntpAuthResult.InvalidCredentials();
            }

            bool isScram = string.Equals(mechanism, NntpAuthMechanisms.SaslScramSha256, StringComparison.Ordinal);
            bool isCram = string.Equals(mechanism, NntpAuthMechanisms.SaslCramMd5, StringComparison.Ordinal);
            return !isScram && !isCram
                ? throw new ArgumentException($"Unsupported SASL completion mechanism '{mechanism}'.", nameof(mechanism))
                : await FinalizeAuthenticationAsync(
                mechanism,
                username,
                clientIp,
                isTls,
                record => isScram ? record.AllowAuthScram256 : record.AllowAuthPlain,
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Clears per-exchange SASL staging when the client abandons or restarts authentication.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Implements <see cref="INntpSaslAccountAuthenticator.AbandonSaslExchange"/>. Called by the sockets authentication
        /// layer when the client issues a new AUTHINFO, starts a different SASL mechanism, or disconnects mid-exchange.
        /// </para>
        /// <para>
        /// Logs EventId <c>208</c> and calls <see cref="MySqlUserRecordSaslCache.Clear"/>. Does not clear the TTL burst cache
        /// (<see cref="MySqlUserRecordCache"/>). Idempotent when no record is staged.
        /// </para>
        /// </remarks>
        void INntpSaslAccountAuthenticator.AbandonSaslExchange()
        {
            SaslExchangeAbandoned(_logger);
            MySqlUserRecordSaslCache.Clear();
        }

        /// <summary>
        /// Validates a cleartext password for AUTHINFO PASS or SASL password mechanisms against MySQL.
        /// </summary>
        /// <param name="mechanism">
        /// Wire mechanism label (for example AUTHINFO PASS or SASL PLAIN). Used for logging and bounded metrics tags only.
        /// </param>
        /// <param name="username">
        /// Account name from the client. Whitespace-only values return <see cref="NntpAuthResult.InvalidCredentials"/> without
        /// throwing.
        /// </param>
        /// <param name="password">Password supplied by the client (may be empty).</param>
        /// <param name="clientIp">
        /// Client IP for logging and policy. Must not be <see langword="null"/>.
        /// </param>
        /// <param name="isTls">Whether the session transport is TLS-protected.</param>
        /// <param name="cancellationToken">
        /// Passed to <see cref="INntpUserRecordStore.TryGetUserAsync"/> on burst-cache miss.
        /// </param>
        /// <returns>
        /// <see cref="NntpAuthResult.Success"/> with <see cref="NntpSessionPolicy"/> when the password matches and policy allows;
        /// <see cref="NntpAuthResult.InvalidCredentials"/> otherwise; <see cref="NntpAuthResult.TransientFailure"/> on backend
        /// fault.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="clientIp"/> is <see langword="null"/>.
        /// </exception>
        /// <remarks>
        /// <para><b>Flow:</b></para>
        /// <list type="number">
        /// <item><description>Compute password fingerprint and try <see cref="MySqlUserRecordCache"/> (metrics <c>cache_hit</c> on hit).</description></item>
        /// <item><description>On miss, await <see cref="INntpUserRecordStore.TryGetUserAsync"/>.</description></item>
        /// <item><description>Reject when row missing, disabled, <see cref="MySqlUserRecord.AllowAuthPlain"/> false, or <see cref="PasswordEquals"/> false.</description></item>
        /// <item><description>On success: burst-cache <c>Put</c>, metrics <c>success</c>, return policy via <c>Succeed</c>.</description></item>
        /// </list>
        /// <para>
        /// Implements <see cref="INntpCredentialValidator.ValidatePasswordAsync"/>. Emits span
        /// <c>auth.mysql.validate.password</c>. <see cref="OperationCanceledException"/> propagates from the store lookup.
        /// </para>
        /// </remarks>
        async ValueTask<NntpAuthResult> INntpCredentialValidator.ValidatePasswordAsync(
            string mechanism,
            string username,
            string password,
            IPAddress clientIp,
            bool isTls,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                return NntpAuthResult.InvalidCredentials();
            }

            string clientIpText = FormatClientIp(clientIp);
            AuthenticationFinalizing(_logger, mechanism, username, clientIpText, isTls);

            using Activity? activity = AuthMySqlTelemetry.ActivitySource.StartActivity(
                "auth.mysql.validate.password",
                ActivityKind.Internal);

            try
            {
                byte[] fingerprint = MySqlUserRecordCache.ComputePasswordFingerprint(password);
                if (_authCache.TryGet(username, fingerprint, out MySqlUserRecord? record))
                {
                    _metrics.RecordLookup("cache_hit");
                }
                else
                {
                    record = await _recordStore
                        .TryGetUserAsync(username, cancellationToken)
                        .ConfigureAwait(false);
                }

                if (record is null)
                {
                    AuthenticationRejectedUserNotFound(_logger, mechanism, username, clientIpText);
                    _metrics.RecordValidate("invalid_credentials", MapMechanismMetric(mechanism));
                    return NntpAuthResult.InvalidCredentials();
                }

                if (!record.IsEnabled)
                {
                    AuthenticationRejectedAccountDisabled(_logger, mechanism, username, clientIpText);
                    _metrics.RecordValidate("invalid_credentials", MapMechanismMetric(mechanism));
                    return NntpAuthResult.InvalidCredentials();
                }

                if (!record.AllowAuthPlain)
                {
                    AuthenticationRejectedInvalidCredentials(_logger, mechanism, username, clientIpText);
                    _metrics.RecordValidate("invalid_credentials", MapMechanismMetric(mechanism));
                    return NntpAuthResult.InvalidCredentials();
                }

                if (!PasswordEquals(record.AccountPassword, password))
                {
                    AuthenticationRejectedInvalidCredentials(_logger, mechanism, username, clientIpText);
                    _metrics.RecordValidate("invalid_credentials", MapMechanismMetric(mechanism));
                    return NntpAuthResult.InvalidCredentials();
                }

                CacheSuccessfulAuth(username, fingerprint, record);
                _metrics.RecordValidate("success", MapMechanismMetric(mechanism));
                return Succeed(mechanism, record, clientIp);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AuthMySqlFailureReason reason = AuthMySqlFailureClassifier.Classify(ex);
                AuthenticationBackendFailed(_logger, ex, mechanism, username, reason);
                AuthenticationTransientFailure(_logger, mechanism, username, reason);
                _metrics.RecordValidate("transient_failure", MapMechanismMetric(mechanism));
                _ = (activity?.SetStatus(ActivityStatusCode.Error, reason.ToString()));
                return NntpAuthResult.TransientFailure();
            }
        }

        /// <summary>
        /// Compares decrypted and supplied passwords using constant-time ASCII byte comparison.
        /// </summary>
        /// <param name="storedPassword">Cleartext password from <see cref="MySqlUserRecord.AccountPassword"/>.</param>
        /// <param name="suppliedPassword">Password presented on the wire during AUTHINFO or SASL password mechanisms.</param>
        /// <returns>
        /// <see langword="true"/> only when both strings are non-null ASCII with identical content and equal length;
        /// <see langword="false"/> when either argument is <see langword="null"/>, contains non-ASCII code points, or bytes
        /// differ.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Pads both sides to <c>max(storedLength, suppliedLength)</c> before
        /// <see cref="CryptographicOperations.FixedTimeEquals"/> so timing does not shorten on length mismatch alone. The final
        /// result also requires <c>storedLength == suppliedLength</c> so padded comparisons cannot equate different lengths.
        /// </para>
        /// <para>
        /// Passwords up to <c>4096</c> characters use <c>stackalloc</c> buffers; longer passwords rent
        /// <see cref="ArrayPool{T}"/> arrays cleared on return. Exposed as <see langword="internal"/> for unit tests.
        /// </para>
        /// </remarks>
        internal bool PasswordEquals(string storedPassword, string suppliedPassword)
        {
            if (storedPassword is null || suppliedPassword is null)
            {
                return false;
            }

            if (!EncodingUtilities.IsAscii(storedPassword.AsSpan()) || !EncodingUtilities.IsAscii(suppliedPassword.AsSpan()))
            {
                return false;
            }

            int storedLength = storedPassword.Length;
            int suppliedLength = suppliedPassword.Length;
            int maxLength = Math.Max(storedLength, suppliedLength);

            const int StackallocThresholdBytes = 4096;

            if (maxLength <= StackallocThresholdBytes)
            {
                Span<byte> left = stackalloc byte[maxLength];
                Span<byte> right = stackalloc byte[maxLength];
                left.Clear();
                right.Clear();
                _ = EncodingUtilities.AsciiToSpan(storedPassword.AsSpan(), left);
                _ = EncodingUtilities.AsciiToSpan(suppliedPassword.AsSpan(), right);
                bool equals = CryptographicOperations.FixedTimeEquals(left, right);
                return equals && storedLength == suppliedLength;
            }

            byte[] leftArray = ArrayPool<byte>.Shared.Rent(maxLength);
            byte[] rightArray = ArrayPool<byte>.Shared.Rent(maxLength);
            try
            {
                Span<byte> left = leftArray.AsSpan(0, maxLength);
                Span<byte> right = rightArray.AsSpan(0, maxLength);
                left.Clear();
                right.Clear();
                _ = EncodingUtilities.AsciiToSpan(storedPassword.AsSpan(), left);
                _ = EncodingUtilities.AsciiToSpan(suppliedPassword.AsSpan(), right);
                bool equals = CryptographicOperations.FixedTimeEquals(left, right);
                return equals && storedLength == suppliedLength;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(leftArray, clearArray: true);
                ArrayPool<byte>.Shared.Return(rightArray, clearArray: true);
            }
        }

        /// <summary>
        /// Normalises and stringifies a client IP for structured authentication logs.
        /// </summary>
        /// <param name="clientIp">Session client address. Must not be <see langword="null"/>.</param>
        /// <returns>
        /// <see cref="IPAddress.ToString"/> after <see cref="FormattingUtilities.NormaliseAddress"/> (IPv4-mapped IPv6 addresses
        /// map to IPv4 text).
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="clientIp"/> is <see langword="null"/>.
        /// </exception>
        private static string FormatClientIp(IPAddress clientIp)
        {
            return FormattingUtilities.NormaliseAddress(clientIp).ToString();
        }

        /// <summary>
        /// Maps wire mechanism labels to low-cardinality <see cref="AuthMySqlMetrics.RecordValidate"/> mechanism tags.
        /// </summary>
        /// <param name="mechanism">Authentication mechanism label from the sockets layer.</param>
        /// <returns>
        /// <c>sasl_scram</c> for <see cref="NntpAuthMechanisms.SaslScramSha256"/>, <c>sasl_cram</c> for
        /// <see cref="NntpAuthMechanisms.SaslCramMd5"/>, otherwise <c>authinfo</c> (all other password mechanisms).
        /// </returns>
        /// <remarks>Prevents unbounded mechanism strings from entering metrics cardinality.</remarks>
        private static string MapMechanismMetric(string mechanism)
        {
            return string.Equals(mechanism, NntpAuthMechanisms.SaslScramSha256, StringComparison.Ordinal)
                ? "sasl_scram"
                : string.Equals(mechanism, NntpAuthMechanisms.SaslCramMd5, StringComparison.Ordinal) ? "sasl_cram" : "authinfo";
        }

        /// <summary>
        /// Materialises <see cref="NntpSessionPolicy"/> from a validated <see cref="MySqlUserRecord"/>.
        /// </summary>
        /// <param name="record">Authenticated row snapshot. Must not be <see langword="null"/>.</param>
        /// <returns>
        /// Policy with posting allowed, rate/byte limits from the row, and BLAKE3 account key from
        /// <see cref="IAccountKeyNormalizer"/>.
        /// </returns>
        /// <remarks>
        /// Maps limit columns through <see cref="NntpAccountLimits"/> and
        /// <see cref="NntpSessionPolicyFactory.Create"/> with <c>allowPosting: true</c> for all successful authentications in
        /// this validator.
        /// </remarks>
        private NntpSessionPolicy CreatePolicy(MySqlUserRecord record)
        {
            NntpAccountLimits limits = new(
                record.AccountName,
                record.AccountType,
                record.RateLimit,
                record.ByteLimit,
                record.SessionLimit,
                record.SrcIpLimit,
                record.CustomerId);
            return NntpSessionPolicyFactory.Create(limits, allowPosting: true, _accountKeyNormalizer);
        }

        /// <summary>
        /// Shared SASL and password-finalization path after wire verification (account policy and caching).
        /// </summary>
        /// <param name="mechanism">Authentication mechanism label for logs and metrics.</param>
        /// <param name="username">Account name to finalize.</param>
        /// <param name="clientIp">Client IP for logging and policy. Must not be <see langword="null"/>.</param>
        /// <param name="isTls">Whether TLS is active on the session.</param>
        /// <param name="isMechanismPermitted">
        /// Row policy predicate (SCRAM vs CRAM/plain flags) evaluated after enablement check.
        /// </param>
        /// <param name="cancellationToken">Honoured on async store lookup when per-exchange cache misses.</param>
        /// <returns>
        /// <see cref="NntpAuthResult"/> with the same semantics as password and SASL public entry points.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="clientIp"/> is <see langword="null"/>.
        /// </exception>
        /// <remarks>
        /// <para><b>Flow:</b></para>
        /// <list type="number">
        /// <item><description><see cref="MySqlUserRecordSaslCache.TryTake"/> or async store lookup on miss.</description></item>
        /// <item><description>Reject on missing row, disabled account, or failed <paramref name="isMechanismPermitted"/>.</description></item>
        /// <item><description>Cache username-only burst entry, record metrics <c>success</c>, return <c>Succeed</c>.</description></item>
        /// </list>
        /// <para>
        /// Emits span <c>auth.mysql.validate.sasl</c>. Always clears <see cref="MySqlUserRecordSaslCache"/> in <c>finally</c>.
        /// Backend faults map to <see cref="NntpAuthResult.TransientFailure"/>.
        /// </para>
        /// </remarks>
        private async ValueTask<NntpAuthResult> FinalizeAuthenticationAsync(
            string mechanism,
            string username,
            IPAddress clientIp,
            bool isTls,
            Func<MySqlUserRecord, bool> isMechanismPermitted,
            CancellationToken cancellationToken)
        {
            string clientIpText = FormatClientIp(clientIp);
            AuthenticationFinalizing(_logger, mechanism, username, clientIpText, isTls);

            using Activity? activity = AuthMySqlTelemetry.ActivitySource.StartActivity(
                "auth.mysql.validate.sasl",
                ActivityKind.Internal);

            try
            {
                if (MySqlUserRecordSaslCache.TryTake(username, out MySqlUserRecord? record))
                {
                    SaslCacheHit(_logger, username);
                }
                else
                {
                    SaslCacheMiss(_logger, username);
                    record = await _recordStore
                        .TryGetUserAsync(username, cancellationToken)
                        .ConfigureAwait(false);
                }

                if (record is null)
                {
                    AuthenticationRejectedUserNotFound(_logger, mechanism, username, clientIpText);
                    _metrics.RecordValidate("invalid_credentials", MapMechanismMetric(mechanism));
                    return NntpAuthResult.InvalidCredentials();
                }

                if (!record.IsEnabled)
                {
                    AuthenticationRejectedAccountDisabled(_logger, mechanism, username, clientIpText);
                    _metrics.RecordValidate("invalid_credentials", MapMechanismMetric(mechanism));
                    return NntpAuthResult.InvalidCredentials();
                }

                if (!isMechanismPermitted(record))
                {
                    AuthenticationRejectedInvalidCredentials(_logger, mechanism, username, clientIpText);
                    _metrics.RecordValidate("invalid_credentials", MapMechanismMetric(mechanism));
                    return NntpAuthResult.InvalidCredentials();
                }

                CacheSuccessfulAuth(username, MySqlUserRecordCache.UsernameOnlyFingerprint, record);
                _metrics.RecordValidate("success", MapMechanismMetric(mechanism));
                return Succeed(mechanism, record, clientIp);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AuthMySqlFailureReason reason = AuthMySqlFailureClassifier.Classify(ex);
                AuthenticationBackendFailed(_logger, ex, mechanism, username, reason);
                AuthenticationTransientFailure(_logger, mechanism, username, reason);
                _metrics.RecordValidate("transient_failure", MapMechanismMetric(mechanism));
                _ = (activity?.SetStatus(ActivityStatusCode.Error, reason.ToString()));
                return NntpAuthResult.TransientFailure();
            }
            finally
            {
                MySqlUserRecordSaslCache.Clear();
            }
        }

        /// <summary>
        /// Inserts an AES-256-GCM protected snapshot into the post-success burst cache.
        /// </summary>
        /// <param name="username">Authenticated account name (cache key component).</param>
        /// <param name="fingerprint">
        /// Password SHA-256 fingerprint from <see cref="MySqlUserRecordCache.ComputePasswordFingerprint"/> or
        /// <see cref="MySqlUserRecordCache.UsernameOnlyFingerprint"/> for SASL finalize.
        /// </param>
        /// <param name="record">Validated row to cache. Must not be <see langword="null"/>.</param>
        /// <remarks>
        /// Delegates to <see cref="MySqlUserRecordCache.Put"/>. Entries expire by TTL only; failed authentications never call
        /// this helper.
        /// </remarks>
        private void CacheSuccessfulAuth(string username, byte[] fingerprint, MySqlUserRecord record)
        {
            _authCache.Put(username, fingerprint, record);
        }

        /// <summary>
        /// Builds session policy, logs EventId <c>204</c>, and returns a success auth result.
        /// </summary>
        /// <param name="mechanism">Authentication mechanism label.</param>
        /// <param name="record">Validated user record used to construct policy.</param>
        /// <param name="clientIp">Client IP for the success log line. Must not be <see langword="null"/>.</param>
        /// <returns><see cref="NntpAuthResult.Success"/> carrying the materialised <see cref="NntpSessionPolicy"/>.</returns>
        /// <remarks>
        /// Does not perform session admission, quota checks, or cluster-wide session limits — the session coordinator consumes
        /// the returned policy afterward.
        /// </remarks>
        private NntpAuthResult Succeed(string mechanism, MySqlUserRecord record, IPAddress clientIp)
        {
            NntpSessionPolicy policy = CreatePolicy(record);
            string clientIpText = FormatClientIp(clientIp);
            AuthenticationSucceeded(
                _logger,
                mechanism,
                policy.Username,
                clientIpText,
                policy.AllowPosting,
                policy.AccountType,
                policy.CustomerId);

            return NntpAuthResult.Success(policy);
        }
    }
}
