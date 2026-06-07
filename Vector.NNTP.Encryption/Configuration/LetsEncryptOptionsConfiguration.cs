// <copyright file="LetsEncryptOptionsConfiguration.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: hydrates LetsEncrypt options from on-disk certificate material before validation.

namespace Vector.NNTP.Encryption.Configuration
{
    /// <summary>
    /// Helpers that populate <see cref="LetsEncryptOptions"/> from files under <see cref="LetsEncryptOptions.CertDir"/>.
    /// </summary>
    internal static partial class LetsEncryptOptionsConfiguration
    {
        /// <summary>
        /// Loads <see cref="LetsEncryptOptions.AccountKeyPem"/> from
        /// <c>{CertDir}/letsencrypt.pem</c> when the property is empty and the file exists.
        /// </summary>
        /// <param name="options">Bound options instance (mutated in place).</param>
        /// <param name="logger">Optional logger for hydration failures.</param>
        /// <remarks>
        /// Best-effort hydration: I/O failures are logged when <paramref name="logger"/> is supplied and are not
        /// rethrown so post-configuration can still apply Development fallbacks and <c>ValidateOnStart</c> can surface
        /// a missing key explicitly.
        /// </remarks>
        public static void HydrateAccountKeyFromCertDir(LetsEncryptOptions options, ILogger? logger = null)
        {
            ArgumentNullException.ThrowIfNull(options);

            if (!string.IsNullOrWhiteSpace(options.AccountKeyPem))
            {
                return;
            }

            string certDir = options.CertDir?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(certDir))
            {
                return;
            }

            string accountKeyPath = Path.Combine(certDir, LetsEncryptOptions.AccountKeyFileName);
            if (!File.Exists(accountKeyPath))
            {
                return;
            }

            try
            {
                options.AccountKeyPem = File.ReadAllText(accountKeyPath);
            }
            catch (Exception ex)
            {
                if (logger is not null)
                {
                    LogAccountKeyHydrationFailed(logger, accountKeyPath, ex);
                }
            }
        }
    }
}
