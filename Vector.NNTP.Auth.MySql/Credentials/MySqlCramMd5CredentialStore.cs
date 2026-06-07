// <copyright file="MySqlCramMd5CredentialStore.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: ICramMd5CredentialStore implementation backed by the MySQL nntpusers table.

using Microsoft.Extensions.Logging;
using Vector.NNTP.Auth.MySql.Configuration;
using Vector.NNTP.Auth.MySql.Records;
using Vector.NNTP.Sockets.Authentication;
using Vector.NNTP.Utilities.Encoding;

namespace Vector.NNTP.Auth.MySql.Credentials
{
    /// <summary>
    /// MySQL-backed implementation of <see cref="ICramMd5CredentialStore"/> that reuses the NNTP user record query.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Secret material:</b> The decrypted <c>account_pass</c> column is converted to ASCII bytes and used as the
    /// CRAM-MD5 shared secret when the password is US-ASCII. Non-ASCII passwords are rejected.
    /// </para>
    /// <para>
    /// <b>Cancellation:</b> <see cref="ICramMd5CredentialStore"/> exposes no token, so lookups use
    /// <see cref="CancellationToken.None"/> and cannot be aborted when the client disconnects mid-query.
    /// </para>
    /// </remarks>
    internal sealed partial class MySqlCramMd5CredentialStore : ICramMd5CredentialStore
    {
        /// <summary>
        /// Backing user record store.
        /// </summary>
        private readonly INntpUserRecordStore _recordStore;

        /// <summary>
        /// Logger for CRAM-MD5 credential lookup diagnostics.
        /// </summary>
        private readonly ILogger<MySqlCramMd5CredentialStore> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="MySqlCramMd5CredentialStore"/> class.
        /// </summary>
        /// <param name="recordStore">Backing user record store.</param>
        /// <param name="logger">Logger for CRAM-MD5 credential lookup diagnostics.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="recordStore"/> or <paramref name="logger"/> is null.</exception>
        internal MySqlCramMd5CredentialStore(
            INntpUserRecordStore recordStore,
            ILogger<MySqlCramMd5CredentialStore> logger)
        {
            _recordStore = recordStore ?? throw new ArgumentNullException(nameof(recordStore));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Resolves the CRAM-MD5 shared secret for <paramref name="username"/> from the MySQL user store.
        /// </summary>
        /// <param name="username">Plaintext NNTP username supplied by the client.</param>
        /// <param name="secret">US-ASCII password bytes used as the HMAC key when lookup succeeds; empty on denial.</param>
        /// <returns>
        /// <see langword="true"/> when the account exists, is enabled, permits password-based mechanisms, and has a
        /// US-ASCII password; otherwise <see langword="false"/> without throwing.
        /// </returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="username"/> is null or empty.</exception>
        /// <exception cref="NntpCredentialStoreTransientException">Thrown when the backing store fails due to a backend error.</exception>
        /// <remarks>
        /// On success, stashes the materialised record in <see cref="MySqlUserRecordSaslCache"/> for
        /// <see cref="INntpSaslAccountAuthenticator.CompleteSaslAccountAsync"/>. The synchronous
        /// <see cref="INntpUserRecordStore.TryGetUser"/> contract does not accept cancellation.
        /// </remarks>
        bool ICramMd5CredentialStore.TryGetCramSecret(string username, out ReadOnlyMemory<byte> secret)
        {
            ArgumentException.ThrowIfNullOrEmpty(username);

            CramLookupStarted(_logger, username);

            try
            {
                MySqlUserRecord? record = _recordStore.TryGetUser(username);
                if (record is null)
                {
                    CramLookupUserNotFound(_logger, username);
                    secret = ReadOnlyMemory<byte>.Empty;
                    return false;
                }

                if (!record.IsEnabled)
                {
                    CramLookupAccountDisabled(_logger, username);
                    secret = ReadOnlyMemory<byte>.Empty;
                    return false;
                }

                if (!record.AllowAuthPlain)
                {
                    CramLookupNotPermitted(_logger, username);
                    secret = ReadOnlyMemory<byte>.Empty;
                    return false;
                }

                if (!EncodingUtilities.IsAscii(record.AccountPassword.AsSpan()))
                {
                    CramLookupNonAsciiPassword(_logger, username);
                    secret = ReadOnlyMemory<byte>.Empty;
                    return false;
                }

                byte[] bytes = EncodingUtilities.AsciiToBytes(record.AccountPassword);
                secret = new ReadOnlyMemory<byte>(bytes);
                MySqlUserRecordSaslCache.Set(record);
                CramLookupSucceeded(_logger, username);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AuthMySqlFailureReason reason = AuthMySqlFailureClassifier.Classify(ex);
                CramLookupFailed(_logger, ex, username, reason);
                throw new NntpCredentialStoreTransientException(
                    "MySQL CRAM-MD5 credential lookup failed due to a backend error.",
                    ex);
            }
        }
    }
}
