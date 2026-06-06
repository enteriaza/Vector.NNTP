// <copyright file="EncryptionFailureClassifier.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: classifies renewal and ACME backend exceptions for structured logging.

namespace Vector.NNTP.Encryption.Configuration
{
    /// <summary>
    /// Classifies exceptions from certificate renewal I/O into stable <see cref="EncryptionFailureReason"/> values.
    /// </summary>
    internal static class EncryptionFailureClassifier
    {
        /// <summary>
        /// Classifies <paramref name="exception"/> for structured logging and diagnostics.
        /// </summary>
        /// <param name="exception">Exception raised during renewal, ACME, DNS, or cluster I/O.</param>
        /// <returns>Stable failure reason for operators and metrics.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="exception"/> is null.</exception>
        internal static EncryptionFailureReason Classify(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);

            return exception switch
            {
                OperationCanceledException => EncryptionFailureReason.Cancelled,
                TimeoutException => EncryptionFailureReason.Timeout,
                HttpRequestException => EncryptionFailureReason.HttpError,
                IOException => EncryptionFailureReason.IoError,
                _ => EncryptionFailureReason.Unknown,
            };
        }
    }

    /// <summary>
    /// Classified reason for a transient encryption backend failure.
    /// </summary>
    internal enum EncryptionFailureReason
    {
        /// <summary>
        /// Failure reason could not be determined from the exception type.
        /// </summary>
        Unknown,

        /// <summary>
        /// The operation was cancelled via <see cref="CancellationToken"/>.
        /// </summary>
        Cancelled,

        /// <summary>
        /// A network or ACME HTTP operation timed out.
        /// </summary>
        Timeout,

        /// <summary>
        /// An HTTP transport or API error occurred.
        /// </summary>
        HttpError,

        /// <summary>
        /// A filesystem or I/O error occurred.
        /// </summary>
        IoError,
    }
}
