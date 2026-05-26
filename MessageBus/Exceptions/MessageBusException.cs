// <copyright file="MessageBusException.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// MessageBusException.cs -- Base exception type for MessageBus operational and configuration failures.
//
// Derived types classify retryable pool saturation vs fatal configuration vs publish confirm timeouts. Hosts may catch
// MessageBusException at integration boundaries and map to application-specific error responses.

namespace MessageBus.Exceptions
{
    /// <summary>
    /// Base exception for MessageBus failures surfaced to host integration layers.
    /// </summary>
    /// <remarks>
    /// <para><b>Taxonomy:</b> Use derived types for specific recovery policies —
    /// <see cref="MessageBusUnavailableException"/> (backoff/retry),
    /// <see cref="MessageBusLeaseTimeoutException"/> (contention),
    /// <see cref="MessageBusConnectionFaultException"/> (faulted connection/channel),
    /// <see cref="MessageBusPublishConfirmTimeoutException"/> (broker slow path),
    /// <see cref="MessageBusConfigurationException"/> (fail fast at startup).</para>
    ///
    /// <para><b>Logging:</b> Hosts should log the full exception (including inner exceptions on fault types) at a severity
    /// matching the derived type's guidance.</para>
    /// </remarks>
    public class MessageBusException : Exception
    {
        /// <summary>Initializes a new instance of the <see cref="MessageBusException"/> class.</summary>
        public MessageBusException()
        {
        }

        /// <summary>Initializes a new instance of the <see cref="MessageBusException"/> class.</summary>
        /// <param name="message">Human-readable error description.</param>
        public MessageBusException(string message)
            : base(message)
        {
        }

        /// <summary>Initializes a new instance of the <see cref="MessageBusException"/> class.</summary>
        /// <param name="message">Human-readable error description.</param>
        /// <param name="innerException">Underlying cause (AMQP, DNS, or I/O failure), when present.</param>
        public MessageBusException(string message, Exception? innerException)
            : base(message, innerException)
        {
        }
    }
}

