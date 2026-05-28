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
        [LoggerMessage(EventName = "QuotaDecremented", Level = LogLevel.Debug, Message = "Quota decremented AccountKey={AccountKey} CommandBytes={CommandBytes} RemainingQuotaBytes={RemainingQuotaBytes}")]
        public static partial void QuotaDecremented(ILogger logger, string accountKey, long commandBytes, long remainingQuotaBytes);

        [LoggerMessage(EventName = "QuotaExceeded", Level = LogLevel.Information, Message = "Quota exceeded AccountKey={AccountKey} SessionId={SessionId} Username={Username}")]
        public static partial void QuotaExceeded(ILogger logger, string accountKey, string sessionId, string username);

        [LoggerMessage(EventName = "QuotaEnforcementFailed", Level = LogLevel.Warning, Message = "Quota enforcement failed AccountKey={AccountKey} SessionId={SessionId}")]
        public static partial void QuotaEnforcementFailed(ILogger logger, Exception exception, string accountKey, string sessionId);

        [LoggerMessage(EventName = "ForcedDeauth", Level = LogLevel.Information, Message = "Forced deauth SessionId={SessionId} Username={Username} Reason={Reason}")]
        public static partial void ForcedDeauth(ILogger logger, string sessionId, string username, string reason);
    }
}
