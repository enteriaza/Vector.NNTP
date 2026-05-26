// <copyright file="MessageBusUnavailableException.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// MessageBusUnavailableException.cs -- Transient pool or consumer availability failure.
//
// Thrown when the connection pool is not accepting slots, waiter limits are exceeded, the consumer manager is stopped,
// or no TCP connection is available. Hosts should retry with backoff rather than treating as a configuration defect.

namespace Vector.NNTP.MessageBus.Exceptions
{
    /// <summary>
    /// Thrown when the pool is unhealthy, saturated, or has no usable TCP connections.
    /// </summary>
    /// <remarks>
    /// <para><b>Recovery:</b> Retry with exponential backoff and full jitter after transient broker or network outages.
    /// Combine with host circuit breakers when failures persist beyond SLO windows.</para>
    ///
    /// <para><b>Distinct from:</b> <see cref="MessageBusLeaseTimeoutException"/> (waited but no slot in time) and
    /// <see cref="MessageBusConfigurationException"/> (invalid static configuration).</para>
    /// </remarks>
    /// <remarks>Initializes a new instance of the <see cref="MessageBusUnavailableException"/> class.</remarks>
    /// <param name="message">Human-readable failure context for logs and metrics.</param>
    public sealed class MessageBusUnavailableException(string message) : MessageBusException(message)
    {
    }
}

