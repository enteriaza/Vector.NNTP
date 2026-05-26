// <copyright file="MessageBusConfigurationException.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// MessageBusConfigurationException.cs -- Invalid RabbitMQOptions or MessageBus DI registration at startup.
//
// Fail-fast signal for hosts: do not retry indefinitely. Fix configuration, secrets, or AddMessageBus ordering.

namespace MessageBus.Exceptions
{
    /// <summary>
    /// Thrown when RabbitMQ configuration or MessageBus DI registration is invalid.
    /// </summary>
    /// <remarks>
    /// <para><b>Recovery:</b> Fail the host start — correct <see cref="Configuration.RabbitMQOptions"/> binding, validation,
    /// or call <see cref="ServiceCollectionExtensions.AddMessageBus(Microsoft.Extensions.DependencyInjection.IServiceCollection)"/>
    /// only after <c>AddOptions&lt;RabbitMQOptions&gt;().ValidateOnStart()</c>.</para>
    ///
    /// <para><b>Distinct from:</b> Runtime <see cref="MessageBusUnavailableException"/> during broker outages.</para>
    /// </remarks>
    /// <remarks>Initializes a new instance of the <see cref="MessageBusConfigurationException"/> class.</remarks>
    /// <param name="message">Validation failure description suitable for operator logs.</param>
    public sealed class MessageBusConfigurationException(string message) : MessageBusException(message)
    {
    }
}

