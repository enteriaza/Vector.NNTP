// <copyright file="ConnectionPool.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// ConnectionPool.Logging.cs -- Source-generated [LoggerMessage] partial methods for ConnectionPool.
//
// Callers in ConnectionPool.cs emit pool TCP lifecycle events.  Per CONTRIBUTING.md all [LoggerMessage] stubs live in
// this partial file.

namespace MessageBus.Connections
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> partial methods for <see cref="ConnectionPool"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Event ID range:</b> 1 -- reserved for <see cref="ConnectionPool"/>.</para>
    /// </remarks>
    public sealed partial class ConnectionPool
    {

        #region Logging -- Pool Lifecycle (1)

        /// <summary>Logs addition of a pooled TCP connection.</summary>
        /// <param name="connectionId">Pool-local connection identifier.</param>
        /// <param name="hostIndex">Selected host index.</param>
        [LoggerMessage(EventId = 1, Level = LogLevel.Information,
            Message = "Added pooled connection {ConnectionId} on host index {HostIndex}.")]
        private partial void LogConnectionAdded(Guid connectionId, int hostIndex);

        #endregion

    }
}
