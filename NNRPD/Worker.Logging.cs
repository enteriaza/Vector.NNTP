// <copyright file="Worker.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// Worker.Logging.cs -- Source-generated [LoggerMessage] partial methods for Worker.

namespace NNRPD
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> partial methods for <see cref="Worker"/>.
    /// </summary>
    public sealed partial class Worker
    {

        #region Logging -- Heartbeat (1)

        /// <summary>Logs a periodic heartbeat with the current time.</summary>
        /// <param name="time">Timestamp included in the log entry.</param>
        [LoggerMessage(EventId = 1, Level = LogLevel.Information,
            Message = "Worker running at: {Time:yyyy-MM-dd HH:mm:ss.fff}")]
        private partial void LogHeartbeat(DateTimeOffset time);

        #endregion

    }
}
