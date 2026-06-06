// <copyright file="LetsEncryptOptionsConfiguration.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// LetsEncryptOptionsConfiguration.Logging.cs -- Source-generated [LoggerMessage] static partial methods.

namespace Vector.NNTP.Encryption.Configuration
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> partial methods for
    /// <see cref="LetsEncryptOptionsConfiguration"/>.
    /// </summary>
    internal static partial class LetsEncryptOptionsConfiguration
    {
        /// <summary>
        /// Logs that the on-disk ACME account key could not be read during options hydration.
        /// </summary>
        /// <param name="logger">Logger for options hydration diagnostics.</param>
        /// <param name="accountKeyPath">Path to the account key file under CertDir.</param>
        /// <param name="ex">The exception observed while reading the file.</param>
        [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
            Message = "Certificates: Failed to hydrate LetsEncrypt AccountKeyPem from {AccountKeyPath}")]
        internal static partial void LogAccountKeyHydrationFailed(ILogger logger, string accountKeyPath, Exception ex);
    }
}
