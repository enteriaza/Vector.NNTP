// <copyright file="MySqlNntpCredentialValidator.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// EventIds 200-204 and 206-210 (credential validation and SASL exchange lifecycle).

using Microsoft.Extensions.Logging;
using Vector.NNTP.Auth.MySql.Configuration;
using Vector.NNTP.Session.Policy;

namespace Vector.NNTP.Auth.MySql.Credentials
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> helpers for <see cref="MySqlNntpCredentialValidator"/>
    /// password and SASL finalization logging.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Role:</b> Logging partial for <see cref="MySqlNntpCredentialValidator"/>. Emits structured events from
    /// <see cref="Sockets.Authentication.INntpCredentialValidator.ValidatePasswordAsync"/>,
    /// <see cref="Sockets.Authentication.INntpSaslAccountAuthenticator.CompleteSaslAccountAsync"/> (via
    /// <c>FinalizeAuthenticationAsync</c>), and <see cref="Sockets.Authentication.INntpSaslAccountAuthenticator.AbandonSaslExchange"/>.
    /// Registered by <see cref="DependencyInjection.ServiceCollectionExtensions.AddNntpMySqlAuth"/>.
    /// </para>
    /// <para>
    /// <b>Logger category:</b> Callers pass <see cref="ILogger{TCategoryName}"/> for
    /// <see cref="MySqlNntpCredentialValidator"/> from the validator instance. Methods are <see langword="static"/>
    /// <see langword="partial"/> with an explicit <see cref="ILogger"/> parameter so
    /// <see cref="LoggerMessageAttribute"/> source generation remains valid on the nested partial class.
    /// </para>
    /// <para><b>Event identifiers:</b></para>
    /// <list type="bullet">
    /// <item><description>EventId <c>200</c> — finalization started (<see cref="AuthenticationFinalizing"/>).</description></item>
    /// <item><description>EventId <c>201</c> — user not found (<see cref="AuthenticationRejectedUserNotFound"/>).</description></item>
    /// <item><description>EventId <c>202</c> — account disabled (<see cref="AuthenticationRejectedAccountDisabled"/>).</description></item>
    /// <item><description>EventId <c>203</c> — invalid credentials or policy denial (<see cref="AuthenticationRejectedInvalidCredentials"/>).</description></item>
    /// <item><description>EventId <c>204</c> — authentication succeeded (<see cref="AuthenticationSucceeded"/>).</description></item>
    /// <item><description>EventId <c>205</c> — reserved (no helper in this partial).</description></item>
    /// <item><description>EventId <c>206</c> — backend fault with exception (<see cref="AuthenticationBackendFailed"/>).</description></item>
    /// <item><description>EventId <c>207</c> — transient failure outcome (<see cref="AuthenticationTransientFailure"/>).</description></item>
    /// <item><description>EventId <c>208</c> — SASL exchange abandoned (<see cref="SaslExchangeAbandoned"/>).</description></item>
    /// <item><description>EventId <c>209</c> — per-exchange SASL cache hit (<see cref="SaslCacheHit"/>).</description></item>
    /// <item><description>EventId <c>210</c> — per-exchange SASL cache miss (<see cref="SaslCacheMiss"/>).</description></item>
    /// </list>
    /// <para>
    /// <b>Observability pairing:</b> Rejection and success paths pair with <see cref="Telemetry.AuthMySqlMetrics.RecordValidate"/>
    /// (<c>invalid_credentials</c>, <c>success</c>, or <c>transient_failure</c>). Backend faults also set OpenTelemetry error
    /// status on <c>auth.mysql.validate.password</c> / <c>auth.mysql.validate.sasl</c> spans when tracing is enabled.
    /// Password-fingerprint burst-cache hits are counted in metrics only; they do not emit a dedicated log line in this partial.
    /// </para>
    /// <para>
    /// <b>Privacy:</b> Logs include mechanism label, username, client IP, TLS flag, and policy metadata on success. Passwords,
    /// SCRAM keys, and decrypted row fields are never written by these helpers.
    /// </para>
    /// <para><b>Threading:</b> Static helpers; safe to call from concurrent NNTP session handlers on the singleton validator.</para>
    /// </remarks>
    internal sealed partial class MySqlNntpCredentialValidator
    {
        /// <summary>
        /// Logs that host-side credential finalization is beginning for a password or SASL completion attempt.
        /// </summary>
        /// <param name="logger">
        /// Validator category logger (typically <see cref="ILogger{TCategoryName}"/> for
        /// <see cref="MySqlNntpCredentialValidator"/>). Must not be <see langword="null"/>.
        /// </param>
        /// <param name="mechanism">
        /// Authentication mechanism label (for example AUTHINFO PASS, SASL PLAIN, SASL SCRAM-SHA-256). Rendered as
        /// <c>{Mechanism}</c> in the message template.
        /// </param>
        /// <param name="username">
        /// Account name presented by the client. Rendered as <c>'{Username}'</c> in the message template.
        /// </param>
        /// <param name="clientIp">
        /// Formatted client IP from <c>FormatClientIp</c>. Rendered as <c>{ClientIp}</c> in the message template.
        /// </param>
        /// <param name="isTls">
        /// Whether the session transport is TLS-protected. Rendered as <c>TLS=</c> in the message template.
        /// </param>
        /// <remarks>
        /// <para>
        /// Source-generated at EventId <c>200</c>, <see cref="LogLevel.Debug"/>. Message template:
        /// <c>Auth.MySql finalizing {Mechanism} authentication for user '{Username}' from {ClientIp} (TLS={IsTls})</c>.
        /// </para>
        /// <para>
        /// Emitted at the start of <c>ValidatePasswordAsync</c> and <c>FinalizeAuthenticationAsync</c> before record lookup or
        /// cache consumption.
        /// </para>
        /// </remarks>
        [LoggerMessage(
            EventId = 200,
            Level = LogLevel.Debug,
            Message = "Auth.MySql finalizing {Mechanism} authentication for user '{Username}' from {ClientIp} (TLS={IsTls})")]
        private static partial void AuthenticationFinalizing(
            ILogger logger,
            string mechanism,
            string username,
            string clientIp,
            bool isTls);

        /// <summary>
        /// Logs that no user record was available after lookup for the authentication attempt.
        /// </summary>
        /// <param name="logger">Validator category logger. Must not be <see langword="null"/>.</param>
        /// <param name="mechanism">Authentication mechanism label.</param>
        /// <param name="username">Account name with no backing row.</param>
        /// <param name="clientIp">Formatted client IP.</param>
        /// <remarks>
        /// <para>
        /// Source-generated at EventId <c>201</c>, <see cref="LogLevel.Debug"/>. Message template:
        /// <c>Auth.MySql {Mechanism} authentication rejected for user '{Username}' from {ClientIp}: user not found</c>.
        /// </para>
        /// <para>
        /// Invoked when <see cref="Records.INntpUserRecordStore"/> returns <see langword="null"/> and the per-exchange SASL
        /// cache had no matching staged record. Paired with <see cref="Telemetry.AuthMySqlMetrics.RecordValidate"/> outcome
        /// <c>invalid_credentials</c>. Returns <see cref="Sockets.Authentication.NntpAuthResult.InvalidCredentials"/> without
        /// throwing.
        /// </para>
        /// </remarks>
        [LoggerMessage(
            EventId = 201,
            Level = LogLevel.Debug,
            Message = "Auth.MySql {Mechanism} authentication rejected for user '{Username}' from {ClientIp}: user not found")]
        private static partial void AuthenticationRejectedUserNotFound(
            ILogger logger,
            string mechanism,
            string username,
            string clientIp);

        /// <summary>
        /// Logs that the account exists but is disabled for authentication.
        /// </summary>
        /// <param name="logger">Validator category logger. Must not be <see langword="null"/>.</param>
        /// <param name="mechanism">Authentication mechanism label.</param>
        /// <param name="username">Disabled account name.</param>
        /// <param name="clientIp">Formatted client IP.</param>
        /// <remarks>
        /// <para>
        /// Source-generated at EventId <c>202</c>, <see cref="LogLevel.Warning"/>. Message template:
        /// <c>Auth.MySql {Mechanism} authentication rejected for user '{Username}' from {ClientIp}: account disabled</c>.
        /// </para>
        /// <para>
        /// Invoked when <see cref="Records.MySqlUserRecord.IsEnabled"/> is <see langword="false"/>. Paired with
        /// <c>invalid_credentials</c> validation metrics.
        /// </para>
        /// </remarks>
        [LoggerMessage(
            EventId = 202,
            Level = LogLevel.Warning,
            Message = "Auth.MySql {Mechanism} authentication rejected for user '{Username}' from {ClientIp}: account disabled")]
        private static partial void AuthenticationRejectedAccountDisabled(
            ILogger logger,
            string mechanism,
            string username,
            string clientIp);

        /// <summary>
        /// Logs that authentication failed due to wrong password, disallowed mechanism, or SASL policy denial.
        /// </summary>
        /// <param name="logger">Validator category logger. Must not be <see langword="null"/>.</param>
        /// <param name="mechanism">Authentication mechanism label.</param>
        /// <param name="username">Account name for the rejected attempt.</param>
        /// <param name="clientIp">Formatted client IP.</param>
        /// <remarks>
        /// <para>
        /// Source-generated at EventId <c>203</c>, <see cref="LogLevel.Debug"/>. Message template:
        /// <c>Auth.MySql {Mechanism} authentication rejected for user '{Username}' from {ClientIp}: invalid credentials</c>.
        /// </para>
        /// <para><b>Invocation contexts (all return <see cref="Sockets.Authentication.NntpAuthResult.InvalidCredentials"/>):</b></para>
        /// <list type="bullet">
        /// <item><description><see cref="Records.MySqlUserRecord.AllowAuthPlain"/> is <see langword="false"/> on password paths.</description></item>
        /// <item><description><c>PasswordEquals</c> returned <see langword="false"/> for AUTHINFO / SASL password mechanisms.</description></item>
        /// <item><description>SASL finalize path: mechanism not permitted for the account (SCRAM vs CRAM policy delegate).</description></item>
        /// </list>
        /// <para>Does not distinguish wrong password from policy denial in the message text; operators use mechanism and account flags.</para>
        /// </remarks>
        [LoggerMessage(
            EventId = 203,
            Level = LogLevel.Debug,
            Message = "Auth.MySql {Mechanism} authentication rejected for user '{Username}' from {ClientIp}: invalid credentials")]
        private static partial void AuthenticationRejectedInvalidCredentials(
            ILogger logger,
            string mechanism,
            string username,
            string clientIp);

        /// <summary>
        /// Logs successful authentication and the materialised session policy metadata.
        /// </summary>
        /// <param name="logger">Validator category logger. Must not be <see langword="null"/>.</param>
        /// <param name="mechanism">Authentication mechanism label.</param>
        /// <param name="username">Authenticated account name from the issued <see cref="NntpSessionPolicy"/>.</param>
        /// <param name="clientIp">Formatted client IP.</param>
        /// <param name="allowPosting">Whether POST is permitted on the issued policy.</param>
        /// <param name="accountType">Resolved <see cref="NntpAccountType"/> on the issued policy.</param>
        /// <param name="customerId">Customer identifier from the issued policy.</param>
        /// <remarks>
        /// <para>
        /// Source-generated at EventId <c>204</c>, <see cref="LogLevel.Information"/>. Message template:
        /// <c>Auth.MySql {Mechanism} authentication succeeded for user '{Username}' from {ClientIp} (Posting={AllowPosting}, Type={AccountType}, CustomerId={CustomerId})</c>.
        /// </para>
        /// <para>
        /// Emitted from <c>Succeed</c> after <see cref="NntpSessionPolicy"/> construction and burst-cache population.
        /// Paired with <see cref="Telemetry.AuthMySqlMetrics.RecordValidate"/> outcome <c>success</c>. Does not log session
        /// admission outcomes (handled by session coordination after the validator returns).
        /// </para>
        /// </remarks>
        [LoggerMessage(
            EventId = 204,
            Level = LogLevel.Information,
            Message = "Auth.MySql {Mechanism} authentication succeeded for user '{Username}' from {ClientIp} (Posting={AllowPosting}, Type={AccountType}, CustomerId={CustomerId})")]
        private static partial void AuthenticationSucceeded(
            ILogger logger,
            string mechanism,
            string username,
            string clientIp,
            bool allowPosting,
            NntpAccountType accountType,
            string customerId);

        /// <summary>
        /// Logs a backend fault during credential validation and attaches the underlying exception.
        /// </summary>
        /// <param name="logger">Validator category logger. Must not be <see langword="null"/>.</param>
        /// <param name="ex">Exception from record-store I/O or mapping. Recorded on the log event by the source-generated helper.</param>
        /// <param name="mechanism">Authentication mechanism label.</param>
        /// <param name="username">Account name for the attempt in flight.</param>
        /// <param name="failureReason">
        /// Stable reason from <see cref="AuthMySqlFailureClassifier.Classify"/>, rendered as <c>Reason=</c> in the
        /// companion <see cref="AuthenticationTransientFailure"/> template.
        /// </param>
        /// <remarks>
        /// <para>
        /// Source-generated at EventId <c>206</c>, <see cref="LogLevel.Error"/>. Message template:
        /// <c>Auth.MySql {Mechanism} authentication failed for user '{Username}' due to backend error (Reason={FailureReason})</c>.
        /// </para>
        /// <para>
        /// Always paired with <see cref="AuthenticationTransientFailure"/> and followed by
        /// <see cref="Sockets.Authentication.NntpAuthResult.TransientFailure"/> (503-class semantics to the client).
        /// <see cref="OperationCanceledException"/> is rethrown without calling this helper. Never swallows <paramref name="ex"/>.
        /// </para>
        /// </remarks>
        [LoggerMessage(
            EventId = 206,
            Level = LogLevel.Error,
            Message = "Auth.MySql {Mechanism} authentication failed for user '{Username}' due to backend error (Reason={FailureReason})")]
        private static partial void AuthenticationBackendFailed(
            ILogger logger,
            Exception ex,
            string mechanism,
            string username,
            AuthMySqlFailureReason failureReason);

        /// <summary>
        /// Logs that the validator will return a transient failure outcome after a backend error.
        /// </summary>
        /// <param name="logger">Validator category logger. Must not be <see langword="null"/>.</param>
        /// <param name="mechanism">Authentication mechanism label.</param>
        /// <param name="username">Account name for the attempt in flight.</param>
        /// <param name="failureReason">
        /// Classified failure reason (for example <see cref="AuthMySqlFailureReason.ConnectTimeout"/>). Rendered as
        /// <c>Reason=</c> in the message template.
        /// </param>
        /// <remarks>
        /// <para>
        /// Source-generated at EventId <c>207</c>, <see cref="LogLevel.Debug"/>. Message template:
        /// <c>Auth.MySql {Mechanism} authentication transient failure for user '{Username}' (Reason={FailureReason})</c>.
        /// </para>
        /// <para>
        /// Emitted immediately after <see cref="AuthenticationBackendFailed"/> in the <c>catch</c> blocks of password and
        /// SASL finalization paths. Provides a Debug-level correlate for operators filtering out Error noise while still
        /// recording the classified reason.
        /// </para>
        /// </remarks>
        [LoggerMessage(
            EventId = 207,
            Level = LogLevel.Debug,
            Message = "Auth.MySql {Mechanism} authentication transient failure for user '{Username}' (Reason={FailureReason})")]
        private static partial void AuthenticationTransientFailure(
            ILogger logger,
            string mechanism,
            string username,
            AuthMySqlFailureReason failureReason);

        /// <summary>
        /// Logs that an in-flight SASL exchange was abandoned and the per-exchange record cache was cleared.
        /// </summary>
        /// <param name="logger">Validator category logger. Must not be <see langword="null"/>.</param>
        /// <remarks>
        /// <para>
        /// Source-generated at EventId <c>208</c>, <see cref="LogLevel.Debug"/>. Message template:
        /// <c>Auth.MySql SASL exchange abandoned; per-exchange record cache cleared</c>.
        /// </para>
        /// <para>
        /// Invoked from <see cref="Sockets.Authentication.INntpSaslAccountAuthenticator.AbandonSaslExchange"/> before
        /// <see cref="Records.MySqlUserRecordSaslCache.Clear"/>. Idempotent when no exchange is active. Does not affect the
        /// TTL burst cache (<see cref="Records.MySqlUserRecordCache"/>).
        /// </para>
        /// </remarks>
        [LoggerMessage(
            EventId = 208,
            Level = LogLevel.Debug,
            Message = "Auth.MySql SASL exchange abandoned; per-exchange record cache cleared")]
        private static partial void SaslExchangeAbandoned(ILogger logger);

        /// <summary>
        /// Logs that a staged user record was consumed from the per-exchange SASL cache during finalize.
        /// </summary>
        /// <param name="logger">Validator category logger. Must not be <see langword="null"/>.</param>
        /// <param name="username">
        /// Username supplied to SASL account completion. Rendered as <c>'{Username}'</c> in the message template.
        /// </param>
        /// <remarks>
        /// <para>
        /// Source-generated at EventId <c>209</c>, <see cref="LogLevel.Debug"/>. Message template:
        /// <c>Auth.MySql SASL per-exchange cache hit for user '{Username}'</c>.
        /// </para>
        /// <para>
        /// Emitted when <see cref="Records.MySqlUserRecordSaslCache.TryTake"/> succeeds in <c>FinalizeAuthenticationAsync</c>
        /// (record was staged by <see cref="MySqlScramCredentialStore"/> or <see cref="MySqlCramMd5CredentialStore"/> during
        /// secret retrieval). Avoids a second MySQL lookup on the hot path.
        /// </para>
        /// </remarks>
        [LoggerMessage(
            EventId = 209,
            Level = LogLevel.Debug,
            Message = "Auth.MySql SASL per-exchange cache hit for user '{Username}'")]
        private static partial void SaslCacheHit(ILogger logger, string username);

        /// <summary>
        /// Logs that no staged record was available in the per-exchange SASL cache before async store lookup.
        /// </summary>
        /// <param name="logger">Validator category logger. Must not be <see langword="null"/>.</param>
        /// <param name="username">
        /// Username for the SASL completion attempt. Rendered as <c>'{Username}'</c> in the message template.
        /// </param>
        /// <remarks>
        /// <para>
        /// Source-generated at EventId <c>210</c>, <see cref="LogLevel.Debug"/>. Message template:
        /// <c>Auth.MySql SASL per-exchange cache miss for user '{Username}'</c>.
        /// </para>
        /// <para>
        /// Emitted when <see cref="Records.MySqlUserRecordSaslCache.TryTake"/> fails and
        /// <see cref="Records.INntpUserRecordStore.TryGetUserAsync"/> runs. A miss does not imply authentication failure;
        /// it only means the credential store did not stage a row or the username did not match the staged record.
        /// </para>
        /// </remarks>
        [LoggerMessage(
            EventId = 210,
            Level = LogLevel.Debug,
            Message = "Auth.MySql SASL per-exchange cache miss for user '{Username}'")]
        private static partial void SaslCacheMiss(ILogger logger, string username);
    }
}
