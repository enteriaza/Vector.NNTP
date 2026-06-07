// <copyright file="EwmaUtilities.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: no LINQ; no allocations on success; no closures; no boxing; prefer Span/stackalloc; throws extracted + NoInlining where documented.
// EwmaUtilities.cs -- Exponentially weighted moving average (EWMA) blending primitives and atomic double storage helpers.

using System.Runtime.CompilerServices;

namespace Vector.NNTP.Utilities.Metrics
{
    /// <summary>
    /// Exponentially weighted moving average (EWMA) blending and lock-free <see cref="double"/> bit-pattern storage helpers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Blending uses a fixed <see cref="Alpha"/> of <c>0.3</c>:
    /// <c>ewma = (alpha * sample) + ((1 - alpha) * oldValue)</c>. Callers that maintain shared EWMA state
    /// (for example <c>NntpCpuLoadMonitor</c>) combine <see cref="BlendOrSeed"/> with
    /// <see cref="AtomicRead"/> and <see cref="AtomicWrite"/> on <see cref="long"/> fields so sampler threads
    /// can publish smoothed values without locks while accept/dispatch paths read snapshots.
    /// </para>
    /// <para>
    /// Atomic helpers encode <see cref="double"/> values with <see cref="BitConverter.DoubleToInt64Bits(double)"/>.
    /// They are intended for single-writer / multi-reader publication patterns; concurrent writers require
    /// external synchronization or a stronger atomic update strategy.
    /// </para>
    /// </remarks>
    public static class EwmaUtilities
    {
        /// <summary>
        /// EWMA smoothing weight applied to each new sample (<c>alpha</c>).
        /// </summary>
        /// <remarks>
        /// Higher values react faster to spikes; <c>0.3</c> retains roughly 70% of the prior EWMA per update.
        /// </remarks>
        public const double Alpha = 0.3;

        /// <summary>
        /// Complement of <see cref="Alpha"/> (<c>1 - alpha</c>), the weight applied to the prior EWMA value.
        /// </summary>
        public const double OneMinusAlpha = 0.7;

        /// <summary>
        /// Blends a new observation into an existing EWMA value.
        /// </summary>
        /// <param name="oldValue">Previous EWMA value (ignored when seeding via <see cref="BlendOrSeed"/>).</param>
        /// <param name="sample">New raw observation to fold into the series.</param>
        /// <returns>
        /// <c>(<see cref="Alpha"/> * <paramref name="sample"/>) + (<see cref="OneMinusAlpha"/> * <paramref name="oldValue"/>)</c>.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Blend(double oldValue, double sample)
        {
            return (Alpha * sample) + (OneMinusAlpha * oldValue);
        }

        /// <summary>
        /// Seeds an EWMA series with the first sample or blends subsequent samples.
        /// </summary>
        /// <param name="oldValue">Prior EWMA value when <paramref name="hasValue"/> is <see langword="true"/>.</param>
        /// <param name="sample">New raw observation.</param>
        /// <param name="hasValue">
        /// <see langword="false"/> on the first observation (returns <paramref name="sample"/> unchanged);
        /// <see langword="true"/> on later observations (delegates to <see cref="Blend"/>).
        /// </param>
        /// <returns>Seeded or blended EWMA value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double BlendOrSeed(double oldValue, double sample, bool hasValue)
        {
            return hasValue ? Blend(oldValue, sample) : sample;
        }

        /// <summary>
        /// Reads a <see cref="double"/> previously stored with <see cref="AtomicWrite"/> using a volatile load.
        /// </summary>
        /// <param name="bits">
        /// Field holding a <see cref="double"/> encoded as <see cref="BitConverter.DoubleToInt64Bits(double)"/>.
        /// </param>
        /// <returns>Decoded <see cref="double"/> value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double AtomicRead(ref long bits)
        {
            return BitConverter.Int64BitsToDouble(Volatile.Read(ref bits));
        }

        /// <summary>
        /// Publishes a <see cref="double"/> into a <see cref="long"/> bit-pattern field via atomic exchange.
        /// </summary>
        /// <param name="bits">
        /// Field to update; must be used consistently with <see cref="AtomicRead"/> on the same storage location.
        /// </param>
        /// <param name="value"><see cref="double"/> value to encode and store.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AtomicWrite(ref long bits, double value)
        {
            _ = Interlocked.Exchange(ref bits, BitConverter.DoubleToInt64Bits(value));
        }
    }
}
