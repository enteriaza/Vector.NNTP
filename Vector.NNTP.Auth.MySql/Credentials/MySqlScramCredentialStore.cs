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
    /// MySQL-backed implementation of <see cref="IScramCredentialStore"/> that supplies SCRAM-SHA-256 stored keys
    /// provisioned in the <c>nntpusers</c> table.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Lookup key:</b> The backing store queries rows by <c>account_name = MD5(username)</c>. This type accepts the
    /// plaintext NNTP username and delegates the lookup to <see cref="INntpUserRecordStore"/>.
    /// </para>
    /// <para>
    /// <b>Mechanism policy:</b> SCRAM material is only returned when the account is enabled and <c>allow_auth_scram256</c>
    /// is <c>Y</c> and all SCRAM columns are present.
    /// </para>
    /// <para>
    /// <b>I/O model:</b> The synchronous <see cref="IScramCredentialStore"/> contract is satisfied via
    /// <see cref="INntpUserRecordStore.TryGetUser"/>, which performs synchronous MySqlConnector I/O on the connection
    /// command-loop context. Authentication is control-plane traffic; ADO.NET connection and command timeouts bound wait time.
    /// </para>
    /// <para>
    /// <b>Cache contract:</b> A successful lookup calls <see cref="MySqlUserRecordSaslCache.Set"/>; hosts must call
    /// <see cref="INntpSaslAccountAuthenticator.AbandonSaslExchange"/> on auth reset and
    /// <see cref="INntpSaslAccountAuthenticator.CompleteSaslAccountAsync"/> on success (which clears via <c>finally</c>).
    /// </para>
    /// </remarks>
    public sealed partial class MySqlScramCredentialStore : IScramCredentialStore
    {
        /// <summary>
        /// Backing user record store.
        /// </summary>
        private readonly INntpUserRecordStore _recordStore;

        /// <summary>
        /// Logger instance.
        /// </summary>
        private readonly ILogger<MySqlScramCredentialStore> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="MySqlScramCredentialStore"/> class.
        /// </summary>
        /// <param name="recordStore">Backing user record store.</param>
        /// <param name="logger">Logger instance.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="recordStore"/> or <paramref name="logger"/> is null.</exception>
        internal MySqlScramCredentialStore(
            INntpUserRecordStore recordStore,
            ILogger<MySqlScramCredentialStore> logger)
        {
            _recordStore = recordStore ?? throw new ArgumentNullException(nameof(recordStore));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Tries to get a SCRAM credential for a username.
        /// </summary>
        /// <param name="username">Username to lookup.</param>
        /// <param name="credential">The resulting SCRAM credential, or <see langword="null"/> if the lookup failed.</param>
        /// <returns><see langword="true"/> if a credential was found and returned; <see langword="false"/> otherwise.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="username"/> is null or empty.</exception>
        /// <exception cref="NntpCredentialStoreTransientException">Thrown when the backing store fails due to a backend error.</exception>
        /// <remarks>
        /// <see cref="OperationCanceledException"/> propagates when the backing lookup is cancelled.
        /// </remarks>
        public bool TryGetScramCredential(string username, [NotNullWhen(true)] out ScramStoredCredential? credential)
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
