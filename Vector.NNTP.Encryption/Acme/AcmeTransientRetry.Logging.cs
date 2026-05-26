// <copyright file="AcmeTransientRetry.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// AcmeTransientRetry.Logging.cs -- Source-generated [LoggerMessage] partial methods for AcmeTransientRetry.

namespace Vector.NNTP.Encryption.Acme
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> partial methods for <see cref="AcmeTransientRetry"/>.
    /// </summary>
    internal static partial class AcmeTransientRetry
    {
        /// <summary>
        /// Logs a transient ACME failure before retrying with exponential backoff.
        /// </summary>
        /// <param name="logger">Logger for retry diagnostics.</param>
        /// <param name="ex">The transient exception.</param>
        /// <param name="operationName">Logical operation name.</param>
        /// <param name="attempt">Current attempt number (1-based).</param>
        /// <param name="maxAttempts">Maximum attempts including the first try.</param>
        /// <param name="delayMs">Backoff delay before the next attempt.</param>
        [LoggerMessage(EventId = 10, Level = LogLevel.Warning,
            Message = "Certificates: Transient ACME failure for {OperationName} (attempt {Attempt}/{MaxAttempts}); retrying after {DelayMs}ms")]
        internal static partial void LogTransientAcmeRetry(
            ILogger logger,
            Exception ex,
            string operationName,
            int attempt,
            int maxAttempts,
            int delayMs);
    }
}
