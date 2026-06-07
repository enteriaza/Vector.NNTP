// <copyright file="NntpQuotaEnforcer.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Logging;

namespace Vector.NNTP.Session.Quota
{
    /// <summary>
    /// Source-generated logging for <see cref="NntpQuotaEnforcer"/>.
    /// </summary>
    internal static partial class NntpQuotaEnforcerLog
    {
        /// <summary>
        /// Logs successful block-quota decrement after a billable command.
        /// </summary>
        /// <param name="logger">Target logger.</param>
        /// <param name="accountKey">Normalized account key.</param>
        /// <param name="commandBytes">Bytes attributed to the command.</param>
        /// <param name="remainingQuotaBytes">Remaining quota after decrement.</param>
        [LoggerMessage(EventName = "QuotaDecremented", Level = LogLevel.Debug, Message = "Quota decremented AccountKey={AccountKey} CommandBytes={CommandBytes} RemainingQuotaBytes={RemainingQuotaBytes}")]
        public static partial void QuotaDecremented(ILogger logger, string accountKey, long commandBytes, long remainingQuotaBytes);

        /// <summary>
        /// Logs byte-quota exhaustion forcing session teardown.
        /// </summary>
        /// <param name="logger">Target logger.</param>
        /// <param name="accountKey">Normalized account key.</param>
        /// <param name="sessionId">Session identifier.</param>
        /// <param name="username">Authenticated username.</param>
        [LoggerMessage(EventName = "QuotaExceeded", Level = LogLevel.Information, Message = "Quota exceeded AccountKey={AccountKey} SessionId={SessionId} Username={Username}")]
        public static partial void QuotaExceeded(ILogger logger, string accountKey, string sessionId, string username);

        /// <summary>
        /// Logs unexpected quota coordinator failures.
        /// </summary>
        /// <param name="logger">Target logger.</param>
        /// <param name="exception">Underlying coordinator exception.</param>
        /// <param name="accountKey">Normalized account key.</param>
        /// <param name="sessionId">Session identifier.</param>
        [LoggerMessage(EventName = "QuotaEnforcementFailed", Level = LogLevel.Warning, Message = "Quota enforcement failed AccountKey={AccountKey} SessionId={SessionId}")]
        public static partial void QuotaEnforcementFailed(ILogger logger, Exception exception, string accountKey, string sessionId);

        /// <summary>
        /// Logs forced deauthentication with an accounting reason code.
        /// </summary>
        /// <param name="logger">Target logger.</param>
        /// <param name="sessionId">Session identifier.</param>
        /// <param name="username">Authenticated username.</param>
        /// <param name="reason">Accounting stop reason code.</param>
        [LoggerMessage(EventName = "ForcedDeauth", Level = LogLevel.Information, Message = "Forced deauth SessionId={SessionId} Username={Username} Reason={Reason}")]
        public static partial void ForcedDeauth(ILogger logger, string sessionId, string username, string reason);
    }
}
