// <copyright file="RetryUtilities.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// RetryUtilities.cs — Allocation-free retry delay calculation with exponential back-off and additive jitter.

using Vector.NNTP.Utilities.Async;

namespace Vector.NNTP.Utilities.Retry;

/// <summary>
/// Allocation-free retry delay calculation with exponential back-off and optional additive random jitter.
/// </summary>
/// <remarks>
/// <para><b>Algorithm:</b> <c>min(baseDelayMs × 2^(attempt−1), maxDelayMs) + random[0, jitterMaxMs)</c>.</para>
///
/// <para><b>Thread safety:</b> All methods are <see langword="static"/> and stateless. Jitter uses
/// <see cref="Random.Shared"/> which is thread-safe on .NET 8+.</para>
/// </remarks>
public static class RetryUtilities
{
    /// <summary>
    /// Maximum bit-shift exponent used to prevent <see cref="long"/> overflow in the intermediate left-shift
    /// computation.
    /// </summary>
    private const int MaxBackOffShift = 30;

    /// <summary>
    /// Calculates a retry delay with exponential back-off and optional additive random jitter.
    /// </summary>
    /// <param name="attempt">1-based attempt number. Values ≤ 0 are clamped to 1.</param>
    /// <param name="baseDelayMs">Base delay in milliseconds (non-negative).</param>
    /// <param name="maxDelayMs">Maximum delay cap in milliseconds (non-negative).</param>
    /// <param name="jitterMaxMs">Upper bound (exclusive) of uniform random jitter in milliseconds (non-negative).</param>
    /// <returns>The computed delay in milliseconds.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when any delay argument is negative.</exception>
    public static int CalculateBackOff(int attempt, int baseDelayMs, int maxDelayMs, int jitterMaxMs = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(baseDelayMs);
        ArgumentOutOfRangeException.ThrowIfNegative(maxDelayMs);
        ArgumentOutOfRangeException.ThrowIfNegative(jitterMaxMs);

        int shift = Math.Min(Math.Max(attempt - 1, 0), MaxBackOffShift);
        long delayMs = (long)baseDelayMs << shift;
        long capped = Math.Min(delayMs, maxDelayMs);

        if (jitterMaxMs > 0)
        {
            capped += Random.Shared.Next(0, jitterMaxMs);
        }

        return (int)Math.Min(capped, int.MaxValue);
    }

    /// <summary>
    /// Computes an exponential back-off delay via <see cref="CalculateBackOff"/> and awaits it with cancellation
    /// support, returning a boolean instead of throwing on cancellation.
    /// </summary>
    /// <param name="attempt">Attempt number (1-based).</param>
    /// <param name="baseDelayMs">Base delay in milliseconds.</param>
    /// <param name="maxDelayMs">Maximum delay cap in milliseconds.</param>
    /// <param name="jitterMaxMs">Jitter window in milliseconds.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><see langword="true"/> if the delay elapsed; <see langword="false"/> if cancelled.</returns>
    public static Task<bool> DelayWithBackOffAsync(int attempt, int baseDelayMs, int maxDelayMs, int jitterMaxMs, CancellationToken ct)
    {
        int delayMs = CalculateBackOff(attempt, baseDelayMs, maxDelayMs, jitterMaxMs);
        return TaskUtilities.DelayOrCancelledAsync(delayMs, ct);
    }
}
