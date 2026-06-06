// <copyright file="HistoryDatabaseHostedService.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// EventId range: 120-139.

namespace Vector.NNTP.HistoryDB.HostedServices
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> partial methods for <see cref="HistoryDatabaseHostedService"/>.
    /// </summary>
    /// <remarks>
    /// Implementations bind to the primary-constructor <c>logger</c> parameter from
    /// <see cref="HistoryDatabaseHostedService"/>.
    /// </remarks>
    internal sealed partial class HistoryDatabaseHostedService
    {
        /// <summary>Logs rebuild start.</summary>
        /// <param name="generation">Rebuild generation.</param>
        /// <param name="resume">Whether a resume key was loaded.</param>
        [LoggerMessage(EventId = 120, Level = LogLevel.Information,
            Message = "HistoryDB rebuild started (generation={Generation}, resume={Resume}).")]
        private partial void LogRebuildStarted(ulong generation, bool resume);

        /// <summary>Logs rebuild completion.</summary>
        /// <param name="keys">Keys processed.</param>
        [LoggerMessage(EventId = 121, Level = LogLevel.Information,
            Message = "HistoryDB rebuild completed ({Keys} keys); CHECK operational.")]
        private partial void LogRebuildCompleted(long keys);

        /// <summary>Logs rebuild failure before rethrow.</summary>
        /// <param name="exception">Failure exception.</param>
        /// <param name="generation">Rebuild generation.</param>
        /// <param name="keysProcessed">Keys processed before failure.</param>
        [LoggerMessage(EventId = 122, Level = LogLevel.Error,
            Message = "HistoryDB rebuild failed (generation={Generation}, keysProcessed={KeysProcessed}).")]
        private partial void LogRebuildFailed(Exception exception, ulong generation, long keysProcessed);

        /// <summary>Logs invalid resume key hex with Base64 fallback.</summary>
        /// <param name="exception">Format exception from hex parse.</param>
        [LoggerMessage(EventId = 123, Level = LogLevel.Warning,
            Message = "HistoryDB rebuild resume key hex parse failed; attempting Base64 fallback.")]
        private partial void LogResumeKeyHexParseFailed(Exception exception);
    }
}
