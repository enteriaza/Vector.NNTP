// <copyright file="Program.MessageBus.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.MessageBus;
using Vector.NNTP.MessageBus.Configuration;
using Microsoft.Extensions.Options;

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

            _ = builder.Services.AddSingleton<IValidateOptions<RabbitMQOptions>, RabbitMQOptionsValidator>();

            _ = builder.Services
                .AddOptions<RabbitMQOptions>()
                .Bind(builder.Configuration.GetSection(RabbitMQOptions.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            _ = builder.Services.AddMessageBus();
        }
    }
}
