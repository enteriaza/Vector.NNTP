// <copyright file="MySqlCramMd5CredentialStore.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: ICramMd5CredentialStore implementation backed by the MySQL nntpusers table.

using Microsoft.Extensions.Logging;
using Vector.NNTP.Auth.MySql.Configuration;
using Vector.NNTP.Auth.MySql.Records;
using Vector.NNTP.Sockets.Authentication;
using Vector.NNTP.Utilities.Encoding;

namespace Vector.NNTP.Auth.MySql.Credentials
{
    /// <summary>
    /// MySQL-backed <see cref="ICramMd5CredentialStore"/> that supplies RFC 2195 CRAM-MD5 shared secrets from the
    /// <c>nntpusers</c> table.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Role:</b> Production CRAM-MD5 credential source registered by
    /// <see cref="DependencyInjection.ServiceCollectionExtensions.AddNntpMySqlAuth"/> as
    /// <see cref="ICramMd5CredentialStore"/>, replacing the development stub from
    /// <c>Vector.NNTP.Sockets</c>. Consumed by <see cref="NntpAuthenticationService"/> and
    /// <see cref="Sockets.Transport.NntpCommandDispatcher"/> during SASL CRAM-MD5 exchange setup.
    /// </para>
    /// <para>
    /// <b>Lookup path:</b> Accepts the plaintext NNTP username from the wire and calls
    /// <see cref="INntpUserRecordStore"/> (production: <see cref="CachingMySqlUserRecordStore"/> with optional
    /// username-only burst-cache hit). The inner store resolves rows where <c>account_name = MD5(username)</c>.
    /// </para>
    /// <para>
    /// <b>Secret material:</b> On success, the decrypted <c>account_pass</c> column (AES in
    /// <see cref="MySqlUserRecordStore"/>) is validated as US-ASCII and encoded to bytes for use as the CRAM-MD5 HMAC
    /// key. Non-ASCII passwords are rejected without throwing.
    /// </para>
    /// <para>
    /// <b>Policy gates:</b> Returns secret material only when the account is enabled (<c>is_enabled = Y</c>),
    /// <see cref="MySqlUserRecord.AllowAuthPlain"/> is <see langword="true"/> (<c>allow_auth_plain = Y</c>), and the
    /// password is US-ASCII. Other outcomes return <see langword="false"/> and log at EventIds <c>301</c>–<c>302</c>
    /// and <c>305</c>–<c>306</c> (see <c>MySqlCramMd5CredentialStore.Logging.cs</c>).
    /// </para>
    /// <para>
    /// <b>I/O model:</b> <see cref="ICramMd5CredentialStore"/> is synchronous with no cancellation token. Lookups run on the
    /// NNTP command-loop thread via <see cref="INntpUserRecordStore.TryGetUser"/>; connection and command timeouts from the
    /// MySQL connection string bound wait time.
    /// </para>
    /// <para>
    /// <b>SASL staging:</b> Successful lookups call <see cref="MySqlUserRecordSaslCache.Set"/> so
    /// <see cref="INntpSaslAccountAuthenticator.CompleteSaslAccountAsync"/> can finalize the account without a second
    /// database round-trip. Hosts must call <see cref="INntpSaslAccountAuthenticator.AbandonSaslExchange"/> on auth reset;
    /// completion clears the slot in a <c>finally</c> block inside the credential validator.
    /// </para>
    /// <para>
    /// Does not populate the post-success burst cache (<see cref="MySqlUserRecordCache"/>); that occurs only after
    /// cryptographic verification in <see cref="MySqlNntpCredentialValidator"/>.
    /// </para>
    /// <para><b>Thread safety:</b> Singleton safe for concurrent sessions; each lookup uses independent connector state.</para>
    /// </remarks>
    internal sealed partial class MySqlCramMd5CredentialStore : ICramMd5CredentialStore
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
        /// Category logger for CRAM-MD5 lookup lifecycle events (EventIds <c>300</c>–<c>306</c>).
        /// </summary>
        /// <remarks>Passed to source-generated helpers in the logging partial.</remarks>
        private readonly ILogger<MySqlCramMd5CredentialStore> _logger;

