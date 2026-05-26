// RetryUtilities.cs — Allocation-free retry delay calculation with exponential back-off and additive jitter.
//
// Provides static methods for computing capped exponential delays with optional uniform random jitter,
// and a convenience async delay method that combines back-off calculation with cancellable waiting.
// Designed for reconnection and startup-retry loops where multiple instances recover simultaneously
// after a broker restart — the additive jitter window de-correlates retry waves to prevent thundering herds.
//
// Algorithm:  min(baseDelayMs × 2^(attempt−1), maxDelayMs) + uniform_random[0, jitterMaxMs)
//
// Overflow safety:
//   The base delay is cast to long before left-shifting, and the shift exponent is capped at 30.  The
//   worst-case intermediate value is (long)int.MaxValue << 30 ≈ 2.3 × 10^18, which is within the long
//   range (9.2 × 10^18).  The subsequent Math.Min(long, long) cap brings the value back to int range.
//   The final jitter addition is also performed in long to prevent int overflow when maxDelayMs + jitter
//   exceeds int.MaxValue.
//
// Thread safety:
//   All methods are static with no shared mutable state.  Jitter uses Random.Shared which is thread-safe
//   on .NET 8+ (backed by per-thread XoshiroImpl — lock-free).
//
// Cross-platform:
//   Fully portable.  Random.Shared, Math.Min, Math.Max, and ArgumentOutOfRangeException.ThrowIfNegative
//   are BCL APIs available on all .NET 8 runtimes (Windows x64, Linux x64).  No P/Invoke, no OS-specific
//   APIs.  Random.Shared uses thread-local XoshiroImpl on all platforms.
//
// SIMD applicability:
//   Not applicable.  The methods perform scalar integer arithmetic (two comparisons, one left-shift, one
//   optional Random.Shared.Next call) — there are no loops or batch operations that would benefit from
//   vector intrinsics.
//
// Allocation: Zero on all code paths in CalculateBackOff.  DelayWithBackOffAsync allocates one async state
//   machine (~100 bytes) only when the delay is genuinely asynchronous; zero allocation on the fast paths
//   (already cancelled, zero delay).
//
// Consumers:
//   ConnectionPool.StartingAsync            — RabbitMQ connection retry (2 s base, 30 s cap, 1 s jitter).
//   CertificateRenewalService.ExecuteAsync         — ACME startup retry (30 s base, 5 min cap, no jitter).

