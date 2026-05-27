// <copyright file="MySqlCramMd5CredentialStore.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: ICramMd5CredentialStore implementation backed by the MySQL nntpusers table.

using System.Text;
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
    public sealed class MySqlCramMd5CredentialStore(INntpUserRecordStore recordStore) : ICramMd5CredentialStore
    {
        private readonly INntpUserRecordStore _recordStore = recordStore ?? throw new ArgumentNullException(nameof(recordStore));

        /// <inheritdoc />
        public bool TryGetCramSecret(string username, out ReadOnlyMemory<byte> secret)
        {
            // CRAM-MD5 lookups are expected to be relatively rare. We perform a synchronous wait on the asynchronous
            // store API here; higher-level SASL negotiation already runs on the thread-pool and is not on a hot path.
            using CancellationTokenSource cancellationSource = new();
            Task<MySqlUserRecord?> task = _recordStore.TryGetUserAsync(username, cancellationSource.Token);
            MySqlUserRecord? record = task.GetAwaiter().GetResult();
            if (record is null || !record.IsEnabled)
            {
                secret = ReadOnlyMemory<byte>.Empty;
                return false;
            }

            byte[] bytes = Encoding.UTF8.GetBytes(record.AccountPassword);
            secret = new ReadOnlyMemory<byte>(bytes);
            return true;
        }
    }
}
