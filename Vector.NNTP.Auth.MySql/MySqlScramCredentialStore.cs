// <copyright file="MySqlScramCredentialStore.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Vector.NNTP.Sockets.Authentication;

namespace Vector.NNTP.Auth.MySql
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
    /// <b>I/O model:</b> The underlying <see cref="INntpUserRecordStore"/> is asynchronous because it performs database I/O.
    /// The SCRAM contract (<see cref="IScramCredentialStore"/>) is synchronous, so this implementation blocks on
    /// <c>TryGetUserAsync</c> during credential lookup on the connection's command-loop context. Authentication volume is
    /// negligible relative to article traffic on an NNTP server, so this is acceptable today; if SASL ever becomes hot-path,
    /// introduce an async lookup contract (for example <c>ValueTask&lt;ScramStoredCredential?&gt;</c>) end-to-end instead of
    /// deepening synchronous waits here.
    /// </para>
    /// <para>
    /// <b>Cancellation:</b> <see cref="IScramCredentialStore"/> exposes no token, so lookups use
    /// <see cref="CancellationToken.None"/> and cannot be aborted when the client disconnects mid-query.
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
        public bool TryGetScramCredential(string username, [NotNullWhen(true)] out ScramStoredCredential? credential)
        {
            ArgumentException.ThrowIfNullOrEmpty(username);

            ScramLookupStarted(_logger, username);

            try
            {
                // IScramCredentialStore is synchronous; CancellationToken.None is required by that contract (see type remarks).
                MySqlUserRecord? record = _recordStore
                    .TryGetUserAsync(username, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
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
            catch (Exception ex)
            {
                ScramLookupFailed(_logger, ex, username);
                credential = null;
                return false;
            }
        }
    }
}