        /// <summary>
        /// Creates a CRAM-MD5 credential store backed by the supplied user-record store.
        /// </summary>
        /// <param name="recordStore">
        /// <see cref="INntpUserRecordStore"/> implementation from DI. Must not be <see langword="null"/>.
        /// </param>
        /// <param name="logger">
        /// Logger for <see cref="MySqlCramMd5CredentialStore"/>. Must not be <see langword="null"/>.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="recordStore"/> or <paramref name="logger"/> is <see langword="null"/>.
        /// </exception>
        /// <remarks>
        /// Registered as a singleton in <see cref="DependencyInjection.ServiceCollectionExtensions.AddNntpMySqlAuth"/>.
        /// Does not open a connection at construction time.
        /// </remarks>
        internal MySqlCramMd5CredentialStore(
            INntpUserRecordStore recordStore,
            ILogger<MySqlCramMd5CredentialStore> logger)
        {
            _recordStore = recordStore ?? throw new ArgumentNullException(nameof(recordStore));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Resolves the CRAM-MD5 shared secret for a username from the MySQL user store.
        /// </summary>
        /// <param name="username">
        /// Plaintext NNTP account name supplied by the SASL client. Must not be <see langword="null"/> or empty.
        /// </param>
        /// <param name="secret">
        /// When this method returns <see langword="true"/>, US-ASCII password bytes used as the CRAM-MD5 HMAC key. When this
        /// method returns <see langword="false"/>, <see cref="ReadOnlyMemory{T}.Empty"/>.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when secret material was returned; <see langword="false"/> for not-found, disabled account,
        /// policy denial, or non-ASCII password without throwing.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="username"/> is <see langword="null"/> or empty.
        /// </exception>
        /// <exception cref="NntpCredentialStoreTransientException">
        /// Thrown when the backing record store fails due to a database or transport error after EventId <c>304</c> is
        /// logged.
        /// </exception>
        /// <remarks>
        /// <para><b>Flow:</b></para>
        /// <list type="number">
        /// <item><description>Log started (EventId <c>300</c>).</description></item>
        /// <item><description>Load row via <see cref="INntpUserRecordStore.TryGetUser"/>.</description></item>
        /// <item>
        /// <description>
        /// Reject with Debug/Warning logs when row missing (EventId <c>301</c>), disabled (EventId <c>302</c>),
        /// <see cref="MySqlUserRecord.AllowAuthPlain"/> false (EventId <c>305</c>), or password not US-ASCII (EventId
        /// <c>306</c>).
        /// </description>
        /// </item>
        /// <item>
        /// <description>
        /// On success: encode <see cref="MySqlUserRecord.AccountPassword"/> to bytes, <see cref="MySqlUserRecordSaslCache.Set"/>,
        /// log succeeded (EventId <c>303</c>), return <see langword="true"/>.
        /// </description>
        /// </item>
        /// </list>
        /// <para>
        /// Implements <see cref="ICramMd5CredentialStore.TryGetCramSecret"/>. Rejection paths are expected authentication
        /// outcomes (wire layer maps <see langword="false"/> to SASL failure). Backend faults are classified by
        /// <see cref="AuthMySqlFailureClassifier"/> before wrapping in <see cref="NntpCredentialStoreTransientException"/>.
        /// <see cref="OperationCanceledException"/> is rethrown without logging or wrapping.
        /// </para>
        /// <para>
        /// The synchronous <see cref="INntpUserRecordStore"/> contract exposes no cancellation token; a client disconnect
        /// during lookup cannot abort the in-flight MySQL command.
        /// </para>
        /// </remarks>
        bool ICramMd5CredentialStore.TryGetCramSecret(string username, out ReadOnlyMemory<byte> secret)
        {
            ArgumentException.ThrowIfNullOrEmpty(username);

            CramLookupStarted(_logger, username);

            try
            {
                MySqlUserRecord? record = _recordStore.TryGetUser(username);
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
                    CramLookupNonAsciiPassword(_logger, username);
                    secret = ReadOnlyMemory<byte>.Empty;
                    return false;
                }

                byte[] bytes = EncodingUtilities.AsciiToBytes(record.AccountPassword);
                secret = new ReadOnlyMemory<byte>(bytes);
                MySqlUserRecordSaslCache.Set(record);
                CramLookupSucceeded(_logger, username);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AuthMySqlFailureReason reason = AuthMySqlFailureClassifier.Classify(ex);
                CramLookupFailed(_logger, ex, username, reason);
                throw new NntpCredentialStoreTransientException(
                    "MySQL CRAM-MD5 credential lookup failed due to a backend error.",
                    ex);
            }
        }
    }
}
