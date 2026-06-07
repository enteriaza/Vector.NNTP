// <copyright file="RedisSessionCoordinator.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
namespace Vector.NNTP.Session.Redis.Coordination
{
    /// <summary>LoggerMessage definitions for <see cref="RedisSessionCoordinator"/>.</summary>
    public sealed partial class RedisSessionCoordinator
    {
        /// <summary>
        /// Log an information message when a session admission is granted.
        /// </summary>
        /// <param name="logger">Target logger for the structured event.</param>
        /// <param name="username">The username of the session.</param>
        /// <param name="clientIp">The client IP of the session.</param>
        [LoggerMessage(
            EventName = "SessionAdmissionGranted",
            Level = LogLevel.Information,
            Message = "Session admission granted Username={Username} ClientIp={ClientIp}")]
        private static partial void LogInformationSessionAdmissionGranted(ILogger logger, string username, string clientIp);

        /// <summary>
        /// Log an information message when a session admission is denied.
        /// </summary>
        /// <param name="logger">Target logger for the structured event.</param>
        /// <param name="username">The username of the session.</param>
        /// <param name="clientIp">The client IP of the session.</param>
        /// <param name="outcome">The outcome of the session admission.</param>
        [LoggerMessage(
            EventName = "SessionAdmissionDenied",
            Level = LogLevel.Information,
            Message = "Session admission denied Username={Username} ClientIp={ClientIp} Outcome={Outcome}")]
        private static partial void LogInformationSessionAdmissionDenied(ILogger logger, string username, string clientIp, string outcome);

        /// <summary>
        /// Log a warning message when a session admission backend failure occurs.
        /// </summary>
        /// <param name="logger">Target logger for the structured event.</param>
        /// <param name="ex">The exception that occurred.</param>
        /// <param name="username">The username of the session.</param>
        [LoggerMessage(
            EventName = "SessionAdmissionBackendFailure",
            Level = LogLevel.Warning,
            Message = "Session admission backend failure Username={Username}")]
        private static partial void LogWarningSessionAdmissionBackendFailure(ILogger logger, Exception ex, string username);

        /// <summary>
        /// Log a warning message when a Redis reconciliation fails.
        /// </summary>
        /// <param name="logger">Target logger for the structured event.</param>
        /// <param name="ex">The exception that occurred.</param>
        /// <param name="accountKey">The account key of the session.</param>
        [LoggerMessage(
            EventName = "RedisReconciliationFailed",
            Level = LogLevel.Warning,
            Message = "Acquire-time reconciliation failed AccountKey={AccountKey}")]
        private static partial void LogWarningRedisReconciliationFailed(ILogger logger, Exception ex, string accountKey);

        /// <summary>
        /// Log a warning message when a Redis operation is slow.
        /// </summary>
        /// <param name="logger">Target logger for the structured event.</param>
        /// <param name="operation">The operation that was slow.</param>
        /// <param name="elapsedMs">The elapsed time in milliseconds.</param>
        [LoggerMessage(
            EventName = "RedisOperationSlow",
            Level = LogLevel.Warning,
            Message = "Slow Redis call Operation={Operation} ElapsedMs={ElapsedMs}")]
        private static partial void LogWarningRedisOperationSlow(ILogger logger, string operation, double elapsedMs);
    }
}
