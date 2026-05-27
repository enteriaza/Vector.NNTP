// <copyright file="LetsEncryptOptionsPostConfigurator.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: post-bind LetsEncrypt hydration and development-safe defaults.

namespace Vector.NNTP.Encryption.Configuration
{
    /// <summary>
    /// Hydrates Let's Encrypt options from disk and applies development fallbacks before <c>ValidateOnStart</c>.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="LetsEncryptOptionsPostConfigurator"/> class.
    /// </remarks>
    /// <param name="hostEnvironment">Hosting environment (Development vs Production).</param>
    /// <param name="logger">Logger.</param>
    internal sealed partial class LetsEncryptOptionsPostConfigurator(
        IHostEnvironment hostEnvironment,
        ILogger<LetsEncryptOptionsPostConfigurator> logger) : IPostConfigureOptions<LetsEncryptOptions>
    {
        private readonly IHostEnvironment _hostEnvironment = hostEnvironment ?? throw new ArgumentNullException(nameof(hostEnvironment));
        private readonly ILogger<LetsEncryptOptionsPostConfigurator> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        /// <inheritdoc />
        public void PostConfigure(string? name, LetsEncryptOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            LetsEncryptOptionsConfiguration.HydrateAccountKeyFromCertDir(options);

            if (!options.Enabled || !string.IsNullOrWhiteSpace(options.AccountKeyPem))
            {
                return;
            }

            if (!_hostEnvironment.IsDevelopment())
            {
                return;
            }

            LogDevelopmentAccountKeyMissing(LetsEncryptOptions.AccountKeyFileName);
            options.Enabled = false;
        }
    }
}
