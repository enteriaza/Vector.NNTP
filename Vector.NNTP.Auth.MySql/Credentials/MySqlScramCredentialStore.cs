// <copyright file="MySqlScramCredentialStore.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Vector.NNTP.Auth.MySql.Configuration;
using Vector.NNTP.Auth.MySql.Records;
using Vector.NNTP.Sockets.Authentication;

namespace Vector.NNTP.Auth.MySql.Credentials
{
    /// <summary>
    /// MySQL-backed <see cref="IScramCredentialStore"/> that supplies RFC 5802 SCRAM-SHA-256 stored keys from the
    /// <c>nntpusers</c> table.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Role:</b> Production SCRAM credential source registered by
    /// <see cref="DependencyInjection.ServiceCollectionExtensions.AddNntpMySqlAuth"/> as
    /// <see cref="IScramCredentialStore"/>, replacing the development stub from
    /// <c>Vector.NNTP.Sockets</c>. Consumed by <see cref="NntpAuthenticationService"/> and
    /// <see cref="Sockets.Transport.NntpCommandDispatcher"/> during SASL SCRAM-SHA-256 exchange setup.
    /// </para>
    /// <para>
    /// <b>Lookup path:</b> Accepts the plaintext NNTP username from the wire and calls
    /// <see cref="INntpUserRecordStore"/> (production: <see cref="CachingMySqlUserRecordStore"/> with optional
    /// username-only burst-cache hit). The inner store resolves rows where <c>account_name = MD5(username)</c>.
    /// </para>
    /// <para>
    /// <b>Policy gates:</b> Returns SCRAM material only when the account is enabled (<c>is_enabled = Y</c>),
    /// <see cref="MySqlUserRecord.AllowAuthScram256"/> is <see langword="true"/> (<c>allow_auth_scram256 = Y</c>), and
    /// salt, positive iterations, stored key, and server key are all provisioned. Other outcomes return
    /// <see langword="false"/> and log at EventIds <c>321</c>–<c>324</c> (see
    /// <c>MySqlScramCredentialStore.Logging.cs</c>).
    /// </para>
    /// <para>
    /// <b>I/O model:</b> <see cref="IScramCredentialStore"/> is synchronous with no cancellation token. Lookups run on the
    /// NNTP command-loop thread via <see cref="INntpUserRecordStore.TryGetUser"/>; connection and command timeouts from the
    /// MySQL connection string bound wait time.
    /// </para>
    /// <para>
    /// <b>SASL staging:</b> Successful lookups call <see cref="MySqlUserRecordSaslCache.Set"/> so
    /// <see cref="INntpSaslAccountAuthenticator.CompleteSaslAccountAsync"/> can finalize the account without a second
    /// database round-trip. Hosts must call <see cref="INntpSaslAccountAuthenticator.AbandonSaslExchange"/> on auth reset;
    /// completion clears the slot in a <c>finally</c> block inside the credential validator.
    /// </para>
    /// <para><b>Thread safety:</b> Singleton safe for concurrent sessions; each lookup uses independent connector state.</para>
    /// </remarks>
    internal sealed partial class MySqlScramCredentialStore : IScramCredentialStore
    {
        /// <summary>
        /// Decorated user-record store that performs MySQL I/O (and optional burst-cache read-through) on lookup.
        /// </summary>
        /// <remarks>
        /// In production this is <see cref="CachingMySqlUserRecordStore"/> wrapping <see cref="MySqlUserRecordStore"/>.
        /// Never null after construction.
        /// </remarks>
        private readonly INntpUserRecordStore _recordStore;

        /// <summary>
        /// Category logger for SCRAM lookup lifecycle events (EventIds <c>320</c>–<c>326</c>).
        /// </summary>
        /// <remarks>Passed to source-generated helpers in the logging partial.</remarks>
        private readonly ILogger<MySqlScramCredentialStore> _logger;