namespace MessageBus.Utilities
{
    /// <summary>
    /// Allocation-free retry delay calculation with exponential back-off and optional additive random jitter.
    /// </summary>
    /// <remarks>
    /// <para><b>Algorithm:</b> <c>min(baseDelayMs × 2^(attempt−1), maxDelayMs) + random[0, jitterMaxMs)</c>.</para>
    ///
    /// <para><b>Overflow safety:</b> The base delay is cast to <see cref="long"/> before left-shifting, and the shift
    /// exponent is capped at <see cref="MaxBackOffShift"/> (30).  The worst-case intermediate value is
    /// <c>(long)int.MaxValue &lt;&lt; 30 ≈ 2.3 × 10^{18}</c>, which is within the <see cref="long"/> range
    /// (<c>9.2 × 10^{18}</c>).  The subsequent <see cref="Math.Min(long, long)"/> cap brings the value back to
    /// <see cref="int"/> range before the cast.  The final jitter addition is performed in <see cref="long"/>
    /// arithmetic and clamped to <see cref="int.MaxValue"/> to prevent overflow when <c>maxDelayMs + jitterMaxMs</c>
    /// exceeds <see cref="int.MaxValue"/>.</para>
    ///
    /// <para><b>Thread safety:</b> All methods are <see langword="static"/> with no shared mutable state.  Jitter uses
    /// <see cref="Random.Shared"/> which is thread-safe on .NET 8+ — backed by a per-thread <c>XoshiroImpl</c>
    /// that requires no locking.</para>
    ///
    /// <para><b>Cross-platform:</b> Fully portable.  <see cref="Random.Shared"/>, <see cref="Math.Min(long, long)"/>,
    /// <see cref="Math.Max(int, int)"/>, and
    /// <c>ArgumentOutOfRangeException.ThrowIfNegative</c> are BCL APIs available on all .NET 8
    /// runtimes (Windows x64, Linux x64).  No P/Invoke, no OS-specific APIs.</para>
    ///
    /// <para><b>Allocation:</b> <see cref="CalculateBackOff"/> has zero allocations on all code paths.
    /// <see cref="Random.Shared"/> is lock-free on .NET 8 (uses thread-local <c>XoshiroImpl</c> under the hood).
    /// <see cref="DelayWithBackOffAsync"/> allocates one async state machine (~100 bytes) only when the delay is
    /// genuinely asynchronous; the already-cancelled and zero-delay fast paths in
    /// <see cref="TaskUtilities.DelayOrCancelledAsync(int, CancellationToken)"/> are allocation-free.</para>
    ///
    /// <para><b>Input validation:</b> All public entry points validate inputs eagerly via
    /// <c>ArgumentOutOfRangeException.ThrowIfNegative</c>.  Negative delays would produce
    /// incorrect back-off behaviour, and a negative <c>maxDelayMs</c> would cause <see cref="Task.Delay(int)"/> to
    /// throw at the call site — catching the error at the source provides a more descriptive exception and prevents
    /// the invalid value from propagating through the delay pipeline.</para>
    ///
    /// <para><b>SIMD applicability:</b> Not applicable.  The methods perform scalar integer comparisons, one
    /// left-shift, and one optional <see cref="Random.Shared"/> call — there are no loops or batch operations that
    /// would benefit from vector intrinsics.</para>
    /// </remarks>
    internal static class RetryUtilities
    {

        #region Constants

        /// <summary>
        /// Maximum bit-shift exponent to prevent <see cref="long"/> overflow when left-shifting the base delay.
        /// </summary>
        /// <remarks>
        /// <para><b>Derivation:</b> 30.  The worst-case product <c>(long)int.MaxValue &lt;&lt; 30</c> is
        /// <c>2,305,843,007,066,210,304</c> — safely within the <see cref="long.MaxValue"/> of
        /// <c>9,223,372,036,854,775,807</c>.  A shift of 31 with <c>int.MaxValue</c> would produce
        /// <c>4,611,686,014,132,420,608</c> — still within <see cref="long"/> range but unnecessarily close to the
        /// limit.  30 provides a comfortable 4× margin.</para>
        ///
        /// <para><b>Practical impact:</b> At a 2 s base delay, the delay reaches the 30 s cap after attempt 5
        /// (<c>2 s × 2^4 = 32 s &gt; 30 s</c>), well before shift 30 is relevant.  At a 30 s base delay, the
        /// delay reaches the 5 min cap after attempt 4 (<c>30 s × 2^3 = 240 s &lt; 300 s</c>, <c>30 s × 2^4 = 480 s &gt; 300 s</c>).
        /// The shift cap is therefore a safety net against misuse, not a value reached in normal operation.</para>
        /// </remarks>
        private const int MaxBackOffShift = 30;

        #endregion

        #region Public Methods

