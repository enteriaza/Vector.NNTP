// <copyright file="MySqlNntpCredentialValidator.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: INntpCredentialValidator implementation backed by a MySQL nntpusers table.

using System.Net;
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
        private readonly INntpUserRecordStore _recordStore = recordStore ?? throw new ArgumentNullException(nameof(recordStore));
        private readonly INntpSessionAdmissionTracker _admissionTracker = admissionTracker ?? throw new ArgumentNullException(nameof(admissionTracker));
        private readonly ILogger<MySqlNntpCredentialValidator> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        /// <inheritdoc />
        public async ValueTask<NntpAuthResult> ValidatePasswordAsync(
            string username,
            string password,
            IPAddress clientIp,
            bool isTls,
            CancellationToken cancellationToken)
        {
            _ = isTls;

            if (string.IsNullOrEmpty(username))
            {
                return NntpAuthResult.InvalidCredentials();
            }

            try
            {
                MySqlUserRecord? record = await _recordStore
                    .TryGetUserAsync(username, cancellationToken)
                    .ConfigureAwait(false);

                if (record is null || !record.IsEnabled)
                {
                    return NntpAuthResult.InvalidCredentials();
                }

                if (!PasswordEquals(record.AccountPassword, password))
                {
                    return NntpAuthResult.InvalidCredentials();
                }

                NntpSessionPolicy policy = CreatePolicy(record);
                return !_admissionTracker.TryEnter(policy, clientIp) ? NntpAuthResult.TransientFailure() : NntpAuthResult.Success(policy);
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
            return string.Equals(storedPassword, suppliedPassword, StringComparison.Ordinal);
        }

        /// <summary>
        /// Creates an <see cref="NntpSessionPolicy"/> from the supplied user record.
        /// </summary>
        /// <param name="record">User record materialised from the backing store.</param>
        /// <returns>Session policy representing the granted permissions and limits.</returns>
        private NntpSessionPolicy CreatePolicy(MySqlUserRecord record)
        {
            bool allowPosting = record.AccountType is 'R' or 'r' or 'B' or 'b';
            return new NntpSessionPolicy(
                record.AccountName,
                allowPosting,
                record.AccountType,
                record.CustomerId,
                record.RateLimit,
                record.ByteLimit,
                record.SessionLimit,
                record.SrcIpLimit);
        }
    }
}
