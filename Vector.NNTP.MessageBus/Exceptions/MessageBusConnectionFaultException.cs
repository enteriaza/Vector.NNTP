// <copyright file="MessageBusConnectionFaultException.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// MessageBusConnectionFaultException.cs -- AMQP connection or channel creation fault after a slot was acquired.
//
// Wraps RabbitMQ.Client exceptions from channel open paths. The pool supervisor may replace faulted TCP connections;
// callers should not assume the same PooledConnection remains valid.

namespace Vector.NNTP.MessageBus.Exceptions
{
    /// <summary>
    /// Thrown when AMQP connection or channel operations fail after pool resources were acquired.
    /// </summary>
    /// <remarks>
    /// <para><b>Inner exception:</b> Preserves the RabbitMQ.Client fault for root-cause analysis. Log both outer and inner
    /// messages.</para>
    ///
    /// <para><b>Recovery:</b> Retry on a new scope; allow <see cref="Connections.RabbitMqPoolSupervisor"/> and background
    /// scaler to heal the TCP layer.</para>
    /// </remarks>
    /// <remarks>Initializes a new instance of the <see cref="MessageBusConnectionFaultException"/> class.</remarks>
    /// <param name="message">Human-readable failure context.</param>
    /// <param name="innerException">Underlying AMQP or I/O exception, when present.</param>
    public sealed class MessageBusConnectionFaultException(string message, Exception? innerException = null) : MessageBusException(message, innerException)
    {
    }
}

