// <copyright file="INntpUserRecordStore.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: abstraction for retrieving NNTP user records.

namespace Vector.NNTP.Auth.MySql
{
    /// <summary>
    /// Abstraction for retrieving NNTP user records from a backing store.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Testability:</b> The MySQL-backed implementation lives in this assembly, but tests can inject in-memory or
    /// fake implementations without requiring a running MySQL instance.
    /// </para>
    /// </remarks>
    public interface INntpUserRecordStore
    {
        /// <summary>
        /// Attempts to retrieve a user record for the specified account name.
        /// </summary>
        /// <param name="accountName">Account name to look up.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>
        /// A task producing a <see cref="MySqlUserRecord"/> when the account exists; otherwise, <see langword="null"/>.
        /// </returns>
        public Task<MySqlUserRecord?> TryGetUserAsync(string accountName, CancellationToken cancellationToken);
    }
}