        /// <summary>
        /// Calculates a retry delay with exponential back-off and optional additive random jitter.
        /// </summary>
        /// <param name="attempt">The 1-based attempt number.  Attempt 1 uses shift 0 (1× base delay), attempt 2
        /// uses shift 1 (2× base delay), and so on.  Values ≤ 0 are clamped to 1 (no shift) to prevent
        /// undefined left-shift behaviour from negative shift counts.</param>
        /// <param name="baseDelayMs">Base delay in milliseconds (e.g. 2_000).  Must be non-negative.  A value
        /// of 0 produces a zero delay (plus any jitter).</param>
        /// <param name="maxDelayMs">Maximum delay cap in milliseconds (e.g. 30_000).  Must be non-negative.
        /// The exponential delay is clamped to this value before jitter is added.</param>
        /// <param name="jitterMaxMs">Upper bound (exclusive) of the uniform random jitter in milliseconds
        /// (e.g. 1_000).  Pass 0 to disable jitter.  Must be non-negative.</param>
        /// <returns>The computed delay in milliseconds, including jitter.  Always non-negative.  May exceed
        /// <paramref name="maxDelayMs"/> by up to <paramref name="jitterMaxMs"/> − 1 ms — this is intentional
        /// so that jitter remains effective even when the exponential delay has reached the cap.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="baseDelayMs"/>,
        /// <paramref name="maxDelayMs"/>, or <paramref name="jitterMaxMs"/> is negative.</exception>
        /// <remarks>
        /// <para><b>Formula:</b>
        /// <c>min(baseDelayMs × 2^(clamp(attempt, 1, ∞) − 1), maxDelayMs) + uniform_random[0, jitterMaxMs)</c>.</para>
        ///
        /// <para><b>Attempt clamping:</b> The <paramref name="attempt"/> parameter is clamped to a minimum of 1
        /// before computing the shift exponent.  Without this clamp, <c>attempt = 0</c> would produce
        /// <c>shift = -1</c>.  C# left-shift on <see cref="long"/> uses only the low 6 bits of the shift count
        /// (C# specification §12.11), so <c>&lt;&lt; -1</c> becomes <c>&lt;&lt; 63</c> — producing an
        /// astronomically large intermediate value that would be capped to <paramref name="maxDelayMs"/>.  While
        /// the cap prevents an incorrect result, the behaviour is surprising and wastes the meaning of attempt 0.
        /// Clamping to 1 ensures <c>attempt ≤ 0</c> behaves identically to the first attempt (1× base
        /// delay).</para>
        ///
        /// <para><b>Shift cap:</b> The shift exponent is further capped at <see cref="MaxBackOffShift"/> (30)
        /// to prevent <see cref="long"/> overflow in the intermediate <c>(long)baseDelayMs &lt;&lt; shift</c>
        /// computation.  See <see cref="MaxBackOffShift"/> remarks for the overflow safety proof.</para>
        ///
        /// <para><b>Jitter design — additive after cap:</b> The jitter is added <em>after</em> the
        /// <see cref="Math.Min(long, long)"/> cap rather than being included in the cap computation.  This is
        /// intentional: if jitter were capped, the total delay at the ceiling would cluster tightly around
        /// <paramref name="maxDelayMs"/>, defeating the purpose of jitter (de-correlating concurrent retries).
        /// The uncapped additive jitter ensures retries remain uniformly spread across a
        /// <paramref name="jitterMaxMs"/>-wide window even at the delay ceiling.</para>
        ///
        /// <para><b>Overflow-safe jitter addition:</b> The capped delay and jitter are summed in
        /// <see cref="long"/> arithmetic and clamped to <see cref="int.MaxValue"/> before the final
        /// <see cref="int"/> cast.  This prevents integer overflow when <c>maxDelayMs + jitterMaxMs - 1</c>
        /// exceeds <see cref="int.MaxValue"/> — a scenario that is unreachable with current callers
        /// (<c>30_000 + 1_000</c>) but possible if the method is reused with larger values.</para>
        ///
        /// <para><b>Thread safety:</b> Uses <see cref="Random.Shared"/> which is thread-safe on .NET 8+ —
        /// backed by a per-thread <c>XoshiroImpl</c> that requires no locking.</para>
        /// </remarks>
        public static int CalculateBackOff(int attempt, int baseDelayMs, int maxDelayMs, int jitterMaxMs = 0)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(baseDelayMs);
            ArgumentOutOfRangeException.ThrowIfNegative(maxDelayMs);
            ArgumentOutOfRangeException.ThrowIfNegative(jitterMaxMs);
            // Clamp attempt to a minimum of 1 to prevent undefined left-shift behaviour from negative shift
            // counts.  See remarks for the C# left-shift semantics that make attempt ≤ 0 produce surprising
            // results without this guard.
            int shift = Math.Min(Math.Max(attempt - 1, 0), MaxBackOffShift);
            long delayMs = (long)baseDelayMs << shift;
            long capped = Math.Min(delayMs, maxDelayMs);
            // Jitter addition in long arithmetic to prevent int overflow when maxDelayMs + jitterMaxMs > int.MaxValue.
            // The Math.Min clamp guarantees the final cast to int is safe.
            if (jitterMaxMs > 0)
                capped += Random.Shared.Next(0, jitterMaxMs);
            return (int)Math.Min(capped, int.MaxValue);
        }

