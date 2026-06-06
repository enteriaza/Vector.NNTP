// <copyright file="HistoryGenerationStore.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// EventId range: 220-229.

namespace Vector.NNTP.HistoryDB.Services
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> partial methods for <see cref="HistoryGenerationStore"/>.
    /// </summary>
    /// <remarks>
    /// Implementations bind to the primary-constructor <c>logger</c> parameter from
    /// <see cref="HistoryGenerationStore"/>.
    /// </remarks>
    internal sealed partial class HistoryGenerationStore
    {
        /// <summary>Logs generation allocation.</summary>
        /// <param name="generation">Allocated generation stamp.</param>
        /// <param name="path">Generation file path.</param>
        [LoggerMessage(EventId = 220, Level = LogLevel.Information,
            Message = "HistoryDB allocated rebuild generation {Generation} at {Path}.")]
        private partial void LogGenerationAllocated(ulong generation, string path);

        /// <summary>Logs generation file I/O failure.</summary>
        /// <param name="exception">I/O exception.</param>
        /// <param name="path">Generation file path.</param>
        [LoggerMessage(EventId = 221, Level = LogLevel.Error,
            Message = "HistoryDB generation file I/O failed at {Path}.")]
        private partial void LogGenerationIoFailed(Exception exception, string path);
    }
}
