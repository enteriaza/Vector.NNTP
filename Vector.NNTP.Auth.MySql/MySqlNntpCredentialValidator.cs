// <copyright file="MySqlNntpCredentialValidator.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Buffers;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Vector.NNTP.Sockets.Authentication;

namespace Vector.NNTP.Auth.MySql
{
    /// <summary>
    /// MySQL-backed implementation of <see cref="INntpCredentialValidator"/> that validates passwords and policy
    /// against rows in the <c>nntpusers</c> table.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Password handling:</b> The underlying <see cref="INntpUserRecordStore"/> executes a parameterised query that
    /// decrypts <c>account_pass</c> using <c>AES_DECRYPT</c> and casts it to <c>CHAR</c>. This validator compares the
    /// supplied password with the decrypted value using an ordinal, case-sensitive comparison.
    /// </para>
    /// <para>
    /// <b>Session admission:</b> On successful password verification a <see cref="NntpSessionPolicy"/> is constructed
    /// and passed to <see cref="INntpSessionAdmissionTracker"/> to enforce per-account and per-source-IP limits. When
    /// limits are exceeded the validator returns <see cref="NntpAuthResult.TransientFailure"/>.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// Initializes a new instance of the <see cref="MySqlNntpCredentialValidator"/> class.
    /// </remarks>
    /// <param name="recordStore">Backing user record store.</param>
    /// <param name="admissionTracker">Session admission tracker enforcing concurrency limits.</param>
    /// <param name="logger">Logger for backend/auth failures.</param>
    public sealed class MySqlNntpCredentialValidator(
        INntpUserRecordStore recordStore,
        INntpSessionAdmissionTracker admissionTracker,
        ILogger<MySqlNntpCredentialValidator> logger) : INntpCredentialValidator
    {
        /// <summary>
        /// Backing user record store.
        /// </summary>
        private readonly INntpUserRecordStore _recordStore = recordStore ?? throw new ArgumentNullException(nameof(recordStore));

        /// <summary>
        /// Session admission tracker enforcing concurrency limits.
        /// </summary>
        private readonly INntpSessionAdmissionTracker _admissionTracker = admissionTracker ?? throw new ArgumentNullException(nameof(admissionTracker));

        /// <summary>
        /// Logger for backend/auth failures.
        /// </summary>
        private readonly ILogger<MySqlNntpCredentialValidator> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        /// <summary>
        /// Validates a password against a user record.
        /// </summary>
        /// <param name="username">Username supplied by the client.</param>
        /// <param name="password">Password supplied by the client.</param>
        /// <param name="clientIp">Client IP address for policy and limit enforcement.</param>
        /// <param name="isTls">Whether the connection is TLS-protected.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>An authentication result describing success, invalid credentials, or transient failures.</returns>
        public async ValueTask<NntpAuthResult> ValidatePasswordAsync(
            string username,
            string password,
            IPAddress clientIp,
            bool isTls,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(username))
            {
                return NntpAuthResult.InvalidCredentials();
            }

            MySqlNntpCredentialValidatorLog.ValidationAttemptStarted(
                _logger,
                username,
                clientIp.ToString(),
                isTls);

            try
            {
                MySqlUserRecord? record = await _recordStore
                    .TryGetUserAsync(username, cancellationToken)
                    .ConfigureAwait(false);

                if (record is null)
                {
                    MySqlNntpCredentialValidatorLog.ValidationRejectedUserNotFound(_logger, username, clientIp.ToString());
                    return NntpAuthResult.InvalidCredentials();
                }

                if (!record.IsEnabled)
                {
                    MySqlNntpCredentialValidatorLog.ValidationRejectedAccountDisabled(_logger, username, clientIp.ToString());
                    return NntpAuthResult.InvalidCredentials();
                }

                if (!record.AllowAuthPlain)
                {
                    MySqlNntpCredentialValidatorLog.ValidationRejectedInvalidCredentials(_logger, username, clientIp.ToString());
                    return NntpAuthResult.InvalidCredentials();
                }

                if (!PasswordEquals(record.AccountPassword, password))
                {
                    MySqlNntpCredentialValidatorLog.ValidationRejectedInvalidCredentials(_logger, username, clientIp.ToString());
                    return NntpAuthResult.InvalidCredentials();
                }

                NntpSessionPolicy policy = CreatePolicy(record);
                if (!_admissionTracker.TryEnter(policy, clientIp))
                {
                    MySqlNntpCredentialValidatorLog.AdmissionRejected(
                        _logger,
                        policy.Username,
                        clientIp.ToString(),
                        policy.SessionLimit,
                        policy.SrcIpLimit);
                    return NntpAuthResult.TransientFailure();
                }

                MySqlNntpCredentialValidatorLog.AuthenticationSucceeded(
                    _logger,
                    policy.Username,
                    clientIp.ToString(),
                    policy.AllowPosting,
                    policy.AccountType,
                    policy.CustomerId);

                return NntpAuthResult.Success(policy);
            }
            catch (OperationCanceledException)
            {
                // Preserve shutdown/timeout semantics.
                throw;
            }
            catch (Exception ex)
            {
                // Prevent backend failures from escaping to the session loop (which would drop the connection).
                // Treat as transient authentication failure to match NNTP semantics (503).
                MySqlNntpCredentialValidatorLog.CredentialValidationBackendFailed(_logger, ex, username);
                return NntpAuthResult.TransientFailure();
            }
        }

        /// <summary>
        /// Compares the stored password with the supplied password.
        /// </summary>
        /// <param name="storedPassword">Decrypted password from the data store.</param>
        /// <param name="suppliedPassword">Password supplied by the client.</param>
        /// <returns><see langword="true"/> when the passwords match.</returns>
        internal bool PasswordEquals(string storedPassword, string suppliedPassword)
        {
            if (storedPassword is null || suppliedPassword is null)
            {
                return false;
            }

            byte[] storedBytes = Encoding.UTF8.GetBytes(storedPassword);
            byte[] suppliedBytes = Encoding.UTF8.GetBytes(suppliedPassword);
            int storedLength = storedBytes.Length;
            int suppliedLength = suppliedBytes.Length;
            int maxLength = Math.Max(storedLength, suppliedLength);

            const int StackallocThresholdBytes = 4096;

            if (maxLength <= StackallocThresholdBytes)
            {
                Span<byte> left = stackalloc byte[maxLength];
                Span<byte> right = stackalloc byte[maxLength];
                left.Clear();
                right.Clear();
                storedBytes.CopyTo(left);
                suppliedBytes.CopyTo(right);
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
                storedBytes.CopyTo(left);
                suppliedBytes.CopyTo(right);
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
        /// Creates an <see cref="NntpSessionPolicy"/> from the supplied user record.
        /// </summary>
        /// <param name="record">User record materialised from the backing store.</param>
        /// <returns>Session policy representing the granted permissions and limits.</returns>
        private NntpSessionPolicy CreatePolicy(MySqlUserRecord record)
        {
            return new NntpSessionPolicy(
                record.AccountName,
                allowPosting: true,
                record.AccountType,
                record.CustomerId,
                record.RateLimit,
                record.ByteLimit,
                record.SessionLimit,
                record.SrcIpLimit);
        }
    }
}