        /// <summary>
        /// Computes an exponential back-off delay via <see cref="CalculateBackOff"/> and awaits it with cancellation
        /// support.  Convenience method that eliminates the repeated <c>CalculateBackOff</c> → <c>Task.Delay</c>
        /// boilerplate at retry call sites.
        /// </summary>
        /// <param name="attempt">The 1-based attempt number.  Forwarded to
        /// <see cref="CalculateBackOff"/>.</param>
        /// <param name="baseDelayMs">Base delay in milliseconds.  Forwarded to
        /// <see cref="CalculateBackOff"/>.</param>
        /// <param name="maxDelayMs">Maximum delay cap in milliseconds.  Forwarded to
        /// <see cref="CalculateBackOff"/>.</param>
        /// <param name="jitterMaxMs">Upper bound (exclusive) of the uniform random jitter in milliseconds.
        /// Forwarded to <see cref="CalculateBackOff"/>.  Pass 0 to disable jitter.</param>
        /// <param name="ct">Cancellation token.  When cancelled, the delay terminates early and the method
        /// returns <see langword="false"/>.</param>
        /// <returns><see langword="true"/> if the full delay elapsed; <see langword="false"/> if cancellation was
        /// requested before the delay completed.  The caller should check this value and exit the retry loop
        /// when <see langword="false"/> is returned.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="baseDelayMs"/>,
        /// <paramref name="maxDelayMs"/>, or <paramref name="jitterMaxMs"/> is negative.</exception>
        /// <remarks>
        /// <para><b>Delegation:</b> Computes the delay via <see cref="CalculateBackOff"/> and forwards it to
        /// <see cref="TaskUtilities.DelayOrCancelledAsync(int, CancellationToken)"/>, which absorbs
        /// <see cref="OperationCanceledException"/> and returns a boolean instead of throwing.  This avoids
        /// duplicating the <c>try</c>/<c>catch (OperationCanceledException)</c> boilerplate at every retry
        /// call site.</para>
        ///
        /// <para><b>Computed delay access:</b> Callers that need the computed delay value for logging
        /// which logs the delay in the warning message) should continue to call <see cref="CalculateBackOff"/>
        /// directly and pass the result to <see cref="TaskUtilities.DelayOrCancelledAsync(int, CancellationToken)"/>.</para>
        ///
        /// <para><b>Allocation:</b> Delegates to
        /// <see cref="TaskUtilities.DelayOrCancelledAsync(int, CancellationToken)"/> which has zero allocation
        /// on already-cancelled and zero-delay fast paths, and one async state machine (~100 bytes) for genuinely
        /// asynchronous delays.</para>
        ///
        /// <para><b>Thread safety:</b> Inherits thread safety from <see cref="CalculateBackOff"/> (stateless,
        /// <see cref="Random.Shared"/> is per-thread) and <see cref="Task.Delay(int, CancellationToken)"/>
        /// (BCL-provided, thread-safe).</para>
        /// </remarks>
        public static Task<bool> DelayWithBackOffAsync(int attempt, int baseDelayMs, int maxDelayMs, int jitterMaxMs, CancellationToken ct)
        {
            int delayMs = CalculateBackOff(attempt, baseDelayMs, maxDelayMs, jitterMaxMs);
            return TaskUtilities.DelayOrCancelledAsync(delayMs, ct);
        }

        #endregion

    }
}
