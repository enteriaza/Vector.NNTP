// <copyright file="LetsEncryptOptionsPostConfigurator.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: post-bind LetsEncrypt hydration and development-safe defaults.

namespace Vector.NNTP.Encryption.Configuration
{
    /// <summary>
    /// Hydrates Let's Encrypt options from disk and applies development fallbacks before <c>ValidateOnStart</c>.
    /// </summary>
    /// <param name="hostEnvironment">Hosting environment (Development vs Production).</param>
    /// <param name="logger">Logger for development fallback and hydration diagnostics.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="hostEnvironment"/> or <paramref name="logger"/> is <see langword="null"/>.
    /// </exception>
    internal sealed partial class LetsEncryptOptionsPostConfigurator(
        IHostEnvironment hostEnvironment,
        ILogger<LetsEncryptOptionsPostConfigurator> logger) : IPostConfigureOptions<LetsEncryptOptions>
    {
        /// <summary>
        /// Hosting environment used to detect Development and disable ACME when no account key is configured.
        /// </summary>
        private readonly IHostEnvironment _hostEnvironment = hostEnvironment ?? throw new ArgumentNullException(nameof(hostEnvironment));

        /// <summary>
        /// Hydrates account-key PEM from disk and applies Development-safe ACME disablement before validation.
        /// </summary>
        /// <param name="name">Options name (unused).</param>
        /// <param name="options">Bound options instance mutated in place.</param>
        /// <remarks>
        /// <para>
        /// Always calls <see cref="LetsEncryptOptionsConfiguration.HydrateAccountKeyFromCertDir"/> first. When
        /// <see cref="IHostEnvironment.EnvironmentName"/> is <c>Development</c> and <see cref="LetsEncryptOptions.AccountKeyPem"/> is
        /// still empty, sets <see cref="LetsEncryptOptions.Enabled"/> to <see langword="false"/> so local dev hosts
        /// start without production ACME credentials.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
        void IPostConfigureOptions<LetsEncryptOptions>.PostConfigure(string? name, LetsEncryptOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            LetsEncryptOptionsConfiguration.HydrateAccountKeyFromCertDir(options, logger);

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
