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
    /// The SCRAM contract (<see cref="IScramCredentialStore"/>) is synchronous, so this implementation performs a
    /// synchronous wait when it must consult the database. This is acceptable for the current socket-host design, but if
    /// authentication throughput becomes a bottleneck, consider introducing an async SCRAM lookup contract end-to-end.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// Initializes a new instance of the <see cref="MySqlScramCredentialStore"/> class.
    /// </remarks>
    /// <param name="recordStore">Backing user record store.</param>
    /// <param name="logger">Logger instance.</param>
    public sealed partial class MySqlScramCredentialStore(INntpUserRecordStore recordStore, ILogger<MySqlScramCredentialStore> logger) : IScramCredentialStore
    {
        /// <summary>
        /// Backing user record store.
        /// </summary>
        private readonly INntpUserRecordStore _recordStore = recordStore ?? throw new ArgumentNullException(nameof(recordStore));

        /// <summary>
        /// Logger instance.
        /// </summary>
        private readonly ILogger<MySqlScramCredentialStore> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

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
