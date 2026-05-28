// <copyright file="RedisSessionReconciliationHostedService.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
namespace Vector.NNTP.Session.Redis.HostedServices
{
    /// <summary>
    /// Logging for reconciliation hosted service and coordinator.
    /// </summary>
    internal static partial class RedisSessionReconciliationHostedServiceLog
    {
        /// <summary>
        /// Redis reconciliation started.
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="accountKey"></param>
        [LoggerMessage(EventName = "RedisReconciliationStarted", Level = LogLevel.Debug, Message = "Redis reconciliation started AccountKey={AccountKey}")]
        public static partial void RedisReconciliationStarted(ILogger logger, string accountKey);

        /// <summary>
        /// Redis reconciliation completed.
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="accountKey"></param>
        /// <param name="sessionsBefore"></param>
        /// <param name="sessionsAfter"></param>
        [LoggerMessage(EventName = "RedisReconciliationCompleted", Level = LogLevel.Information, Message = "Redis reconciliation completed AccountKey={AccountKey} SessionsBefore={SessionsBefore} SessionsAfter={SessionsAfter}")]
        public static partial void RedisReconciliationCompleted(ILogger logger, string accountKey, string sessionsBefore, string sessionsAfter);

        /// <summary>
        /// Redis reconciliation failed.
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="exception"></param>
        /// <param name="accountKey"></param>
        [LoggerMessage(EventName = "RedisReconciliationFailed", Level = LogLevel.Warning, Message = "Redis reconciliation failed AccountKey={AccountKey}")]
        public static partial void RedisReconciliationFailed(ILogger logger, Exception exception, string accountKey);
    }
}
