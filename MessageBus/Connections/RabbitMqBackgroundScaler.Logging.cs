// <copyright file="RabbitMqBackgroundScaler.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// RabbitMqBackgroundScaler.Logging.cs -- Source-generated [LoggerMessage] partial methods for RabbitMqBackgroundScaler.
//
// Callers in RabbitMqBackgroundScaler.cs log scale-up success and connection-add failures.

namespace MessageBus.Connections
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> partial methods for <see cref="RabbitMqBackgroundScaler"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Event ID range:</b> 1--2 -- reserved for <see cref="RabbitMqBackgroundScaler"/>.</para>
    /// </remarks>
    public sealed partial class RabbitMqBackgroundScaler
    {

        #region Logging -- Scale Events (1-2)

        /// <summary>Logs successful TCP scale-up.</summary>
        /// <param name="connectionCount">New total connection count after scale-up.</param>
        [LoggerMessage(EventId = 1, Level = LogLevel.Information,
            Message = "Scaled TCP pool up to {ConnectionCount} connections.")]
        private partial void LogScaledUp(int connectionCount);

        /// <summary>Logs failure to add a TCP connection during scale-up.</summary>
        /// <param name="ex">Exception from <see cref="ConnectionPool.AddConnectionAsync"/>.</param>
        [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
            Message = "Background scaler failed to add TCP connection.")]
        private partial void LogScaleError(Exception ex);

        #endregion

    }
}
