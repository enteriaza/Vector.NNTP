// <copyright file="AcmeTransientRetry.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

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
        /// Executes <paramref name="operation"/> with exponential backoff and jitter.
        /// </summary>
        /// <typeparam name="T">Result type.</typeparam>
        /// <param name="operation">Operation to execute.</param>
        /// <param name="logger">Logger for retry diagnostics.</param>
        /// <param name="operationName">Logical operation name for logs.</param>
        /// <param name="maxAttempts">Maximum attempts including the first try.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The operation result.</returns>
        /// <exception cref="InvalidOperationException">Thrown when all attempts fail.</exception>
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

                    int delayMs = ComputeBackoffMilliseconds(attempt);
                    LogTransientAcmeRetry(logger, ex, operationName, attempt, maxAttempts, delayMs);
                    await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
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
        /// <returns>A task that completes when the operation succeeds.</returns>
        /// <exception cref="InvalidOperationException">Thrown when all attempts fail.</exception>
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
        /// The set of exceptions considered transient and worth retrying, including nested inner exceptions.
        /// </summary>
        /// <param name="ex"></param>
        /// <returns></returns>
        private static bool IsRetriable(Exception ex)
        {
            return ex is HttpRequestException or IOException or TaskCanceledException || (ex is InvalidOperationException ioe && ioe.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase)) || (ex.InnerException is not null && IsRetriable(ex.InnerException));
        }

        /// <summary>
        /// Calculates the exponential backoff time in milliseconds for a given retry attempt, including a random
        /// jitter.
        /// </summary>
        /// <param name="attemptOneBased">The one-based retry attempt number used to determine the backoff duration.</param>
        /// <returns>The computed backoff time in milliseconds, capped at 30,000 ms and including up to 249 ms of random jitter.</returns>
        private static int ComputeBackoffMilliseconds(int attemptOneBased)
        {
            int cap = 30_000;
            int exp = Math.Min(cap, 500 * (1 << Math.Min(attemptOneBased - 1, 6)));
            return exp + Random.Shared.Next(0, 250);
        }
    }
}
