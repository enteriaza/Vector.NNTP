// <copyright file="EncodingUtilities.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: no LINQ; no allocations on success; no closures; no boxing; prefer Span/stackalloc; throws extracted + NoInlining where documented.
// EncodingUtilities.cs -- SIMD-accelerated ASCII encoding, decoding, and validation helpers.

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using Vector.NNTP.Utilities.Internal;

namespace Vector.NNTP.Utilities.Encoding
{
    /// <summary>
    /// SIMD-accelerated ASCII encoding, decoding, validation, and lossy conversion helpers.
    /// </summary>
    /// <remarks>
    /// <para><b>Security:</b> Strict encoding and decoding methods throw on non-ASCII input. Lossy conversion is
    /// explicitly opt-in via <see cref="AsciiToCharsLossy"/>.</para>
    ///
    /// <para><b>SIMD:</b> Core paths delegate to <see cref="Ascii"/> (.NET 8+) which uses SIMD acceleration on x64.</para>
    ///
    /// <para><b>Performance:</b> HOT PATH -- success paths avoid allocations except where a new <see cref="string"/> or
    /// <see cref="byte"/>[] is explicitly returned; throws are delegated to <see cref="ThrowHelpers"/>.</para>
    /// </remarks>
    public static class EncodingUtilities
    {
        /// <summary>
        /// Encodes a pure-ASCII <see cref="string"/> into a new byte array.
        /// </summary>
        /// <param name="value">The ASCII string to encode.</param>
        /// <returns>A new byte array containing ASCII bytes.</returns>
        /// <exception cref="ArgumentException">Thrown when input is null/empty or contains non-ASCII characters.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte[] AsciiToBytes(string value)
        {
            ArgumentException.ThrowIfNullOrEmpty(value);

            byte[] result = new byte[value.Length];
            if (Ascii.FromUtf16(value, result, out _) != OperationStatus.Done)
            {
                ThrowHelpers.NonAsciiInput(value.Length, nameof(value));
            }

            return result;
        }

        /// <summary>
        /// Encodes ASCII characters into a destination buffer.
        /// </summary>
        /// <param name="source">ASCII characters to encode.</param>
        /// <param name="destination">Destination buffer (must be at least <c>source.Length</c>).</param>
        /// <returns>Bytes written.</returns>
        /// <exception cref="ArgumentException">Thrown on non-ASCII input or insufficient destination capacity.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int AsciiToSpan(ReadOnlySpan<char> source, Span<byte> destination)
        {
            if (source.IsEmpty)
            {
                return 0;
            }

            if (destination.Length < source.Length)
            {
                ThrowHelpers.DestinationTooShortForAsciiEncode(source.Length, destination.Length, nameof(destination));
            }

            if (Ascii.FromUtf16(source, destination, out int bytesWritten) != OperationStatus.Done)
            {
                ThrowHelpers.NonAsciiSpanInput(source.Length, nameof(source));
            }

            return bytesWritten;
        }

        /// <summary>
        /// Decodes ASCII bytes into a new <see cref="string"/>, throwing on any non-ASCII byte.
        /// </summary>
        /// <param name="source">ASCII bytes.</param>
        /// <returns>The decoded string.</returns>
        /// <exception cref="ArgumentException">Thrown when source is empty or contains non-ASCII bytes.</exception>
        public static string AsciiToString(ReadOnlySpan<byte> source)
        {
            if (source.IsEmpty)
            {
                ThrowHelpers.EmptySource(nameof(source));
            }

            if (!Ascii.IsValid(source))
            {
                ThrowHelpers.NonAsciiByteInput(source.Length, nameof(source));
            }

            byte[] sourceArray = source.ToArray();
            return string.Create(sourceArray.Length, sourceArray, static (chars, state) =>
            {
                _ = Ascii.ToUtf16(state, chars, out _);
            });
        }

        /// <summary>
        /// Decodes bytes into chars, replacing non-ASCII bytes with <c>?</c> (U+003F).
        /// </summary>
        /// <param name="source">Bytes to decode.</param>
        /// <param name="destination">Destination characters (must be at least <c>source.Length</c>).</param>
        /// <returns>Characters written.</returns>
        /// <exception cref="ArgumentException">Thrown when destination is too short.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int AsciiToCharsLossy(ReadOnlySpan<byte> source, Span<char> destination)
        {
            if (source.IsEmpty)
            {
                return 0;
            }

            if (destination.Length < source.Length)
            {
                ThrowHelpers.DestinationTooShortForAsciiEncode(source.Length, destination.Length, nameof(destination));
            }

            return Ascii.ToUtf16(source, destination, out int charsWritten) == OperationStatus.Done
                ? charsWritten
                : System.Text.Encoding.ASCII.GetChars(source, destination);
        }

        /// <summary>
        /// Returns <see langword="true"/> if every character is US-ASCII.
        /// </summary>
        /// <param name="value">Input characters.</param>
        /// <returns><see langword="true"/> if all characters are ASCII.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsAscii(ReadOnlySpan<char> value)
        {
            return Ascii.IsValid(value);
        }

        /// <summary>
        /// Returns <see langword="true"/> if every byte is US-ASCII (0x00-0x7F).
        /// </summary>
        /// <param name="value">Input bytes.</param>
        /// <returns><see langword="true"/> if all bytes are ASCII.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsAscii(ReadOnlySpan<byte> value)
        {
            return Ascii.IsValid(value);
        }

        /// <summary>
        /// Returns the number of bytes required to ASCII-encode <paramref name="value"/>.
        /// </summary>
        /// <param name="value">String input.</param>
        /// <returns>Byte length.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is <see langword="null"/>.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int AsciiBytesLength(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            return value.Length;
        }
    }
}
