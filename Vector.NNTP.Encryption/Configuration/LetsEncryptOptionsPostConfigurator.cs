// <copyright file="LetsEncryptOptionsPostConfigurator.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: post-bind LetsEncrypt hydration and development-safe defaults.

namespace Vector.NNTP.Encryption.Configuration
{
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Hydrates Let's Encrypt options from disk and applies development fallbacks before <c>ValidateOnStart</c>.
    /// </summary>
    internal sealed partial class LetsEncryptOptionsPostConfigurator : IPostConfigureOptions<LetsEncryptOptions>
    {
        private readonly IHostEnvironment _hostEnvironment;
        private readonly ILogger<LetsEncryptOptionsPostConfigurator> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="LetsEncryptOptionsPostConfigurator"/> class.
        /// </summary>
        /// <param name="hostEnvironment">Hosting environment (Development vs Production).</param>
        /// <param name="logger">Logger.</param>
        public LetsEncryptOptionsPostConfigurator(
            IHostEnvironment hostEnvironment,
            ILogger<LetsEncryptOptionsPostConfigurator> logger)
        {
            this._hostEnvironment = hostEnvironment ?? throw new ArgumentNullException(nameof(hostEnvironment));
            this._logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public void PostConfigure(string? name, LetsEncryptOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            LetsEncryptOptionsConfiguration.HydrateAccountKeyFromCertDir(options);

            if (!options.Enabled || !string.IsNullOrWhiteSpace(options.AccountKeyPem))
            {
                return;
            }

            if (!this._hostEnvironment.IsDevelopment())
            {
                return;
            }

            this.LogDevelopmentAccountKeyMissing(LetsEncryptOptions.AccountKeyFileName);
            options.Enabled = false;
        }
    }
}
