// <copyright file="MySqlCramMd5CredentialStore.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// EventIds 300-306 (SASL CRAM-MD5 credential-store lookup lifecycle).

using Microsoft.Extensions.Logging;
using Vector.NNTP.Auth.MySql.Configuration;

namespace Vector.NNTP.Auth.MySql.Credentials
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> helpers for <see cref="MySqlCramMd5CredentialStore"/> CRAM-MD5
    /// secret lookup diagnostics.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Role:</b> Logging partial for <see cref="MySqlCramMd5CredentialStore"/>. Emits structured lookup lifecycle events from
    /// <see cref="Sockets.Authentication.ICramMd5CredentialStore.TryGetCramSecret"/> while resolving the shared HMAC secret
    /// from <see cref="Records.INntpUserRecordStore"/>. Registered by
    /// <see cref="DependencyInjection.ServiceCollectionExtensions.AddNntpMySqlAuth"/>.
    /// </para>
    /// <para>
    /// <b>Logger category:</b> Callers pass <see cref="ILogger{TCategoryName}"/> for
    /// <see cref="MySqlCramMd5CredentialStore"/> from the store instance. Methods are <see langword="static"/>
    /// <see langword="partial"/> with an explicit <see cref="ILogger"/> parameter so
    /// <see cref="LoggerMessageAttribute"/> source generation remains valid on the nested partial class.
    /// </para>
    /// <para><b>Event identifiers:</b></para>
    /// <list type="bullet">
    /// <item><description>EventId <c>300</c> — lookup started (<see cref="CramLookupStarted"/>).</description></item>
    /// <item><description>EventId <c>301</c> — user not found (<see cref="CramLookupUserNotFound"/>).</description></item>
    /// <item><description>EventId <c>302</c> — account disabled (<see cref="CramLookupAccountDisabled"/>).</description></item>
    /// <item><description>EventId <c>303</c> — lookup succeeded (<see cref="CramLookupSucceeded"/>).</description></item>
    /// <item><description>EventId <c>304</c> — backend fault (<see cref="CramLookupFailed"/>).</description></item>
    /// <item><description>EventId <c>305</c> — password mechanisms not permitted (<see cref="CramLookupNotPermitted"/>).</description></item>
    /// <item><description>EventId <c>306</c> — non-ASCII password (<see cref="CramLookupNonAsciiPassword"/>).</description></item>
    /// </list>
    /// <para>
    /// <b>Outcome pairing:</b> Rejection helpers (EventIds <c>301</c>, <c>302</c>, <c>305</c>, <c>306</c>) correspond to
    /// <see langword="false"/> returns without throwing. EventId <c>303</c> is emitted after
    /// <see cref="Records.MySqlUserRecordSaslCache.Set"/> stages the row for SASL account completion.
    /// <see cref="CramLookupFailed"/> is followed by <see cref="Sockets.Authentication.NntpCredentialStoreTransientException"/>.
    /// </para>
    /// <para>
    /// <b>Privacy:</b> Logs include the plaintext username only; decrypted password bytes and HMAC secrets are never written by
    /// these helpers.
    /// </para>
    /// <para><b>Threading:</b> Static helpers; invoked synchronously from the NNTP command loop during SASL CRAM-MD5 setup.</para>
    /// </remarks>
    internal sealed partial class MySqlCramMd5CredentialStore
    {
        /// <summary>
        /// Logs the start of a CRAM-MD5 shared-secret lookup before the backing record store is queried.
        /// </summary>
        /// <param name="logger">
        /// Store category logger (typically <see cref="ILogger{TCategoryName}"/> for
        /// <see cref="MySqlCramMd5CredentialStore"/>). Must not be <see langword="null"/>.
        /// </param>
        /// <param name="username">
        /// Plaintext NNTP username from the SASL client. Rendered as <c>'{Username}'</c> in the message template.
        /// </param>
        /// <remarks>
        /// <para>
        /// Source-generated at EventId <c>300</c>, <see cref="LogLevel.Debug"/>. Message template:
        /// <c>Auth.MySql SASL CRAM-MD5 credential lookup started for user '{Username}'</c>.
        /// </para>
        /// <para>Emitted once per <c>TryGetCramSecret</c> invocation after username validation.</para>
        /// </remarks>
        [LoggerMessage(
            EventId = 300,
            Level = LogLevel.Debug,
            Message = "Auth.MySql SASL CRAM-MD5 credential lookup started for user '{Username}'")]
        private static partial void CramLookupStarted(ILogger logger, string username);

        /// <summary>
        /// Logs that the backing store returned no row for the requested username.
        /// </summary>
        /// <param name="logger">Store category logger. Must not be <see langword="null"/>.</param>
        /// <param name="username">
        /// Username with no <c>nntpusers</c> match. Rendered as <c>'{Username}'</c> in the message template.
        /// </param>
        /// <remarks>
        /// <para>
        /// Source-generated at EventId <c>301</c>, <see cref="LogLevel.Debug"/>. Message template:
        /// <c>Auth.MySql SASL CRAM-MD5 credential lookup rejected for user '{Username}': user not found</c>.
        /// </para>
        /// <para>
        /// Invoked when <see cref="Records.INntpUserRecordStore.TryGetUser"/> returns <see langword="null"/>. The method
        /// returns <see langword="false"/> with an empty secret; this is an expected authentication outcome.
        /// </para>
        /// </remarks>
        [LoggerMessage(
            EventId = 301,
            Level = LogLevel.Debug,
            Message = "Auth.MySql SASL CRAM-MD5 credential lookup rejected for user '{Username}': user not found")]
        private static partial void CramLookupUserNotFound(ILogger logger, string username);

        /// <summary>
        /// Logs that the account exists but is disabled for authentication.
        /// </summary>
        /// <param name="logger">Store category logger. Must not be <see langword="null"/>.</param>
        /// <param name="username">
        /// Disabled account name. Rendered as <c>'{Username}'</c> in the message template.
        /// </param>
        /// <remarks>
        /// <para>
        /// Source-generated at EventId <c>302</c>, <see cref="LogLevel.Warning"/>. Message template:
        /// <c>Auth.MySql SASL CRAM-MD5 credential lookup rejected for user '{Username}': account disabled</c>.
        /// </para>
        /// <para>
        /// Invoked when <see cref="Records.MySqlUserRecord.IsEnabled"/> is <see langword="false"/>. Returns
        /// <see langword="false"/> with an empty secret without throwing.
        /// </para>
        /// </remarks>
        [LoggerMessage(
            EventId = 302,
            Level = LogLevel.Warning,
            Message = "Auth.MySql SASL CRAM-MD5 credential lookup rejected for user '{Username}': account disabled")]
        private static partial void CramLookupAccountDisabled(ILogger logger, string username);

        /// <summary>
        /// Logs that password-oriented authentication (including CRAM-MD5) is not permitted for the account.
        /// </summary>
        /// <param name="logger">Store category logger. Must not be <see langword="null"/>.</param>
        /// <param name="username">
        /// Account name denied by policy. Rendered as <c>'{Username}'</c> in the message template.
        /// </param>
        /// <remarks>
        /// <para>
        /// Source-generated at EventId <c>305</c>, <see cref="LogLevel.Debug"/>. Message template:
        /// <c>Auth.MySql SASL CRAM-MD5 credential lookup rejected for user '{Username}': password-based authentication not permitted</c>.
        /// </para>
        /// <para>
        /// Invoked when <see cref="Records.MySqlUserRecord.AllowAuthPlain"/> is <see langword="false"/> (database
        /// <c>allow_auth_plain</c> not <c>Y</c>). CRAM-MD5 is gated by the same flag as AUTHINFO PASS and SASL PLAIN.
        /// </para>
        /// </remarks>
        [LoggerMessage(
            EventId = 305,
            Level = LogLevel.Debug,
            Message = "Auth.MySql SASL CRAM-MD5 credential lookup rejected for user '{Username}': password-based authentication not permitted")]
        private static partial void CramLookupNotPermitted(ILogger logger, string username);

        /// <summary>
        /// Logs that the decrypted password cannot be encoded as US-ASCII for CRAM-MD5 HMAC.
        /// </summary>
        /// <param name="logger">Store category logger. Must not be <see langword="null"/>.</param>
        /// <param name="username">
        /// Account name whose password contains non-ASCII code points. Rendered as <c>'{Username}'</c> in the message template.
        /// </param>
        /// <remarks>
        /// <para>
        /// Source-generated at EventId <c>306</c>, <see cref="LogLevel.Warning"/>. Message template:
        /// <c>Auth.MySql SASL CRAM-MD5 credential lookup rejected for user '{Username}': non-ASCII password</c>.
        /// </para>
        /// <para>
        /// Invoked when ASCII validation of <see cref="Records.MySqlUserRecord.AccountPassword"/> fails in the store
        /// implementation. Indicates a data or encoding
        /// constraint rather than a wrong client proof. Returns <see langword="false"/> without throwing.
        /// </para>
        /// </remarks>
        [LoggerMessage(
            EventId = 306,
            Level = LogLevel.Warning,
            Message = "Auth.MySql SASL CRAM-MD5 credential lookup rejected for user '{Username}': non-ASCII password")]
        private static partial void CramLookupNonAsciiPassword(ILogger logger, string username);

        /// <summary>
        /// Logs that CRAM-MD5 secret material was returned and the user record was staged for SASL completion.
        /// </summary>
        /// <param name="logger">Store category logger. Must not be <see langword="null"/>.</param>
        /// <param name="username">
        /// Account name for which ASCII secret bytes were produced. Rendered as <c>'{Username}'</c> in the message template.
        /// </param>
        /// <remarks>
        /// <para>
        /// Source-generated at EventId <c>303</c>, <see cref="LogLevel.Debug"/>. Message template:
        /// <c>Auth.MySql SASL CRAM-MD5 credential lookup succeeded for user '{Username}'</c>.
        /// </para>
        /// <para>
        /// Emitted after ASCII secret bytes are assigned to the <c>secret</c> out parameter and
        /// <see cref="Records.MySqlUserRecordSaslCache.Set"/> runs. Does not log secret length or byte content.
        /// </para>
        /// </remarks>
        [LoggerMessage(
            EventId = 303,
            Level = LogLevel.Debug,
            Message = "Auth.MySql SASL CRAM-MD5 credential lookup succeeded for user '{Username}'")]
        private static partial void CramLookupSucceeded(ILogger logger, string username);

        /// <summary>
        /// Logs a backend fault during CRAM-MD5 secret lookup and attaches the underlying exception.
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
        /// template (for example <see cref="AuthMySqlFailureReason.ConnectTimeout"/>).
        /// </param>
        /// <remarks>
        /// <para>
        /// Source-generated at EventId <c>304</c>, <see cref="LogLevel.Error"/>. Message template:
        /// <c>Auth.MySql SASL CRAM-MD5 credential lookup failed for user '{Username}' due to backend error (Reason={FailureReason})</c>.
        /// </para>
        /// <para>
        /// Invoked from the <c>catch</c> block in <c>TryGetCramSecret</c> before throwing
        /// <see cref="Sockets.Authentication.NntpCredentialStoreTransientException"/>.
        /// <see cref="OperationCanceledException"/> is rethrown without logging here. Never swallows <paramref name="ex"/>.
        /// </para>
        /// </remarks>
        [LoggerMessage(
            EventId = 304,
            Level = LogLevel.Error,
            Message = "Auth.MySql SASL CRAM-MD5 credential lookup failed for user '{Username}' due to backend error (Reason={FailureReason})")]
        private static partial void CramLookupFailed(
            ILogger logger,
            Exception ex,
            string username,
            AuthMySqlFailureReason failureReason);
    }
}
