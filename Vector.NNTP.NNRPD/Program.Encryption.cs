// <copyright file="Program.Encryption.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Options;
using Vector.NNTP.Encryption.Configuration;
using Vector.NNTP.Encryption.DependencyInjection;
using EncryptionNntpServerOptions = Vector.NNTP.Encryption.Configuration.NntpServerOptions;

namespace Vector.NNTP.NNRPD
{
    /// <summary>
    /// TLS certificate renewal (ACME) host configuration.
    /// </summary>
    public partial class Program
    {
        /// <summary>
        /// Binds Encryption options from configuration and registers certificate renewal services.
        /// </summary>
        /// <param name="builder">Host builder.</param>
        private static void ConfigureEncryption(HostApplicationBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            _ = builder.Services.AddSingleton<IValidateOptions<LetsEncryptOptions>, LetsEncryptOptionsValidator>();

            _ = builder.Services
                .AddOptions<LetsEncryptOptions>()
                .Bind(builder.Configuration.GetSection(LetsEncryptOptions.SectionName))
                .ValidateOnStart();

            _ = builder.Services
                .AddOptions<EncryptionNntpServerOptions>()
                .Bind(builder.Configuration.GetSection(EncryptionNntpServerOptions.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            _ = builder.Services.AddEncryption();
        }
    }
}
