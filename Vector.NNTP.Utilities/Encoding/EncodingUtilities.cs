// <copyright file="EncodingUtilities.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// EncodingUtilities.cs -- SIMD-accelerated ASCII encoding, decoding, and validation helpers.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;

namespace Vector.NNTP.Utilities.Encoding;

/// <summary>
/// SIMD-accelerated ASCII encoding, decoding, validation, and lossy conversion helpers.
/// </summary>
/// <remarks>
/// <para><b>Security:</b> Strict encoding and decoding methods throw on non-ASCII input. Lossy conversion is
/// explicitly opt-in via <see cref="AsciiToCharsLossy"/>.</para>
///
/// <para><b>SIMD:</b> Core paths delegate to <see cref="Ascii"/> (.NET 8+) which uses SIMD acceleration on x64.</para>
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
            ThrowNonAsciiInput(value.Length, nameof(value));
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
            ThrowDestinationTooShort(source.Length, destination.Length, nameof(destination));
        }

        if (Ascii.FromUtf16(source, destination, out int bytesWritten) != OperationStatus.Done)
        {
            ThrowNonAsciiSpanInput(source.Length, nameof(source));
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
            ThrowEmptySource(nameof(source));
        }

        if (!Ascii.IsValid(source))
        {
            ThrowNonAsciiByteInput(source.Length, nameof(source));
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
            ThrowDestinationTooShort(source.Length, destination.Length, nameof(destination));
        }

        if (Ascii.ToUtf16(source, destination, out int charsWritten) == OperationStatus.Done)
        {
            return charsWritten;
        }

        return System.Text.Encoding.ASCII.GetChars(source, destination);
    }

    /// <summary>
    /// Returns <see langword="true"/> if every character is US-ASCII.
    /// </summary>
    /// <param name="value">Input characters.</param>
    /// <returns><see langword="true"/> if all characters are ASCII.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsAscii(ReadOnlySpan<char> value) => Ascii.IsValid(value);

    /// <summary>
    /// Returns <see langword="true"/> if every byte is US-ASCII (0x00-0x7F).
    /// </summary>
    /// <param name="value">Input bytes.</param>
    /// <returns><see langword="true"/> if all bytes are ASCII.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsAscii(ReadOnlySpan<byte> value) => Ascii.IsValid(value);

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

    /// <summary>
    /// Throws when a required source span is empty.
    /// </summary>
    /// <param name="paramName">Parameter name for the thrown exception.</param>
    [DoesNotReturn]
    private static void ThrowEmptySource(string paramName) =>
        throw new ArgumentException("Input span must not be empty.", paramName);

    /// <summary>
    /// Throws when a string contains non-ASCII characters.
    /// </summary>
    /// <param name="length">Length of the offending input.</param>
    /// <param name="paramName">Parameter name for the thrown exception.</param>
    [DoesNotReturn]
    private static void ThrowNonAsciiInput(int length, string paramName) =>
        throw new ArgumentException($"Input contains non-ASCII characters (length={length}).", paramName);

    /// <summary>
    /// Throws when a character span contains non-ASCII characters.
    /// </summary>
    /// <param name="length">Length of the offending input.</param>
    /// <param name="paramName">Parameter name for the thrown exception.</param>
    [DoesNotReturn]
    private static void ThrowNonAsciiSpanInput(int length, string paramName) =>
        throw new ArgumentException($"Input contains non-ASCII characters (source length={length}).", paramName);

    /// <summary>
    /// Throws when a byte span contains non-ASCII bytes.
    /// </summary>
    /// <param name="length">Length of the offending input.</param>
    /// <param name="paramName">Parameter name for the thrown exception.</param>
    [DoesNotReturn]
    private static void ThrowNonAsciiByteInput(int length, string paramName) =>
        throw new ArgumentException($"Input contains non-ASCII bytes (length={length}).", paramName);

    /// <summary>
    /// Throws when a destination span is too short for the requested operation.
    /// </summary>
    /// <param name="requiredLength">Required destination length.</param>
    /// <param name="actualLength">Actual destination length.</param>
    /// <param name="paramName">Parameter name for the thrown exception.</param>
    [DoesNotReturn]
    private static void ThrowDestinationTooShort(int requiredLength, int actualLength, string paramName) =>
        throw new ArgumentException(
            $"Destination span is too short (required={requiredLength}, actual={actualLength}). ASCII encoding requires destination.Length >= source.Length.",
            paramName);
}
