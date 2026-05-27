// <copyright file="EwmaUtilities.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: no LINQ; no allocations on success; no closures; no boxing; prefer Span/stackalloc; throws extracted + NoInlining where documented.
// EwmaUtilities.cs -- Exponentially weighted moving average (EWMA) blending primitives and atomic double storage helpers.

using System.Runtime.CompilerServices;

namespace Vector.NNTP.Utilities.Metrics
{
    /// <summary>
    /// Exponentially weighted moving average (EWMA) blending primitives and lock-free atomic <see cref="double"/> storage.
    /// </summary>
    public static class EwmaUtilities
    {
        /// <summary>
        /// EWMA blending factor (alpha).
        /// </summary>
        public const double Alpha = 0.3;

        /// <summary>
        /// Complement of <see cref="Alpha"/> (<c>1 - alpha</c>).
        /// </summary>
        public const double OneMinusAlpha = 0.7;

        /// <summary>
        /// Blends a new sample into an existing EWMA value.
        /// </summary>
        /// <param name="oldValue">Previous EWMA value.</param>
        /// <param name="sample">New observation.</param>
        /// <returns>Blended EWMA value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Blend(double oldValue, double sample)
        {
            return (Alpha * sample) + (OneMinusAlpha * oldValue);
        }

        /// <summary>
        /// Blends a new sample into an EWMA, seeding the series when <paramref name="hasValue"/> is <see langword="false"/>.
        /// </summary>
        /// <param name="oldValue">Previous EWMA value.</param>
        /// <param name="sample">New observation.</param>
        /// <param name="hasValue">Whether the series has been seeded.</param>
        /// <returns>Seeded or blended value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double BlendOrSeed(double oldValue, double sample, bool hasValue)
        {
            return hasValue ? Blend(oldValue, sample) : sample;
        }

        /// <summary>
        /// Reads a lock-free atomically stored <see cref="double"/> from a <see cref="long"/> bit-pattern field.
        /// </summary>
        /// <param name="bits">The field containing <see cref="BitConverter.DoubleToInt64Bits(double)"/>.</param>
        /// <returns>The decoded <see cref="double"/> value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double AtomicRead(ref long bits)
        {
            return BitConverter.Int64BitsToDouble(Volatile.Read(ref bits));
        }

        /// <summary>
        /// Writes a lock-free atomically stored <see cref="double"/> into a <see cref="long"/> bit-pattern field.
        /// </summary>
        /// <param name="bits">The field to write.</param>
        /// <param name="value">The value to store.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AtomicWrite(ref long bits, double value)
        {
            _ = Interlocked.Exchange(ref bits, BitConverter.DoubleToInt64Bits(value));
        }
    }
}
