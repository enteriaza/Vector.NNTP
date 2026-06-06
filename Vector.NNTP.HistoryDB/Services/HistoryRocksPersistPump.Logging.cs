// <copyright file="HistoryRocksPersistPump.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// EventId range: 140-159.

namespace Vector.NNTP.HistoryDB.Services
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> partial methods for <see cref="HistoryRocksPersistPump"/>.
    /// </summary>
    /// <remarks>
    /// Implementations bind to the primary-constructor <c>logger</c> parameter from
    /// <see cref="HistoryRocksPersistPump"/>.
    /// </remarks>
    internal sealed partial class HistoryRocksPersistPump
    {
        /// <summary>Logs persist pump startup.</summary>
        /// <param name="dbDir">RocksDB directory.</param>
        [LoggerMessage(EventId = 140, Level = LogLevel.Information, Message = "HistoryDB Rocks persist pump started for {DbDir}.")]
        private partial void LogPumpStarted(string dbDir);

        /// <summary>Logs first persist write.</summary>
        /// <param name="dbDir">RocksDB directory.</param>
        [LoggerMessage(EventId = 141, Level = LogLevel.Information,
            Message = "HistoryDB persist pump wrote first CHECK reservation to RocksDB at {DbDir}.")]
        private partial void LogFirstPersist(string dbDir);

        /// <summary>Logs persist milestone.</summary>
        /// <param name="count">Total reservations written.</param>
        [LoggerMessage(EventId = 142, Level = LogLevel.Information,
            Message = "HistoryDB persist pump has written {Count} reservations to RocksDB.")]
        private partial void LogPersistMilestone(long count);

        /// <summary>Logs per-item persist failure.</summary>
        /// <param name="exception">Failure exception.</param>
        [LoggerMessage(EventId = 143, Level = LogLevel.Error, Message = "HistoryDB failed to persist history entry to RocksDB.")]
        private partial void LogPersistItemFailed(Exception exception);

        /// <summary>Logs pump shutdown.</summary>
        [LoggerMessage(EventId = 144, Level = LogLevel.Debug, Message = "HistoryDB Rocks persist pump stopped.")]
        private partial void LogPumpStopped();

        /// <summary>Logs unexpected pump termination.</summary>
        /// <param name="exception">Fatal exception.</param>
        [LoggerMessage(EventId = 145, Level = LogLevel.Critical, Message = "HistoryDB Rocks persist pump terminated unexpectedly.")]
        private partial void LogPumpFatal(Exception exception);
    }
}