        /// <summary>
        /// Creates a SCRAM credential store backed by the supplied user-record store.
        /// </summary>
        /// <param name="recordStore">
        /// <see cref="INntpUserRecordStore"/> implementation from DI. Must not be <see langword="null"/>.
        /// </param>
        /// <param name="logger">
        /// Logger for <see cref="MySqlScramCredentialStore"/>. Must not be <see langword="null"/>.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="recordStore"/> or <paramref name="logger"/> is <see langword="null"/>.
        /// </exception>
        /// <remarks>
        /// Registered as a singleton in <see cref="DependencyInjection.ServiceCollectionExtensions.AddNntpMySqlAuth"/>.
        /// Does not open a connection at construction time.
        /// </remarks>
        internal MySqlScramCredentialStore(
            INntpUserRecordStore recordStore,
            ILogger<MySqlScramCredentialStore> logger)
        {
            _recordStore = recordStore ?? throw new ArgumentNullException(nameof(recordStore));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Resolves SCRAM-SHA-256 stored-key material for a username from the MySQL user store.
        /// </summary>
        /// <param name="username">
        /// Plaintext NNTP account name supplied by the SASL client. Must not be <see langword="null"/> or empty.
        /// </param>
        /// <param name="credential">
        /// When this method returns <see langword="true"/>, a <see cref="ScramStoredCredential"/> built from the row's SCRAM
        /// columns. When this method returns <see langword="false"/>, <see langword="null"/>.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when SCRAM material was returned; <see langword="false"/> for not-found, disabled account,
        /// policy denial, or incomplete SCRAM provisioning without throwing.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="username"/> is <see langword="null"/> or empty.
        /// </exception>
        /// <exception cref="NntpCredentialStoreTransientException">
        /// Thrown when the backing record store fails due to a database or transport error after EventId <c>326</c> is
        /// logged.
        /// </exception>
        /// <remarks>
        /// <para><b>Flow:</b></para>
        /// <list type="number">
        /// <item><description>Log started (EventId <c>320</c>).</description></item>
        /// <item><description>Load row via <see cref="INntpUserRecordStore.TryGetUser"/>.</description></item>
        /// <item><description>Reject with Debug/Warning logs when row missing, disabled, not SCRAM-permitted, or SCRAM columns incomplete.</description></item>
        /// <item>
        /// <description>
        /// On success: build <see cref="ScramStoredCredential"/>, <see cref="MySqlUserRecordSaslCache.Set"/>, log succeeded
        /// (EventId <c>325</c>), return <see langword="true"/>.
        /// </description>
        /// </item>
        /// </list>
        /// <para>
        /// Implements <see cref="IScramCredentialStore.TryGetScramCredential"/>. Rejection paths are expected authentication
        /// outcomes (wire layer maps <see langword="false"/> to SASL failure). Backend faults are classified by
        /// <see cref="AuthMySqlFailureClassifier"/> before wrapping in <see cref="NntpCredentialStoreTransientException"/>.
        /// <see cref="OperationCanceledException"/> is rethrown without logging or wrapping.
        /// </para>
        /// <para>
        /// Does not populate the post-success burst cache (<see cref="MySqlUserRecordCache"/>); that occurs only after
        /// cryptographic verification in <see cref="MySqlNntpCredentialValidator"/>.
        /// </para>
        /// </remarks>
        bool IScramCredentialStore.TryGetScramCredential(string username, [NotNullWhen(true)] out ScramStoredCredential? credential)
        {
            ArgumentException.ThrowIfNullOrEmpty(username);

            ScramLookupStarted(_logger, username);

            try
            {
                MySqlUserRecord? record = _recordStore.TryGetUser(username);
                if (record is null)
                {
                    ScramLookupUserNotFound(_logger, username);
                    credential = null;
                    return false;
                }

                if (!record.IsEnabled)
                {
                    ScramLookupAccountDisabled(_logger, username);
                    credential = null;
                    return false;
                }

                if (!record.AllowAuthScram256)
                {
                    ScramLookupNotPermitted(_logger, username);
                    credential = null;
                    return false;
                }

                if (record.ScramSalt.IsEmpty ||
                    record.ScramIterations <= 0 ||
                    record.ScramStoredKey.IsEmpty ||
                    record.ScramServerKey.IsEmpty)
                {
                    ScramLookupMaterialMissing(_logger, username);
                    credential = null;
                    return false;
                }

                credential = new ScramStoredCredential(
                    record.ScramSalt,
                    record.ScramIterations,
                    record.ScramStoredKey,
                    record.ScramServerKey);
                MySqlUserRecordSaslCache.Set(record);
                ScramLookupSucceeded(_logger, username, record.ScramIterations);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AuthMySqlFailureReason reason = AuthMySqlFailureClassifier.Classify(ex);
                ScramLookupFailed(_logger, ex, username, reason);
                throw new NntpCredentialStoreTransientException(
                    "MySQL SCRAM credential lookup failed due to a backend error.",
                    ex);
            }
        }
    }
}
