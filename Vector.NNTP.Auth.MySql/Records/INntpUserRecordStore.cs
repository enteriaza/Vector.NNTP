// <copyright file="INntpUserRecordStore.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: abstraction for retrieving NNTP user records.

namespace Vector.NNTP.Auth.MySql.Records
{
    /// <summary>
    /// Abstraction for retrieving NNTP user records from a backing store.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Scope:</b> Assembly-internal abstraction; hosts integrate via the public credential-validator and SASL credential
    /// store contracts instead.
    /// </para>
    /// <para>
    /// <b>Testability:</b> The MySQL-backed implementation lives in this assembly, but tests can inject in-memory or
    /// fake implementations without requiring a running MySQL instance.
    /// </para>
    /// </remarks>
    internal interface INntpUserRecordStore
    {
        /// <summary>
        /// Attempts to retrieve a user record for the specified account name using synchronous database I/O.
        /// </summary>
        /// <param name="accountName">Account name to look up.</param>
        /// <returns>
        /// A <see cref="MySqlUserRecord"/> when the account exists; otherwise, <see langword="null"/>.
        /// </returns>
        /// <remarks>
        /// Used by synchronous SASL credential stores (<see cref="Sockets.Authentication.ICramMd5CredentialStore"/>,
        /// <see cref="Sockets.Authentication.IScramCredentialStore"/>). Exceptions propagate to the caller.
        /// </remarks>
        public MySqlUserRecord? TryGetUser(string accountName);

        /// <summary>
        /// Attempts to retrieve a user record for the specified account name using asynchronous database I/O.
        /// </summary>
        /// <param name="accountName">Account name to look up.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>
        /// A task producing a <see cref="MySqlUserRecord"/> when the account exists; otherwise, <see langword="null"/>.
        /// </returns>
        /// <remarks>
        /// Backend I/O and provider exceptions propagate to the caller after logging at the implementation boundary.
        /// </remarks>
        public Task<MySqlUserRecord?> TryGetUserAsync(string accountName, CancellationToken cancellationToken);
    }
}
