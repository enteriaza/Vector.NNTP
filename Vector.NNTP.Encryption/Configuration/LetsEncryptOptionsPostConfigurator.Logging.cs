// <copyright file="LetsEncryptOptionsPostConfigurator.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: source-generated LoggerMessage methods for LetsEncryptOptionsPostConfigurator.

namespace Vector.NNTP.Encryption.Configuration
{
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> partial methods for
    /// <see cref="LetsEncryptOptionsPostConfigurator"/>.
    /// </summary>
    internal sealed partial class LetsEncryptOptionsPostConfigurator
    {
        /// <summary>
        /// Logs that Let's Encrypt is disabled in Development because no account key was found.
        /// </summary>
        /// <param name="accountKeyFile">Expected account key file name under CertDir.</param>
        [LoggerMessage(
            EventId = 1,
            Level = LogLevel.Warning,
            Message = "LetsEncrypt.Enabled is true but AccountKeyPem is missing and {AccountKeyFile} was not found under CertDir. Disabling automatic renewal for Development so the host can start; set LetsEncrypt.Enabled to false explicitly or supply AccountKeyPem (or place the key at CertDir/{AccountKeyFile}) for ACME.")]
        private partial void LogDevelopmentAccountKeyMissing(string accountKeyFile);
    }
}
