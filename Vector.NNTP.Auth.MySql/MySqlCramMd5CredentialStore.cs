// <copyright file="MySqlCramMd5CredentialStore.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: ICramMd5CredentialStore implementation backed by the MySQL nntpusers table.

using System.Text;
using Microsoft.Extensions.Logging;
using Vector.NNTP.Sockets.Authentication;

namespace Vector.NNTP.Auth.MySql
{
    /// <summary>
    /// MySQL-backed implementation of <see cref="ICramMd5CredentialStore"/> that reuses the NNTP user record query.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Secret material:</b> The decrypted <c>account_pass</c> column is converted to UTF-8 bytes and used as the
    /// CRAM-MD5 shared secret. This matches the password comparison used for <see cref="INntpCredentialValidator"/>.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// Initializes a new instance of the <see cref="MySqlCramMd5CredentialStore"/> class.
    /// </remarks>
    /// <param name="recordStore">Backing user record store.</param>
    /// <param name="logger">Logger instance.</param>
    public sealed class MySqlCramMd5CredentialStore(
        INntpUserRecordStore recordStore,
        ILogger<MySqlCramMd5CredentialStore> logger) : ICramMd5CredentialStore
    {
        /// <summary>
        /// Backing user record store.
        /// </summary>
        private readonly INntpUserRecordStore _recordStore = recordStore ?? throw new ArgumentNullException(nameof(recordStore));

        /// <summary>
        /// Logger instance.
        /// </summary>
        private readonly ILogger<MySqlCramMd5CredentialStore> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        /// <summary>
        /// Tries to get a CRAM-MD5 secret for a username.
        /// </summary>
        /// <param name="username">Username supplied by the client.</param>
        /// <param name="secret">Shared secret derived from the stored password, when available.</param>
        /// <returns><see langword="true"/> when a secret was retrieved; otherwise <see langword="false"/>.</returns>
        public bool TryGetCramSecret(string username, out ReadOnlyMemory<byte> secret)
        {
            ArgumentException.ThrowIfNullOrEmpty(username);

            MySqlCramMd5CredentialStoreLog.CramLookupStarted(this._logger, username);

            // CRAM-MD5 lookups are expected to be relatively rare. We perform a synchronous wait on the asynchronous
            // store API here; higher-level SASL negotiation already runs on the thread-pool and is not on a hot path.
            try
            {
                using CancellationTokenSource cancellationSource = new();
                Task<MySqlUserRecord?> task = this._recordStore.TryGetUserAsync(username, cancellationSource.Token);
                MySqlUserRecord? record = task.GetAwaiter().GetResult();
                if (record is null)
                {
                    MySqlCramMd5CredentialStoreLog.CramLookupUserNotFound(this._logger, username);
                    secret = ReadOnlyMemory<byte>.Empty;
                    return false;
                }

                if (!record.IsEnabled)
                {
                    MySqlCramMd5CredentialStoreLog.CramLookupAccountDisabled(this._logger, username);
                    secret = ReadOnlyMemory<byte>.Empty;
                    return false;
                }

                if (!record.AllowAuthPlain)
                {
                    MySqlCramMd5CredentialStoreLog.CramLookupNotPermitted(this._logger, username);
                    secret = ReadOnlyMemory<byte>.Empty;
                    return false;
                }

                byte[] bytes = Encoding.UTF8.GetBytes(record.AccountPassword);
                secret = new ReadOnlyMemory<byte>(bytes);
                MySqlCramMd5CredentialStoreLog.CramLookupSucceeded(this._logger, username);
                return true;
            }
            catch (Exception ex)
            {
                MySqlCramMd5CredentialStoreLog.CramLookupFailed(this._logger, ex, username);
                secret = ReadOnlyMemory<byte>.Empty;
                return false;
            }
        }
    }
}
