// <copyright file="UsenetEpoch.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: no LINQ; no allocations on success; no closures; no boxing; prefer Span/stackalloc; throws extracted + NoInlining where documented.
// UsenetEpoch.cs -- Converts UTC DateTime to Unix epoch seconds as uint; used by NewsDateParser.ParseToUnixTimestamp.
//
// Thread safety:
//   All methods are static and stateless.

namespace Vector.NNTP.Filters.DateParser
{
    /// <summary>
    /// Converts UTC <see cref="DateTime"/> values to Unix epoch seconds (<see cref="uint"/>).
    /// </summary>
    /// <remarks>
    /// <para><b>Contract:</b> Callers must pass UTC; <see cref="DateTimeKind.Local"/> triggers a debug-only assertion in
    /// <see cref="ToUnixTimestamp"/>.</para>
    ///
    /// <para><b>Performance:</b> HOT PATH — scalar arithmetic only; returns 0 for non-positive or out-of-range values.</para>
    /// </remarks>
    internal static class UsenetEpoch
    {
        /// <summary>
        /// Ticks at 1970-01-01T00:00:00Z used to subtract from UTC <see cref="DateTime"/> ticks.
        /// </summary>
        /// <remarks>Derived from the Unix epoch offset in .NET ticks (constant 621355968000000000).</remarks>
        private const long UnixEpochTicks = 621_355_968_000_000_000;

        /// <summary>
        /// Converts a UTC instant to seconds since 1970-01-01Z, or <c>0</c> when out of <see cref="uint"/> range or non-positive.
        /// </summary>
        /// <param name="utc">The instant in UTC.</param>
        /// <returns>Seconds since the Unix epoch, or <c>0</c> when the value does not fit in <see cref="uint"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ToUnixTimestamp(DateTime utc)
        {
            Debug.Assert(utc.Kind != DateTimeKind.Local, "UsenetEpoch.ToUnixTimestamp requires UTC.");

            long epoch = (utc.Ticks - UnixEpochTicks) / TimeSpan.TicksPerSecond;
            return epoch is > 0 and <= uint.MaxValue ? (uint)epoch : 0;
        }
    }
}

