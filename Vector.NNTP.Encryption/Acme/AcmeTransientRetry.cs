// <copyright file="AcmeTransientRetry.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.Utilities.Async;
using Vector.NNTP.Utilities.Retry;

namespace Vector.NNTP.Encryption.Acme
{
    /// <summary>
    /// Retries ACME operations on common transient failures (HTTP timeouts, I/O errors).
    /// </summary>
    /// <remarks>
    /// <para><b>Logging:</b> <see cref="LoggerMessageAttribute"/> partial methods in
    /// <c>AcmeTransientRetry.Logging.cs</c>.</para>
    /// </remarks>
    internal static partial class AcmeTransientRetry
    {
        /// <summary>
        /// Base delay in milliseconds for transient ACME retry back-off.
        /// </summary>
        private const int BackoffBaseDelayMs = 500;

        /// <summary>
        /// Maximum delay cap in milliseconds for transient ACME retry back-off.
        /// </summary>
        private const int BackoffMaxDelayMs = 30_000;

        /// <summary>
        /// Exclusive upper bound of uniform jitter added to each retry delay.
        /// </summary>
        private const int BackoffJitterMaxMs = 250;

        /// <summary>
        /// Executes <paramref name="operation"/> with exponential backoff and jitter.
        /// </summary>
        /// <typeparam name="T">Result type.</typeparam>
        /// <param name="operation">Operation to execute.</param>
        /// <param name="logger">Logger for retry diagnostics.</param>
        /// <param name="operationName">Logical operation name for logs.</param>
        /// <param name="maxAttempts">Maximum attempts including the first try.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Result from the first successful <paramref name="operation"/> invocation.</returns>
        /// <exception cref="InvalidOperationException">Thrown when all attempts fail or the last failure is not retriable.</exception>
        /// <remarks>
        /// <see cref="OperationCanceledException"/> propagates immediately when <paramref name="cancellationToken"/> is
        /// signalled; retriable failures are logged and delayed with jittered exponential back-off.
        /// </remarks>
        public static async Task<T> ExecuteAsync<T>(
            Func<Task<T>> operation,
            ILogger logger,
            string operationName,
            int maxAttempts,
            CancellationToken cancellationToken)
        {
            Exception? last = null;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    return await operation().ConfigureAwait(false);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested && IsRetriable(ex))
                {
                    last = ex;
                    if (attempt >= maxAttempts)
                    {
                        break;
                    }

                    int delayMs = RetryUtilities.CalculateBackOff(
                        attempt,
                        BackoffBaseDelayMs,
                        BackoffMaxDelayMs,
                        BackoffJitterMaxMs);
                    LogTransientAcmeRetry(logger, ex, operationName, attempt, maxAttempts, delayMs);
                    bool delayed = await TaskUtilities.DelayOrCancelledAsync(delayMs, cancellationToken).ConfigureAwait(false);
                    if (!delayed)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                }
            }

            throw new InvalidOperationException($"ACME operation '{operationName}' failed after {maxAttempts} attempts.", last);
        }

        /// <summary>
        /// Executes <paramref name="operation"/> with exponential backoff and jitter.
        /// </summary>
        /// <param name="operation">Operation to execute.</param>
        /// <param name="logger">Logger for retry diagnostics.</param>
        /// <param name="operationName">Logical operation name for logs.</param>
        /// <param name="maxAttempts">Maximum attempts including the first try.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that completes after <paramref name="operation"/> succeeds without throwing.</returns>
        /// <exception cref="InvalidOperationException">Thrown when all attempts fail or the last failure is not retriable.</exception>
        public static async Task ExecuteAsync(
            Func<Task> operation,
            ILogger logger,
            string operationName,
            int maxAttempts,
            CancellationToken cancellationToken)
        {
            _ = await ExecuteAsync(
                async () =>
                {
                    await operation().ConfigureAwait(false);
                    return true;
                },
                logger,
                operationName,
                maxAttempts,
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Determines whether <paramref name="ex"/> represents a transient ACME or transport failure worth retrying.
        /// </summary>
        /// <param name="ex">Observed exception from an ACME operation attempt.</param>
        /// <returns>
        /// <see langword="true"/> for <see cref="HttpRequestException"/>, <see cref="IOException"/>,
        /// <see cref="TaskCanceledException"/> (excluding host cancellation), <see cref="InvalidOperationException"/>
        /// messages containing <c>timeout</c>, or any retriable inner exception; otherwise <see langword="false"/>.
        /// </returns>
        private static bool IsRetriable(Exception ex)
        {
            return ex is HttpRequestException or IOException or TaskCanceledException || (ex is InvalidOperationException ioe && ioe.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase)) || (ex.InnerException is not null && IsRetriable(ex.InnerException));
        }

    }
}
