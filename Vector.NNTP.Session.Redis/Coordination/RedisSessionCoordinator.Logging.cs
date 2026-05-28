// <copyright file="RedisSessionCoordinator.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
namespace Vector.NNTP.Session.Redis.Coordination
{
    /// <summary>LoggerMessage definitions for <see cref="RedisSessionCoordinator"/>.</summary>
    public sealed partial class RedisSessionCoordinator
    {
        [LoggerMessage(
            EventName = "SessionAdmissionGranted",
            Level = LogLevel.Information,
            Message = "Session admission granted Username={Username} ClientIp={ClientIp}")]
        private partial void LogInformationSessionAdmissionGranted(string username, string clientIp);

        [LoggerMessage(
            EventName = "SessionAdmissionDenied",
            Level = LogLevel.Information,
            Message = "Session admission denied Username={Username} ClientIp={ClientIp} Outcome={Outcome}")]
        private partial void LogInformationSessionAdmissionDenied(string username, string clientIp, string outcome);

        [LoggerMessage(
            EventName = "SessionAdmissionBackendFailure",
            Level = LogLevel.Warning,
            Message = "Session admission backend failure Username={Username}")]
        private partial void LogWarningSessionAdmissionBackendFailure(string username, Exception ex);

        [LoggerMessage(
            EventName = "RedisReconciliationFailed",
            Level = LogLevel.Warning,
            Message = "Acquire-time reconciliation failed AccountKey={AccountKey}")]
        private partial void LogWarningRedisReconciliationFailed(string accountKey, Exception ex);

        [LoggerMessage(
            EventName = "RedisOperationSlow",
            Level = LogLevel.Warning,
            Message = "Slow Redis call Operation={Operation} ElapsedMs={ElapsedMs}")]
        private partial void LogWarningRedisOperationSlow(string operation, double elapsedMs);
    }
}
