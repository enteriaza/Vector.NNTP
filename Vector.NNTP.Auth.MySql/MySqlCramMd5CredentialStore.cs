// <copyright file="MySqlCramMd5CredentialStore.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: ICramMd5CredentialStore implementation backed by the MySQL nntpusers table.

using Microsoft.Extensions.Logging;
using Vector.NNTP.Sockets.Authentication;
using Vector.NNTP.Utilities.Encoding;

namespace Vector.NNTP.Auth.MySql
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
    public sealed partial class MySqlCramMd5CredentialStore : ICramMd5CredentialStore
    {
        /// <summary>
        /// Backing user record store.
        /// </summary>
        private readonly INntpUserRecordStore _recordStore;

        /// <summary>
        /// Logger instance.
        /// </summary>
        private readonly ILogger<MySqlCramMd5CredentialStore> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="MySqlCramMd5CredentialStore"/> class.
        /// </summary>
        /// <param name="recordStore">Backing user record store.</param>
        /// <param name="logger">Logger instance.</param>
        internal MySqlCramMd5CredentialStore(
            INntpUserRecordStore recordStore,
            ILogger<MySqlCramMd5CredentialStore> logger)
        {
            _recordStore = recordStore ?? throw new ArgumentNullException(nameof(recordStore));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Tries to get a CRAM-MD5 secret for a username.
        /// </summary>
        /// <param name="username">Username supplied by the client.</param>
        /// <param name="secret">Shared secret derived from the stored password, when available.</param>
        /// <returns><see langword="true"/> when a secret was retrieved; otherwise <see langword="false"/>.</returns>
        public bool TryGetCramSecret(string username, out ReadOnlyMemory<byte> secret)
        {
            ArgumentException.ThrowIfNullOrEmpty(username);

            CramLookupStarted(_logger, username);

            try
            {
                // ICramMd5CredentialStore is synchronous; CancellationToken.None is required by that contract (see type remarks).
                MySqlUserRecord? record = _recordStore
                    .TryGetUserAsync(username, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
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
                    CramLookupNotPermitted(_logger, username);
                    secret = ReadOnlyMemory<byte>.Empty;
                    return false;
                }

                byte[] bytes = EncodingUtilities.AsciiToBytes(record.AccountPassword);
                secret = new ReadOnlyMemory<byte>(bytes);
                MySqlUserRecordSaslCache.Set(record);
                CramLookupSucceeded(_logger, username);
                return true;
            }
            catch (Exception ex)
            {
                CramLookupFailed(_logger, ex, username);
                secret = ReadOnlyMemory<byte>.Empty;
                return false;
            }
        }
    }
}
