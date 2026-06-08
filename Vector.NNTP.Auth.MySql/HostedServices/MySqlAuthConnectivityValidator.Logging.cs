// <copyright file="MySqlAuthConnectivityValidator.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// EventIds 110-111 (fail-fast MySQL connectivity probe at host startup).

using Microsoft.Extensions.Logging;

namespace Vector.NNTP.Auth.MySql.HostedServices
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> helpers for <see cref="MySqlAuthConnectivityValidator"/>
    /// startup connectivity logging.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Role:</b> Logging partial for <see cref="MySqlAuthConnectivityValidator"/>. Emits Information or Error events from
    /// <see cref="Microsoft.Extensions.Hosting.IHostedService.StartAsync"/> after the synchronous <c>SELECT 1</c> probe against
    /// <see cref="Configuration.MySqlAuthOptions.ConnectionString"/>. Registered by
    /// <see cref="DependencyInjection.ServiceCollectionExtensions.AddNntpMySqlAuth"/> so unreachable auth databases fail
    /// fast before NNTP sessions accept credential traffic.
    /// </para>
    /// <para>
    /// <b>Logger category:</b> Callers pass <see cref="ILogger{TCategoryName}"/> for
    /// <see cref="MySqlAuthConnectivityValidator"/> from the hosted-service instance. Methods are <see langword="static"/>
    /// <see langword="partial"/> with an explicit <see cref="ILogger"/> parameter so
    /// <see cref="LoggerMessageAttribute"/> source generation remains valid on the nested partial class.
    /// </para>
    /// <para><b>Event identifiers:</b></para>
    /// <list type="bullet">
    /// <item><description>EventId <c>110</c> — probe succeeded (<see cref="ConnectivityValidationSucceeded"/>).</description></item>
    /// <item><description>EventId <c>111</c> — probe failed (<see cref="ConnectivityValidationFailed"/>).</description></item>
    /// </list>
    /// <para>
    /// <b>Failure handling:</b> <see cref="ConnectivityValidationFailed"/> is always followed by an
    /// <see cref="InvalidOperationException"/> rethrow from <c>StartAsync</c> with the original exception as the inner
    /// exception. The host must not start serving authentication when EventId <c>111</c> is emitted.
    /// </para>
    /// <para>
    /// <b>Privacy:</b> Message templates do not include connection strings or credentials. The attached exception on failure
    /// may still surface provider error text; operators should configure redaction at the logging pipeline if required.
    /// </para>
    /// <para><b>Threading:</b> Invoked once per process start on the host startup thread; not used from session handlers.</para>
    /// </remarks>
    internal sealed partial class MySqlAuthConnectivityValidator
    {
        /// <summary>
        /// Logs that the startup <c>SELECT 1</c> connectivity probe completed successfully.
        /// </summary>
        /// <param name="logger">
        /// Hosted-service category logger (typically <see cref="ILogger{TCategoryName}"/> for
        /// <see cref="MySqlAuthConnectivityValidator"/>). Must not be <see langword="null"/>.
        /// </param>
        /// <remarks>
        /// <para>
        /// Source-generated at EventId <c>110</c>, <see cref="LogLevel.Information"/>. Message template:
        /// <c>Auth.MySql startup connectivity validation succeeded (SELECT 1)</c>.
        /// </para>
        /// <para>
        /// Emitted after <see cref="MySqlConnector.MySqlConnection.Open"/> and successful
        /// <c>ExecuteScalar</c> on the probe command (command timeout
        /// <c>ConnectivityTimeoutSeconds</c> = <c>5</c>). <c>StartAsync</c> then returns
        /// <see cref="Task.CompletedTask"/> and the host may accept authentication traffic.
        /// </para>
        /// </remarks>
        [LoggerMessage(
            EventId = 110,
            Level = LogLevel.Information,
            Message = "Auth.MySql startup connectivity validation succeeded (SELECT 1)")]
        private static partial void ConnectivityValidationSucceeded(ILogger logger);

        /// <summary>
        /// Logs that the startup connectivity probe failed and attaches the underlying exception.
        /// </summary>
        /// <param name="logger">
        /// Hosted-service category logger (typically <see cref="ILogger{TCategoryName}"/> for
        /// <see cref="MySqlAuthConnectivityValidator"/>). Must not be <see langword="null"/>.
        /// </param>
        /// <param name="ex">
        /// Exception from connection open or <c>SELECT 1</c> execution (for example timeout, auth failure, or network
        /// fault). Recorded on the log event by the source-generated helper even though the message template has no
        /// placeholders.
        /// </param>
        /// <remarks>
        /// <para>
        /// Source-generated at EventId <c>111</c>, <see cref="LogLevel.Error"/>. Message template:
        /// <c>Auth.MySql startup connectivity validation failed</c>.
        /// </para>
        /// <para>
        /// Invoked from the <c>catch</c> block in <c>StartAsync</c> before throwing
        /// <see cref="InvalidOperationException"/> with message
        /// <c>MySQL authentication connectivity validation failed. See inner exception for details.</c>.
        /// Never swallows <paramref name="ex"/>; the hosted service always fails startup after logging.
        /// </para>
        /// </remarks>
        [LoggerMessage(
            EventId = 111,
            Level = LogLevel.Error,
            Message = "Auth.MySql startup connectivity validation failed")]
        private static partial void ConnectivityValidationFailed(ILogger logger, Exception ex);
    }
}
