// <copyright file="ConnectionPool.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// ConnectionPool.Logging.cs -- Source-generated [LoggerMessage] partial methods for ConnectionPool.
//
// Callers in ConnectionPool.cs emit pool TCP lifecycle events.  Per CONTRIBUTING.md all [LoggerMessage] stubs live in
// this partial file.
//
// EventId range allocation:
//   pool: 200-219.

namespace Vector.NNTP.MessageBus.Connections
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> partial methods for <see cref="ConnectionPool"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Event ID range:</b> 200-202 -- reserved for <see cref="ConnectionPool"/>.</para>
    /// </remarks>
    internal sealed partial class ConnectionPool
    {

        #region Logging -- Pool Lifecycle (200-202)

        /// <summary>Logs addition of a pooled TCP connection.</summary>
        /// <param name="connectionId">Pool-local connection identifier.</param>
        /// <param name="hostIndex">Selected host index.</param>
        [LoggerMessage(EventId = 200, Level = LogLevel.Information,
            Message = "Added pooled connection {ConnectionId} on host index {HostIndex}.")]
        private partial void LogConnectionAdded(Guid connectionId, int hostIndex);

        /// <summary>Logs when publisher slot acquisition times out.</summary>
        /// <param name="timeout">Configured lease timeout.</param>
        [LoggerMessage(EventId = 201, Level = LogLevel.Warning,
            Message = "Publisher slot lease timed out after waiting {Timeout}.")]
        private partial void LogSlotLeaseTimeout(TimeSpan timeout);

        /// <summary>Logs when waiter wakeup exits due to caller cancellation.</summary>
        [LoggerMessage(EventId = 202, Level = LogLevel.Debug,
            Message = "Publisher slot waiter canceled while waiting for release signal.")]
        private partial void LogSlotWaitCanceled();

        #endregion

    }
}
