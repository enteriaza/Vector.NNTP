// <copyright file="NntpQuotaEnforcer.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Logging;

namespace Vector.NNTP.Session.Quota
{
    /// <summary>
    /// Logging for <see cref="NntpQuotaEnforcer"/>.
    /// </summary>
    internal static partial class NntpQuotaEnforcerLog
    {
        /// <summary>
        /// Log a quota decremented message.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="accountKey">The account key.</param>
        /// <param name="commandBytes">The command bytes.</param>
        /// <param name="remainingQuotaBytes">The remaining quota bytes.</param>
        [LoggerMessage(EventName = "QuotaDecremented", Level = LogLevel.Debug, Message = "Quota decremented AccountKey={AccountKey} CommandBytes={CommandBytes} RemainingQuotaBytes={RemainingQuotaBytes}")]
        public static partial void QuotaDecremented(ILogger logger, string accountKey, long commandBytes, long remainingQuotaBytes);

        /// <summary>
        /// Log a quota exceeded message.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="accountKey">The account key.</param>
        /// <param name="sessionId">The session ID.</param>
        /// <param name="username">The username.</param>
        [LoggerMessage(EventName = "QuotaExceeded", Level = LogLevel.Information, Message = "Quota exceeded AccountKey={AccountKey} SessionId={SessionId} Username={Username}")]
        public static partial void QuotaExceeded(ILogger logger, string accountKey, string sessionId, string username);

        /// <summary>
        /// Log a quota enforcement failed message.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="exception">The exception.</param>
        /// <param name="accountKey">The account key.</param>
        /// <param name="sessionId">The session ID.</param>
        [LoggerMessage(EventName = "QuotaEnforcementFailed", Level = LogLevel.Warning, Message = "Quota enforcement failed AccountKey={AccountKey} SessionId={SessionId}")]
        public static partial void QuotaEnforcementFailed(ILogger logger, Exception exception, string accountKey, string sessionId);

        /// <summary>
        /// Log a forced deauth message.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="sessionId">The session ID.</param>
        /// <param name="username">The username.</param>
        /// <param name="reason">The reason.</param>
        [LoggerMessage(EventName = "ForcedDeauth", Level = LogLevel.Information, Message = "Forced deauth SessionId={SessionId} Username={Username} Reason={Reason}")]
        public static partial void ForcedDeauth(ILogger logger, string sessionId, string username, string reason);
    }
}
