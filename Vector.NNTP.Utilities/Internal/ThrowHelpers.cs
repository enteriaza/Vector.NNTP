// <copyright file="ThrowHelpers.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// ThrowHelpers.cs -- Centralized [DoesNotReturn] throw methods for duplicated Utilities validation failures.
//
// HOT PATH: throws are isolated with NoInlining so callers remain eligible for JIT inlining.
// COLD PATH: not used for constructor or I/O entry validation (prefer BCL throw helpers there).
//
// Thread safety:
//   All methods are static and stateless. Safe for concurrent use from any thread.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Vector.NNTP.Utilities.Internal
{
    /// <summary>
    /// Shared throw helpers extracted from hot paths to keep caller IL small and exception messages consistent.
    /// </summary>
    /// <remarks>
    /// <para><b>Performance:</b> HOT PATH — methods used from encode/read loops use
    /// <see cref="MethodImplOptions.NoInlining"/> so the throw instruction does not block inlining of callers.</para>
    ///
    /// <para><b>Thread safety:</b> All members are <see langword="static"/> and stateless.</para>
    /// </remarks>
    internal static class ThrowHelpers
    {
        /// <summary>
        /// Throws when a required input span is empty.
        /// </summary>
        /// <param name="paramName">Parameter name for the thrown exception.</param>
        /// <exception cref="ArgumentException">Always thrown.</exception>
        [DoesNotReturn]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void EmptySource(string paramName)
        {
            throw new ArgumentException("Input span must not be empty.", paramName);
        }

        /// <summary>
        /// Throws when a string contains non-ASCII characters.
        /// </summary>
        /// <param name="length">Length of the offending input.</param>
        /// <param name="paramName">Parameter name for the thrown exception.</param>
        /// <exception cref="ArgumentException">Always thrown.</exception>
        [DoesNotReturn]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void NonAsciiInput(int length, string paramName)
        {
            throw new ArgumentException($"Input contains non-ASCII characters (length={length}).", paramName);
        }

        /// <summary>
        /// Throws when a character span contains non-ASCII characters.
        /// </summary>
        /// <param name="length">Length of the offending input.</param>
        /// <param name="paramName">Parameter name for the thrown exception.</param>
        /// <exception cref="ArgumentException">Always thrown.</exception>
        [DoesNotReturn]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void NonAsciiSpanInput(int length, string paramName)
        {
            throw new ArgumentException($"Input contains non-ASCII characters (source length={length}).", paramName);
        }

        /// <summary>
        /// Throws when a byte span contains non-ASCII bytes.
        /// </summary>
        /// <param name="length">Length of the offending input.</param>
        /// <param name="paramName">Parameter name for the thrown exception.</param>
        /// <exception cref="ArgumentException">Always thrown.</exception>
        [DoesNotReturn]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void NonAsciiByteInput(int length, string paramName)
        {
            throw new ArgumentException($"Input contains non-ASCII bytes (length={length}).", paramName);
        }

        /// <summary>
        /// Throws when a destination span is too short for the requested operation.
        /// </summary>
        /// <param name="requiredLength">Required destination length.</param>
        /// <param name="actualLength">Actual destination length.</param>
        /// <param name="paramName">Parameter name for the thrown exception.</param>
        /// <exception cref="ArgumentException">Always thrown.</exception>
        [DoesNotReturn]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void DestinationTooShort(int requiredLength, int actualLength, string paramName)
        {
            throw new ArgumentException(
                $"Destination span is too short (required={requiredLength}, actual={actualLength}).",
                paramName);
        }

        /// <summary>
        /// Throws when an ASCII encode destination span is too short.
        /// </summary>
        /// <param name="requiredLength">Required destination length.</param>
        /// <param name="actualLength">Actual destination length.</param>
        /// <param name="paramName">Parameter name for the thrown exception.</param>
        /// <exception cref="ArgumentException">Always thrown.</exception>
        [DoesNotReturn]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void DestinationTooShortForAsciiEncode(int requiredLength, int actualLength, string paramName)
        {
            throw new ArgumentException(
                $"Destination span is too short (required={requiredLength}, actual={actualLength}). ASCII encoding requires destination.Length >= source.Length.",
                paramName);
        }

        /// <summary>
        /// Throws when an inner stream does not support reading.
        /// </summary>
        /// <param name="paramName">Parameter name for the thrown exception.</param>
        /// <exception cref="ArgumentException">Always thrown.</exception>
        [DoesNotReturn]
        public static void InnerStreamNotReadable(string paramName)
        {
            throw new ArgumentException("Inner stream must be readable (CanRead must be true).", paramName);
        }
    }
}
