// <copyright file="RedisSessionReconciliationHostedService.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
namespace Vector.NNTP.Session.Redis.HostedServices
{
    /// <summary>
    /// LoggerMessage definitions shared by reconciliation hosted services.
    /// </summary>
    internal static partial class RedisSessionReconciliationHostedServiceLog
    {
        /// <summary>
        /// Logs debug when a hosted reconciliation sweep starts for an account.
        /// </summary>
        /// <param name="logger">Target logger for the structured event.</param>
        /// <param name="accountKey">Normalized account key being reconciled.</param>
        [LoggerMessage(EventName = "RedisReconciliationStarted", Level = LogLevel.Debug, Message = "Redis reconciliation started AccountKey={AccountKey}")]
        public static partial void RedisReconciliationStarted(ILogger logger, string accountKey);

        /// <summary>
        /// Logs information when a hosted reconciliation sweep completes for an account.
        /// </summary>
        /// <param name="logger">Target logger for the structured event.</param>
        /// <param name="accountKey">Normalized account key that was reconciled.</param>
        /// <param name="sessionsBefore">Session counter text before reconciliation (diagnostic).</param>
        /// <param name="sessionsAfter">Session counter text after reconciliation (diagnostic).</param>
        [LoggerMessage(EventName = "RedisReconciliationCompleted", Level = LogLevel.Information, Message = "Redis reconciliation completed AccountKey={AccountKey} SessionsBefore={SessionsBefore} SessionsAfter={SessionsAfter}")]
        public static partial void RedisReconciliationCompleted(ILogger logger, string accountKey, string sessionsBefore, string sessionsAfter);

        /// <summary>
        /// Logs warning when a hosted reconciliation sweep fails for an account.
        /// </summary>
        /// <param name="logger">Target logger for the structured event.</param>
        /// <param name="exception">Exception raised by the reconciliation coordinator.</param>
        /// <param name="accountKey">Normalized account key that failed reconciliation.</param>
        [LoggerMessage(EventName = "RedisReconciliationFailed", Level = LogLevel.Warning, Message = "Redis reconciliation failed AccountKey={AccountKey}")]
        public static partial void RedisReconciliationFailed(ILogger logger, Exception exception, string accountKey);
    }
}
