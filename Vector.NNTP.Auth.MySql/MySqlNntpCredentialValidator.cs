// <copyright file="MySqlNntpCredentialValidator.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Vector.NNTP.Session.Accounts;
using Vector.NNTP.Session.Coordination;
using Vector.NNTP.Session.Policy;
using Vector.NNTP.Sockets.Authentication;
using Vector.NNTP.Utilities.Diagnostics;
using Vector.NNTP.Utilities.Encoding;

namespace Vector.NNTP.Auth.MySql
{
    /// <summary>
    /// MySQL-backed implementation of <see cref="INntpCredentialValidator"/> and <see cref="INntpSaslAccountAuthenticator"/>
    /// that validates credentials and policy against rows in the <c>nntpusers</c> table.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Password handling:</b> The underlying <see cref="INntpUserRecordStore"/> executes a parameterised query that
    /// decrypts <c>account_pass</c> using <c>AES_DECRYPT</c> and casts it to <c>CHAR</c>. This validator compares the
    /// supplied password with the decrypted value using a constant-time ASCII comparison. Passwords must be US-ASCII;
    /// non-ASCII input is rejected without comparison.
    /// </para>
    /// <para>
    /// <b>Session admission:</b> Credential validation returns <see cref="NntpSessionPolicy"/> only. Distributed admission
    /// is performed by <see cref="NntpAuthenticationService"/> via <see cref="INntpSessionCoordinator"/>.
    /// </para>
    /// </remarks>
    public sealed partial class MySqlNntpCredentialValidator : INntpCredentialValidator, INntpSaslAccountAuthenticator
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
        /// Logger for backend/auth failures.
        /// </summary>
        private readonly ILogger<MySqlNntpCredentialValidator> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="MySqlNntpCredentialValidator"/> class.
        /// </summary>
        /// <param name="recordStore">Backing user record store.</param>
        /// <param name="accountKeyNormalizer">Account key normalizer for policy construction.</param>
        /// <param name="logger">Logger for backend/auth failures.</param>
        internal MySqlNntpCredentialValidator(
            INntpUserRecordStore recordStore,
            IAccountKeyNormalizer accountKeyNormalizer,
            ILogger<MySqlNntpCredentialValidator> logger)
        {
            _recordStore = recordStore ?? throw new ArgumentNullException(nameof(recordStore));
            _accountKeyNormalizer = accountKeyNormalizer ?? throw new ArgumentNullException(nameof(accountKeyNormalizer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Completes SASL authentication for the given account.
        /// </summary>
        /// <param name="mechanism">The mechanism to use for authentication.</param>
        /// <param name="username">The username to authenticate.</param>
        /// <param name="clientIp">The client IP address.</param>
        /// <param name="isTls">Whether the connection is TLS-protected.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The authentication result.</returns>
        /// <exception cref="ArgumentException">Thrown when the mechanism is not supported.</exception>
        public async ValueTask<NntpAuthResult> CompleteSaslAccountAsync(
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
        /// Abandons the SASL exchange and clears the cached user record.
        /// </summary>
        public void AbandonSaslExchange()
        {
            MySqlUserRecordSaslCache.Clear();
        }

        /// <summary>
        /// Validates the password for the given account.
        /// </summary>
        /// <param name="mechanism">The mechanism to use for authentication.</param>
        /// <param name="username">The username to authenticate.</param>
        /// <param name="password">The password to authenticate.</param>
        /// <param name="clientIp">The client IP address.</param>
        /// <param name="isTls">Whether the connection is TLS-protected.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The authentication result.</returns>
        /// <exception cref="ArgumentException">Thrown when the mechanism is not supported.</exception>
        public async ValueTask<NntpAuthResult> ValidatePasswordAsync(
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

            try
            {
                MySqlUserRecord? record = await _recordStore
                    .TryGetUserAsync(username, cancellationToken)
                    .ConfigureAwait(false);

                if (record is null)
                {
                    AuthenticationRejectedUserNotFound(_logger, mechanism, username, clientIpText);
                    return NntpAuthResult.InvalidCredentials();
                }

                if (!record.IsEnabled)
                {
                    AuthenticationRejectedAccountDisabled(_logger, mechanism, username, clientIpText);
                    return NntpAuthResult.InvalidCredentials();
                }

                if (!record.AllowAuthPlain)
                {
                    AuthenticationRejectedInvalidCredentials(_logger, mechanism, username, clientIpText);
                    return NntpAuthResult.InvalidCredentials();
                }

                if (!PasswordEquals(record.AccountPassword, password))
                {
                    AuthenticationRejectedInvalidCredentials(_logger, mechanism, username, clientIpText);
                    return NntpAuthResult.InvalidCredentials();
                }

                return Succeed(mechanism, record, clientIp);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AuthenticationBackendFailed(_logger, ex, mechanism, username);
                return NntpAuthResult.TransientFailure();
            }
        }

        /// <summary>
        /// Compares the stored password with the supplied password using constant-time ASCII encoding.
        /// </summary>
        /// <param name="storedPassword">Decrypted password from the data store.</param>
        /// <param name="suppliedPassword">Password supplied by the client.</param>
        /// <returns><see langword="true"/> when the passwords match.</returns>
        /// <remarks>
        /// <para><b>Charset:</b> Both operands must be pure US-ASCII. Non-ASCII input returns <see langword="false"/>
        /// without throwing.</para>
        /// <para><b>Encoding:</b> Uses <see cref="EncodingUtilities.AsciiToSpan"/> (BCL SIMD on x64) into zero-padded
        /// compare buffers; no intermediate heap byte arrays for password material.</para>
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
        private static string FormatClientIp(IPAddress clientIp)
        {
            return FormattingUtilities.NormaliseAddress(clientIp).ToString();
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

            try
            {
                if (!MySqlUserRecordSaslCache.TryTake(username, out MySqlUserRecord? record))
                {
                    record = await _recordStore
                        .TryGetUserAsync(username, cancellationToken)
                        .ConfigureAwait(false);
                }

                if (record is null)
                {
                    AuthenticationRejectedUserNotFound(_logger, mechanism, username, clientIpText);
                    return NntpAuthResult.InvalidCredentials();
                }

                if (!record.IsEnabled)
                {
                    AuthenticationRejectedAccountDisabled(_logger, mechanism, username, clientIpText);
                    return NntpAuthResult.InvalidCredentials();
                }

                if (!isMechanismPermitted(record))
                {
                    AuthenticationRejectedInvalidCredentials(_logger, mechanism, username, clientIpText);
                    return NntpAuthResult.InvalidCredentials();
                }

                return Succeed(mechanism, record, clientIp);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AuthenticationBackendFailed(_logger, ex, mechanism, username);
                return NntpAuthResult.TransientFailure();
            }
            finally
            {
                MySqlUserRecordSaslCache.Clear();
            }
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
