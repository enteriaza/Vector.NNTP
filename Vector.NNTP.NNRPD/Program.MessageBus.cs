// <copyright file="Program.MessageBus.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.MessageBus.Configuration;
using Vector.NNTP.MessageBus.DependencyInjection;

namespace Vector.NNTP.NNRPD
{
    /// <summary>
    /// RabbitMQ MessageBus host configuration (reads JSON; library does not).
    /// </summary>
    public partial class Program
    {
        /// <summary>
        /// Binds <see cref="RabbitMQOptions"/> from configuration and registers MessageBus services.
        /// </summary>
        /// <param name="builder">Host builder.</param>
        private static void ConfigureMessageBus(HostApplicationBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            _ = builder.Services
                .AddOptions<RabbitMQOptions>()
                .Bind(builder.Configuration.GetSection(RabbitMQOptions.SectionName))
                .ValidateOnStart();

            _ = builder.Services.AddMessageBus();
        }
    }
}
