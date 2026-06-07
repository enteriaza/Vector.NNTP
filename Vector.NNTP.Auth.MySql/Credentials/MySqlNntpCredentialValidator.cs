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
    /// MySQL-backed implementation of <see cref="INntpCredentialValidator"/> and <see cref="INntpSaslAccountAuthenticator"/>
    /// that validates credentials and policy against rows in the <c>nntpusers</c> table.
    /// </summary>
    /// <remarks>
    /// <para><b>Outcomes:</b> Invalid credentials return <see cref="NntpAuthResult.InvalidCredentials"/>; backend failures
    /// return <see cref="NntpAuthResult.TransientFailure"/> so the sockets layer can answer with 503.</para>
    /// <para><b>Burst cache:</b> Successful AUTHINFO and SASL completions populate <see cref="MySqlUserRecordCache"/> with
    /// AES-256-GCM protected snapshots and a short TTL for concurrent duplicate logons.</para>
    /// <para><b>SASL staging:</b> Credential stores stash records in <see cref="MySqlUserRecordSaslCache"/> for the
    /// completion step; <see cref="INntpSaslAccountAuthenticator.AbandonSaslExchange"/> clears that slot on auth reset.</para>
    /// <para><b>Password compare:</b> <see cref="PasswordEquals"/> uses constant-time ASCII comparison for AUTHINFO paths.</para>
    /// </remarks>
    internal sealed partial class MySqlNntpCredentialValidator : INntpCredentialValidator, INntpSaslAccountAuthenticator
    {
        /// <summary>
        /// Backing user record store.
        /// </summary>
        private readonly INntpUserRecordStore _recordStore;

        /// <summary>
        /// Account key normalizer for policy construction.
        /// </summary>
        private readonly IAccountKeyNormalizer _accountKeyNormalizer;

        /// <summary>
        /// Successful-authentication cache for burst deduplication.
        /// </summary>
        private readonly MySqlUserRecordCache _authCache;

        /// <summary>
        /// Metrics for validation outcomes.
        /// </summary>
        private readonly AuthMySqlMetrics _metrics;

        /// <summary>
        /// Logger for backend/auth failures.
        /// </summary>
        private readonly ILogger<MySqlNntpCredentialValidator> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="MySqlNntpCredentialValidator"/> class.
        /// </summary>
        /// <param name="recordStore">Backing user record store.</param>
        /// <param name="accountKeyNormalizer">Account key normalizer for policy construction.</param>
        /// <param name="authCache">Successful-authentication cache.</param>
        /// <param name="metrics">Metrics for validation outcomes.</param>
        /// <param name="logger">Logger for backend/auth failures.</param>
        /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
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
        /// Finalizes SCRAM-SHA-256 or CRAM-MD5 authentication after wire-level cryptographic verification succeeds.
        /// </summary>
        /// <param name="mechanism">SASL mechanism label (<see cref="NntpAuthMechanisms.SaslScramSha256"/> or <see cref="NntpAuthMechanisms.SaslCramMd5"/>).</param>
        /// <param name="username">Authenticated username supplied by the client.</param>
        /// <param name="clientIp">Client IP address used for structured logs and session policy materialisation.</param>
        /// <param name="isTls">Whether the NNTP session transport is TLS-protected at completion time.</param>
        /// <param name="cancellationToken">Cancellation token for the backing user-record lookup on cache miss.</param>
        /// <returns>
        /// <see cref="NntpAuthResult.Success"/> with <see cref="NntpSessionPolicy"/> when the account is enabled and the
        /// mechanism is permitted; <see cref="NntpAuthResult.InvalidCredentials"/> for policy or lookup failures;
        /// <see cref="NntpAuthResult.TransientFailure"/> when MySQL I/O fails.
        /// </returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="mechanism"/> is not SCRAM-SHA-256 or CRAM-MD5.</exception>
        /// <remarks>
        /// <para>
        /// Consumes a record stashed by <see cref="MySqlScramCredentialStore"/> or <see cref="MySqlCramMd5CredentialStore"/>
        /// via <see cref="MySqlUserRecordSaslCache"/> when available; otherwise queries
        /// <see cref="INntpUserRecordStore.TryGetUserAsync"/>.
        /// </para>
        /// <para>
        /// <see cref="MySqlUserRecordSaslCache.Clear"/> runs in a <c>finally</c> block so the per-exchange slot does not
        /// leak across authentications. <see cref="OperationCanceledException"/> propagates when the lookup is cancelled.
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
        /// Clears staged SASL user-record material when the client resets authentication mid-exchange.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Invoked by the sockets authentication layer from
        /// <see cref="INntpSaslAccountAuthenticator.AbandonSaslExchange"/> when the client issues a new AUTHINFO or
        /// abandons an in-progress SASL dialog.
        /// </para>
        /// <para>Clears <see cref="MySqlUserRecordSaslCache"/> and logs the abandonment. Idempotent when no exchange is active.</para>
        /// </remarks>
        void INntpSaslAccountAuthenticator.AbandonSaslExchange()
        {
            SaslExchangeAbandoned(_logger);
            MySqlUserRecordSaslCache.Clear();
        }

        /// <summary>
        /// Validates a password for AUTHINFO PASS or SASL password mechanisms against the MySQL user store.
        /// </summary>
        /// <param name="mechanism">Authentication mechanism label (for example AUTHINFO PASS or SASL PLAIN).</param>
        /// <param name="username">Username supplied by the client.</param>
        /// <param name="password">Password supplied by the client.</param>
        /// <param name="clientIp">Client IP address.</param>
        /// <param name="isTls">Whether the connection is TLS-protected.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Authentication outcome and optional session policy.</returns>
        /// <remarks>
        /// A null or whitespace <paramref name="username"/> yields <see cref="NntpAuthResult.InvalidCredentials"/> rather
        /// than throwing. <see cref="OperationCanceledException"/> propagates when the backing lookup is cancelled.
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
        /// Compares the stored password with the supplied password using constant-time ASCII encoding.
        /// </summary>
        /// <param name="storedPassword">Decrypted password from the data store.</param>
        /// <param name="suppliedPassword">Password supplied by the client.</param>
        /// <returns><see langword="true"/> when the passwords match; <see langword="false"/> when either argument is null,
        /// non-ASCII, or the values differ.</returns>
        /// <remarks>
        /// Pads both sides to the longer length before <see cref="CryptographicOperations.FixedTimeEquals"/> so length
        /// differences do not leak via early exit. Large passwords rent pooled buffers cleared on return.
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
        /// Formats a client IP for structured authentication logs.
        /// </summary>
        /// <param name="clientIp">Client IP address.</param>
        /// <returns>Normalised textual IP representation.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="clientIp"/> is null.</exception>
        private static string FormatClientIp(IPAddress clientIp)
        {
            return FormattingUtilities.NormaliseAddress(clientIp).ToString();
        }

        /// <summary>
        /// Maps mechanism labels to bounded metric mechanism names.
        /// </summary>
        /// <param name="mechanism">Authentication mechanism label.</param>
        /// <returns>Bounded metric label.</returns>
        private static string MapMechanismMetric(string mechanism)
        {
            return string.Equals(mechanism, NntpAuthMechanisms.SaslScramSha256, StringComparison.Ordinal)
                ? "sasl_scram"
                : string.Equals(mechanism, NntpAuthMechanisms.SaslCramMd5, StringComparison.Ordinal) ? "sasl_cram" : "authinfo";
        }

        /// <summary>
        /// Creates an <see cref="NntpSessionPolicy"/> from the supplied user record.
        /// </summary>
        /// <param name="record">User record materialised from the backing store.</param>
        /// <returns>Session policy representing the granted permissions and limits.</returns>
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
        /// Finalizes SASL authentication after cryptographic verification on the wire.
        /// </summary>
        /// <param name="mechanism">Authentication mechanism label.</param>
        /// <param name="username">Authenticated username.</param>
        /// <param name="clientIp">Client IP address.</param>
        /// <param name="isTls">Whether the connection is TLS-protected.</param>
        /// <param name="isMechanismPermitted">Delegate that checks whether the mechanism is allowed for the account.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Authentication outcome and optional session policy.</returns>
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
        /// Caches a successful authentication record for burst deduplication.
        /// </summary>
        /// <param name="username">Authenticated username.</param>
        /// <param name="fingerprint">Credential fingerprint or username-only sentinel.</param>
        /// <param name="record">Validated user record.</param>
        /// <remarks>
        /// The cache encrypts password and SCRAM key material at rest and expires entries within a short TTL window.
        /// </remarks>
        private void CacheSuccessfulAuth(string username, byte[] fingerprint, MySqlUserRecord record)
        {
            _authCache.Put(username, fingerprint, record);
        }

        /// <summary>
        /// Logs successful authentication and returns policy without admission.
        /// </summary>
        /// <param name="mechanism">Authentication mechanism label.</param>
        /// <param name="record">Validated user record.</param>
        /// <param name="clientIp">Client IP address.</param>
        /// <returns>Authentication outcome and session policy.</returns>
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
