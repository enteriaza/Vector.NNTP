// <copyright file="INntpUserRecordStore.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: abstraction for retrieving NNTP user records.

namespace Vector.NNTP.Auth.MySql.Records
{
    /// <summary>
    /// Contract for loading <see cref="MySqlUserRecord"/> snapshots from NNTP account persistence.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Role:</b> Assembly-internal seam between authentication components and the MySQL <c>nntpusers</c> table.
    /// Production hosts resolve this interface from
    /// <see cref="DependencyInjection.ServiceCollectionExtensions.AddNntpMySqlAuth"/> as
    /// <see cref="CachingMySqlUserRecordStore"/> decorating <see cref="MySqlUserRecordStore"/>. Host integration uses
    /// <see cref="Sockets.Authentication.INntpCredentialValidator"/>,
    /// <see cref="Sockets.Authentication.IScramCredentialStore"/>, and
    /// <see cref="Sockets.Authentication.ICramMd5CredentialStore"/> instead of this type directly.
    /// </para>
    /// <para><b>Consumers:</b></para>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <see cref="Credentials.MySqlNntpCredentialValidator"/> — <see cref="TryGetUserAsync"/> during SASL account
    /// completion when <see cref="MySqlUserRecordSaslCache"/> has no staged record.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <see cref="Credentials.MySqlScramCredentialStore"/> and <see cref="Credentials.MySqlCramMd5CredentialStore"/> —
    /// <see cref="TryGetUser"/> on protocol threads (the synchronous contract does not accept cancellation).
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// <b>Lookup semantics:</b> The account name argument is the plaintext NNTP username presented on the wire. A
    /// <see langword="null"/> return means no matching row was found; it is not an authentication failure by itself.
    /// Disabled accounts (<see cref="MySqlUserRecord.IsEnabled"/> <see langword="false"/>) still materialise when the row
    /// exists — callers enforce enablement and mechanism policy after lookup.
    /// </para>
    /// <para>
    /// <b>Errors:</b> Implementations validate the account name and throw <see cref="ArgumentException"/> for null or empty
    /// input. Database, network, and unexpected mapper faults are logged at the implementation boundary and rethrown; they
    /// are not converted into <see langword="null"/>. Credential stores typically wrap backend faults in
    /// <see cref="Sockets.Authentication.NntpCredentialStoreTransientException"/>.
    /// </para>
    /// <para>
    /// <b>Testability:</b> Unit and protocol tests register in-memory fakes implementing this interface so credential
    /// validation can run without a live MySQL instance.
    /// </para>
    /// </remarks>
    internal interface INntpUserRecordStore
    {
        /// <summary>
        /// Loads a user record synchronously when the backing store can be queried without async I/O.
        /// </summary>
        /// <param name="accountName">
        /// Plaintext NNTP account name to resolve against persistence. Must not be <see langword="null"/> or empty.
        /// </param>
        /// <returns>
        /// A materialised <see cref="MySqlUserRecord"/> when a row exists for the account; otherwise
        /// <see langword="null"/>.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="accountName"/> is <see langword="null"/> or empty.
        /// </exception>
        /// <remarks>
        /// <para>
        /// <b>Callers:</b> <see cref="Credentials.MySqlCramMd5CredentialStore"/> and
        /// <see cref="Credentials.MySqlScramCredentialStore"/> invoke this method during SASL secret retrieval. Successful
        /// lookups may stash the record in <see cref="MySqlUserRecordSaslCache"/> for subsequent account completion in the
        /// validator.
        /// </para>
        /// <para>
        /// <b>Production path:</b> <see cref="CachingMySqlUserRecordStore"/> consults the username-only burst cache
        /// (<see cref="MySqlUserRecordCache.UsernameOnlyFingerprint"/>) before delegating cache misses to
        /// <see cref="MySqlUserRecordStore"/>. Password-fingerprint cache hits are not evaluated at this layer; they are
        /// handled inside <see cref="Credentials.MySqlNntpCredentialValidator"/>.
        /// </para>
        /// <para>
        /// <b>Backend faults:</b> <see cref="MySqlUserRecordStore"/> logs, records <c>transient_failure</c> metrics, and
        /// rethrows MySQL and transport exceptions. This method does not honour cancellation; long-running synchronous I/O
        /// can block the calling protocol thread.
        /// </para>
        /// </remarks>
        public MySqlUserRecord? TryGetUser(string accountName);

        /// <summary>
        /// Loads a user record asynchronously with cancellation support during connection and reader I/O.
        /// </summary>
        /// <param name="accountName">
        /// Plaintext NNTP account name to resolve against persistence. Must not be <see langword="null"/> or empty.
        /// </param>
        /// <param name="cancellationToken">
        /// Token signalled when the hosting session or authentication operation is aborted. Honoured during async connector
        /// I/O in <see cref="MySqlUserRecordStore"/>.
        /// </param>
        /// <returns>
        /// A task that completes with a <see cref="MySqlUserRecord"/> when a row exists, or <see langword="null"/> when no
        /// row matches. The task faults when backend I/O fails after implementation logging.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="accountName"/> is <see langword="null"/> or empty.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        /// Thrown when <paramref name="cancellationToken"/> is signalled during async lookup in
        /// <see cref="MySqlUserRecordStore"/>.
        /// </exception>
        /// <remarks>
        /// <para>
        /// <b>Callers:</b> <see cref="Credentials.MySqlNntpCredentialValidator"/> uses this method in
        /// <c>FinalizeAuthenticationAsync</c> after <see cref="MySqlUserRecordSaslCache.TryTake"/> misses, so SASL account
        /// completion can still resolve the row without blocking a thread pool worker on synchronous ADO.NET calls.
        /// </para>
        /// <para>
        /// <b>Production path:</b> <see cref="CachingMySqlUserRecordStore"/> applies the same username-only cache read as
        /// <see cref="TryGetUser"/> before awaiting the inner store on miss. Cache hits avoid database I/O entirely.
        /// </para>
        /// <para>
        /// <b>Not-found vs fault:</b> A completed task with a <see langword="null"/> result means the account row is absent.
        /// Faulted tasks indicate environment or backend errors and should be handled separately from invalid credentials.
        /// </para>
        /// </remarks>
        public Task<MySqlUserRecord?> TryGetUserAsync(string accountName, CancellationToken cancellationToken);
    }
}
