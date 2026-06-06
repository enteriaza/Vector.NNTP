// <copyright file="HistoryRocksStatsLogHostedService.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// EventId range: 180-199.

namespace Vector.NNTP.HistoryDB.HostedServices
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> partial methods for <see cref="HistoryRocksStatsLogHostedService"/>.
    /// </summary>
    /// <remarks>
    /// Implementations bind to the primary-constructor <c>logger</c> parameter from
    /// <see cref="HistoryRocksStatsLogHostedService"/>.
    /// </remarks>
    internal sealed partial class HistoryRocksStatsLogHostedService
    {
        /// <summary>Logs stats logging interval.</summary>
        /// <param name="periodSeconds">Snapshot period seconds.</param>
        [LoggerMessage(EventId = 180, Level = LogLevel.Debug,
            Message = "HistoryDB host Rocks stats logging every {PeriodSeconds}s.")]
        private partial void LogStatsInterval(uint periodSeconds);

        /// <summary>Logs stats snapshot failure.</summary>
        /// <param name="exception">Failure exception.</param>
        [LoggerMessage(EventId = 181, Level = LogLevel.Error, Message = "HistoryDB Rocks stats snapshot failed.")]
        private partial void LogStatsSnapshotFailed(Exception exception);
    }
}
