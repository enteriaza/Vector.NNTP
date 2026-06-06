// <copyright file="IRabbitMqConnectionFactory.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// IRabbitMqConnectionFactory.cs -- Public abstraction for creating RabbitMQ IConnection instances.
//
// MessageBus registers an internal implementation in DI and exposes this interface so host-facing APIs can remain
// stable while construction internals evolve.

using Vector.NNTP.MessageBus.Configuration;
using RabbitMQ.Client;

namespace Vector.NNTP.MessageBus.Connections
{
    /// <summary>
    /// Creates and opens RabbitMQ <see cref="IConnection"/> instances from validated <see cref="RabbitMQOptions"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Registration:</b> Resolved from DI as a singleton in <c>ServiceCollectionExtensions.AddMessageBus</c>.</para>
    /// <para><b>Usage:</b> Implementations are used by the connection pool during startup and scale-up to establish
    /// broker TCP sessions.</para>
    /// </remarks>
    public interface IRabbitMqConnectionFactory
    {
        /// <summary>
        /// Opens a broker connection using the provided options.
        /// </summary>
        /// <param name="options">Validated RabbitMQ configuration values.</param>
        /// <param name="cancellationToken">Cancellation token for the connection attempt.</param>
        /// <returns>A task that resolves to an open connection.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is <see langword="null"/>.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled before connect completes.</exception>
        public Task<IConnection> CreateConnectionAsync(RabbitMQOptions options, CancellationToken cancellationToken = default);
    }
}
