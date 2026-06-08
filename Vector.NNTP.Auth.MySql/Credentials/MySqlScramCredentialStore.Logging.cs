// <copyright file="MySqlScramCredentialStore.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// EventIds 320-326 (SASL SCRAM-SHA-256 credential-store lookup lifecycle).

using Microsoft.Extensions.Logging;
using Vector.NNTP.Auth.MySql.Configuration;

namespace Vector.NNTP.Auth.MySql.Credentials
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> helpers for <see cref="MySqlScramCredentialStore"/> SCRAM
    /// credential lookup diagnostics.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Role:</b> Logging partial for <see cref="MySqlScramCredentialStore"/>. Emits structured lookup lifecycle events from
    /// <see cref="Sockets.Authentication.IScramCredentialStore.TryGetScramCredential"/> while resolving SCRAM-SHA-256 material
    /// from <see cref="Records.INntpUserRecordStore"/>. Registered by
    /// <see cref="DependencyInjection.ServiceCollectionExtensions.AddNntpMySqlAuth"/>.
    /// </para>
    /// <para>
    /// <b>Logger category:</b> Callers pass <see cref="ILogger{TCategoryName}"/> for
    /// <see cref="MySqlScramCredentialStore"/> from the store instance. Methods are <see langword="static"/>
    /// <see langword="partial"/> with an explicit <see cref="ILogger"/> parameter so
    /// <see cref="LoggerMessageAttribute"/> source generation remains valid on the nested partial class.
    /// </para>
    /// <para><b>Event identifiers:</b></para>
    /// <list type="bullet">
    /// <item><description>EventId <c>320</c> — lookup started (<see cref="ScramLookupStarted"/>).</description></item>
    /// <item><description>EventId <c>321</c> — user not found (<see cref="ScramLookupUserNotFound"/>).</description></item>
    /// <item><description>EventId <c>322</c> — account disabled (<see cref="ScramLookupAccountDisabled"/>).</description></item>
    /// <item><description>EventId <c>323</c> — SCRAM not permitted (<see cref="ScramLookupNotPermitted"/>).</description></item>
    /// <item><description>EventId <c>324</c> — incomplete SCRAM provisioning (<see cref="ScramLookupMaterialMissing"/>).</description></item>
    /// <item><description>EventId <c>325</c> — lookup succeeded (<see cref="ScramLookupSucceeded"/>).</description></item>
    /// <item><description>EventId <c>326</c> — backend fault (<see cref="ScramLookupFailed"/>).</description></item>
    /// </list>
    /// <para>
    /// <b>Outcome pairing:</b> Rejection helpers (EventIds <c>321</c>–<c>324</c>) correspond to <see langword="false"/>
    /// returns without throwing — expected policy or provisioning outcomes, not Error-level faults. EventId <c>325</c> is
    /// emitted after <see cref="Records.MySqlUserRecordSaslCache.Set"/> stages the row for SASL account completion.
    /// <see cref="ScramLookupFailed"/> is followed by <see cref="Sockets.Authentication.NntpCredentialStoreTransientException"/>.
    /// </para>
    /// <para>
    /// <b>Privacy:</b> Logs include the plaintext username and iteration count on success; SCRAM salt, stored key, server key,
    /// and decrypted passwords are never written by these helpers.
    /// </para>
    /// <para><b>Threading:</b> Static helpers; invoked synchronously from the NNTP command loop during SASL exchange setup.</para>
    /// </remarks>
    internal sealed partial class MySqlScramCredentialStore
    {
        /// <summary>
        /// Logs the start of a SCRAM-SHA-256 credential lookup before the backing record store is queried.
        /// </summary>
        /// <param name="logger">
        /// Store category logger (typically <see cref="ILogger{TCategoryName}"/> for
        /// <see cref="MySqlScramCredentialStore"/>). Must not be <see langword="null"/>.
        /// </param>
        /// <param name="username">
        /// Plaintext NNTP username from the SASL client. Rendered as <c>'{Username}'</c> in the message template.
        /// </param>
        /// <remarks>
        /// <para>
        /// Source-generated at EventId <c>320</c>, <see cref="LogLevel.Debug"/>. Message template:
        /// <c>Auth.MySql SASL SCRAM-SHA-256 credential lookup started for user '{Username}'</c>.
        /// </para>
        /// <para>Emitted once per <c>TryGetScramCredential</c> invocation after username validation.</para>
        /// </remarks>
        [LoggerMessage(
            EventId = 320,
            Level = LogLevel.Debug,
            Message = "Auth.MySql SASL SCRAM-SHA-256 credential lookup started for user '{Username}'")]
        private static partial void ScramLookupStarted(ILogger logger, string username);

        /// <summary>
        /// Logs that the backing store returned no row for the requested username.
        /// </summary>
        /// <param name="logger">Store category logger. Must not be <see langword="null"/>.</param>
        /// <param name="username">
        /// Username with no <c>nntpusers</c> match. Rendered as <c>'{Username}'</c> in the message template.
        /// </param>
        /// <remarks>
        /// <para>
        /// Source-generated at EventId <c>321</c>, <see cref="LogLevel.Debug"/>. Message template:
        /// <c>Auth.MySql SASL SCRAM-SHA-256 credential lookup rejected for user '{Username}': user not found</c>.
        /// </para>
        /// <para>
        /// Invoked when <see cref="Records.INntpUserRecordStore.TryGetUser"/> returns <see langword="null"/>. The method
        /// returns <see langword="false"/> without throwing; this is an expected authentication outcome.
        /// </para>
        /// </remarks>
        [LoggerMessage(
            EventId = 321,
            Level = LogLevel.Debug,
            Message = "Auth.MySql SASL SCRAM-SHA-256 credential lookup rejected for user '{Username}': user not found")]
        private static partial void ScramLookupUserNotFound(ILogger logger, string username);

        /// <summary>
        /// Logs that the account exists but is disabled for authentication.
        /// </summary>
        /// <param name="logger">Store category logger. Must not be <see langword="null"/>.</param>
        /// <param name="username">
        /// Disabled account name. Rendered as <c>'{Username}'</c> in the message template.
        /// </param>
        /// <remarks>
        /// <para>
        /// Source-generated at EventId <c>322</c>, <see cref="LogLevel.Warning"/>. Message template:
        /// <c>Auth.MySql SASL SCRAM-SHA-256 credential lookup rejected for user '{Username}': account disabled</c>.
        /// </para>
        /// <para>
        /// Invoked when <see cref="Records.MySqlUserRecord.IsEnabled"/> is <see langword="false"/>. Returns
        /// <see langword="false"/> without throwing.
        /// </para>
        /// </remarks>
        [LoggerMessage(
            EventId = 322,
            Level = LogLevel.Warning,
            Message = "Auth.MySql SASL SCRAM-SHA-256 credential lookup rejected for user '{Username}': account disabled")]
        private static partial void ScramLookupAccountDisabled(ILogger logger, string username);

        /// <summary>
        /// Logs that SCRAM-SHA-256 is not permitted by account policy.
        /// </summary>
        /// <param name="logger">Store category logger. Must not be <see langword="null"/>.</param>
        /// <param name="username">
        /// Account name denied by policy. Rendered as <c>'{Username}'</c> in the message template.
        /// </param>
        /// <remarks>
        /// <para>
        /// Source-generated at EventId <c>323</c>, <see cref="LogLevel.Debug"/>. Message template:
        /// <c>Auth.MySql SASL SCRAM-SHA-256 credential lookup rejected for user '{Username}': SCRAM-SHA-256 not permitted</c>.
        /// </para>
        /// <para>
        /// Invoked when <see cref="Records.MySqlUserRecord.AllowAuthScram256"/> is <see langword="false"/> (database
        /// <c>allow_auth_scram256</c> not <c>Y</c>). Returns <see langword="false"/> without throwing.
        /// </para>
        /// </remarks>
        [LoggerMessage(
            EventId = 323,
            Level = LogLevel.Debug,
            Message = "Auth.MySql SASL SCRAM-SHA-256 credential lookup rejected for user '{Username}': SCRAM-SHA-256 not permitted")]
        private static partial void ScramLookupNotPermitted(ILogger logger, string username);

        /// <summary>
        /// Logs that SCRAM column material is missing or incomplete for an otherwise valid account.
        /// </summary>
        /// <param name="logger">Store category logger. Must not be <see langword="null"/>.</param>
        /// <param name="username">
        /// Account name with incomplete SCRAM provisioning. Rendered as <c>'{Username}'</c> in the message template.
        /// </param>
        /// <remarks>
        /// <para>
        /// Source-generated at EventId <c>324</c>, <see cref="LogLevel.Warning"/>. Message template:
        /// <c>Auth.MySql SASL SCRAM-SHA-256 credential lookup rejected for user '{Username}': SCRAM material missing</c>.
        /// </para>
        /// <para>
        /// Invoked when salt, iterations, stored key, or server key is empty or iterations is non-positive. Indicates a
        /// data provisioning problem rather than a client credential failure. Returns <see langword="false"/> without throwing.
        /// </para>
        /// </remarks>
        [LoggerMessage(
            EventId = 324,
            Level = LogLevel.Warning,
            Message = "Auth.MySql SASL SCRAM-SHA-256 credential lookup rejected for user '{Username}': SCRAM material missing")]
        private static partial void ScramLookupMaterialMissing(ILogger logger, string username);

        /// <summary>
        /// Logs that SCRAM stored-key material was returned and the user record was staged for SASL completion.
        /// </summary>
        /// <param name="logger">Store category logger. Must not be <see langword="null"/>.</param>
        /// <param name="username">
        /// Account name for which SCRAM material was resolved. Rendered as <c>'{Username}'</c> in the message template.
        /// </param>
        /// <param name="iterations">
        /// SCRAM PBKDF2 iteration count from the database row. Rendered as <c>Iterations=</c> in the message template.
        /// </param>
        /// <remarks>
        /// <para>
        /// Source-generated at EventId <c>325</c>, <see cref="LogLevel.Debug"/>. Message template:
        /// <c>Auth.MySql SASL SCRAM-SHA-256 credential lookup succeeded for user '{Username}' (Iterations={Iterations})</c>.
        /// </para>
        /// <para>
        /// Emitted after <see cref="Sockets.Authentication.ScramStoredCredential"/> is constructed and
        /// <see cref="Records.MySqlUserRecordSaslCache.Set"/> runs. Does not log key bytes.
        /// </para>
        /// </remarks>
        [LoggerMessage(
            EventId = 325,
            Level = LogLevel.Debug,
            Message = "Auth.MySql SASL SCRAM-SHA-256 credential lookup succeeded for user '{Username}' (Iterations={Iterations})")]
        private static partial void ScramLookupSucceeded(ILogger logger, string username, int iterations);

        /// <summary>
        /// Logs a backend fault during SCRAM credential lookup and attaches the underlying exception.
        /// </summary>
        /// <param name="logger">Store category logger. Must not be <see langword="null"/>.</param>
        /// <param name="ex">
        /// Exception from the record store or connector. Recorded on the log event by the source-generated helper.
        /// </param>
        /// <param name="username">
        /// Username for the lookup in flight. Rendered as <c>'{Username}'</c> in the message template.
        /// </param>
        /// <param name="failureReason">
        /// Stable reason from <see cref="AuthMySqlFailureClassifier.Classify"/>, rendered as <c>Reason=</c> in the message
        /// template (for example <see cref="AuthMySqlFailureReason.ConnectTimeout"/> or
        /// <see cref="AuthMySqlFailureReason.PoolPressure"/>).
        /// </param>
        /// <remarks>
        /// <para>
        /// Source-generated at EventId <c>326</c>, <see cref="LogLevel.Error"/>. Message template:
        /// <c>Auth.MySql SASL SCRAM-SHA-256 credential lookup failed for user '{Username}' due to backend error (Reason={FailureReason})</c>.
        /// </para>
        /// <para>
        /// Invoked from the <c>catch</c> block in <c>TryGetScramCredential</c> before throwing
        /// <see cref="Sockets.Authentication.NntpCredentialStoreTransientException"/>.
        /// <see cref="OperationCanceledException"/> is rethrown without logging here. Never swallows <paramref name="ex"/>.
        /// </para>
        /// </remarks>
        [LoggerMessage(
            EventId = 326,
            Level = LogLevel.Error,
            Message = "Auth.MySql SASL SCRAM-SHA-256 credential lookup failed for user '{Username}' due to backend error (Reason={FailureReason})")]
        private static partial void ScramLookupFailed(
            ILogger logger,
            Exception ex,
            string username,
            AuthMySqlFailureReason failureReason);
    }
}
