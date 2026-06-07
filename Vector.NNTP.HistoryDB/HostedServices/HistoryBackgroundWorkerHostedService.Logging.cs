// <copyright file="HistoryBackgroundWorkerHostedService.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// EventId range: 160-179.

namespace Vector.NNTP.HistoryDB.HostedServices
{
    /// <summary>
    /// Source-generated logging for <see cref="HistoryBackgroundWorkerHostedService"/>.
    /// </summary>
    /// <remarks>Event IDs 160–179 for Rocks expiration sweep diagnostics.</remarks>
    internal sealed partial class HistoryBackgroundWorkerHostedService
    {
        /// <summary>Logs sweep completion with deleted count.</summary>
        /// <param name="deleted">Keys deleted.</param>
        /// <param name="elapsedMs">Sweep duration milliseconds.</param>
        [LoggerMessage(EventId = 160, Level = LogLevel.Debug,
            Message = "HistoryDB Rocks sweep deleted {Deleted} expired keys in {ElapsedMs} ms.")]
        private partial void LogSweepCompleted(int deleted, double elapsedMs);

        /// <summary>Logs sweep failure.</summary>
        /// <param name="exception">Failure exception.</param>
        [LoggerMessage(EventId = 161, Level = LogLevel.Error, Message = "HistoryDB RocksDB sweep failed.")]
        private partial void LogSweepFailed(Exception exception);
    }
}
