//-----------------------------------------------------------------------
// <copyright file="DnsAsciiEncoding.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe <cknipe@opticnetworks.net>. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
//-----------------------------------------------------------------------

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;

namespace Vector.NNTP.Encryption.Dns
{
    /// <summary>
    /// ASCII validation and encoding for DNS wire-format names (RFC 1035).
    /// </summary>
    internal static class DnsAsciiEncoding
    {
        /// <summary>
        /// Returns true when every character is US-ASCII.
        /// </summary>
        /// <param name="value">The value to check.</param>
        /// <returns><see langword="true"/> if the value is ASCII; otherwise <see langword="false"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsAscii(ReadOnlySpan<char> value)
        {
            return Ascii.IsValid(value);
        }

        /// <summary>
        /// Encodes ASCII into <paramref name="destination"/>; returns bytes written.
        /// </summary>
        /// <param name="source">The source string to encode.</param>
        /// <param name="destination">The destination span to write the encoded bytes to.</param>
        /// <returns>The number of bytes written to the destination.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int AsciiToSpan(ReadOnlySpan<char> source, Span<byte> destination)
        {
            if (source.IsEmpty)
            {
                return 0;
            }

            if (destination.Length < source.Length)
            {
                ThrowDestinationTooShort(source.Length, destination.Length, nameof(destination));
            }

            if (Ascii.FromUtf16(source, destination, out int bytesWritten) != OperationStatus.Done)
            {
                ThrowNonAsciiSpanInput(source.Length, nameof(source));
            }

            return bytesWritten;
        }

        /// <summary>
        /// Throws an exception if the input contains non-ASCII characters.
        /// </summary>
        /// <param name="sourceLength">The length of the source string.</param>
        /// <param name="paramName">The name of the parameter that contains the non-ASCII characters.</param>
        [DoesNotReturn]
        private static void ThrowNonAsciiSpanInput(int sourceLength, string paramName)
        {
            throw new ArgumentException($"Input contains non-ASCII characters (source length={sourceLength}).", paramName);
        }

        /// <summary>
        /// Throws an exception if the destination span is too short.
        /// </summary>
        /// <param name="requiredLength">The required length of the destination span.</param>
        /// <param name="actualLength">The actual length of the destination span.</param>
        /// <param name="paramName">The name of the parameter that contains the destination span.</param>
        [DoesNotReturn]
        private static void ThrowDestinationTooShort(int requiredLength, int actualLength, string paramName)
        {
            throw new ArgumentException(
                $"Destination span is too short (required={requiredLength}, actual={actualLength}). " +
                "ASCII encoding requires destination.Length >= source.Length.",
                paramName);
        }
    }
}
