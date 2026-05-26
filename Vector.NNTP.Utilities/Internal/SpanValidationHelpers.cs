// <copyright file="SpanValidationHelpers.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// SpanValidationHelpers.cs -- Zero-allocation span length checks for hot-path encode/write helpers.
//
// HOT PATH: no allocations on success; delegates throws to ThrowHelpers.
//
// Thread safety:
//   All methods are static and stateless. Safe for concurrent use from any thread.

using System.Runtime.CompilerServices;

namespace Vector.NNTP.Utilities.Internal
{
    /// <summary>
    /// Span length validation helpers that keep success paths allocation-free.
    /// </summary>
    /// <remarks>
    /// <para><b>Performance:</b> HOT PATH — inlined length comparison on success; throws isolated in
    /// <see cref="ThrowHelpers"/>.</para>
    ///
    /// <para><b>Thread safety:</b> All members are <see langword="static"/> and stateless.</para>
    /// </remarks>
    internal static class SpanValidationHelpers
    {
        /// <summary>
        /// Validates that a destination span meets the required length, throwing when it does not.
        /// </summary>
        /// <param name="requiredLength">Minimum required length.</param>
        /// <param name="actualLength">Actual destination length.</param>
        /// <param name="paramName">Parameter name for the thrown exception.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void EnsureDestinationLength(int requiredLength, int actualLength, string paramName)
        {
            if (actualLength < requiredLength)
            {
                ThrowHelpers.DestinationTooShort(requiredLength, actualLength, paramName);
            }
        }
    }
}
