// <copyright file="HistoryDatabaseService.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HistoryDatabaseService.Logging.cs -- Source-generated [LoggerMessage] partial methods for HistoryDatabaseService.
//
// EventId range: 100-119.

namespace Vector.NNTP.HistoryDB.Services
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> partial methods for <see cref="HistoryDatabaseService"/>.
    /// </summary>
    internal sealed partial class HistoryDatabaseService
    {
        /// <summary>Logs CHECK path when HistoryDB is not yet operational.</summary>
        [LoggerMessage(EventId = 100, Level = LogLevel.Debug, Message = "HistoryDB CHECK rejected: not operational.")]
        private partial void LogCheckNotOperational();

        /// <summary>Logs Redis unavailability on CHECK.</summary>
        /// <param name="exception">Redis exception.</param>
        [LoggerMessage(EventId = 101, Level = LogLevel.Warning, Message = "HistoryDB Redis unavailable for CHECK.")]
        private partial void LogCheckRedisUnavailable(Exception exception);

        /// <summary>Logs Redis timeout on CHECK.</summary>
        /// <param name="exception">Timeout exception.</param>
        [LoggerMessage(EventId = 102, Level = LogLevel.Warning, Message = "HistoryDB Redis timeout for CHECK.")]
        private partial void LogCheckRedisTimeout(Exception exception);

        /// <summary>Logs Redis unavailability on record.</summary>
        /// <param name="exception">Redis exception.</param>
        [LoggerMessage(EventId = 103, Level = LogLevel.Warning, Message = "HistoryDB Redis unavailable for record.")]
        private partial void LogRecordRedisUnavailable(Exception exception);

        /// <summary>Logs Redis timeout on record.</summary>
        /// <param name="exception">Timeout exception.</param>
        [LoggerMessage(EventId = 104, Level = LogLevel.Warning, Message = "HistoryDB Redis timeout for record.")]
        private partial void LogRecordRedisTimeout(Exception exception);

        /// <summary>Logs persist queue full after successful Redis record.</summary>
        /// <param name="capacity">Configured queue capacity.</param>
        [LoggerMessage(EventId = 105, Level = LogLevel.Error,
            Message = "HistoryDB persist queue full; Rocks backfill dropped after Redis record (queue capacity {Capacity}).")]
        private partial void LogPersistQueueFull(int capacity);
    }
}
