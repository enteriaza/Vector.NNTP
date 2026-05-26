// EncodingUtilities.cs -- SIMD-accelerated ASCII encoding, decoding, and validation helpers.
//
// Centralises ASCII string <-> byte[] / Span<byte> encoding, decoding, validation, and lossy conversion
// patterns used across the project.  All core paths delegate to System.Text.Ascii (.NET 8+) which is
// SIMD-accelerated -- the JIT emits SSE2/AVX2 vector instructions on x64, processing 16-32 elements per
// iteration.  No additional manual SIMD enhancement is needed.
//
// Thread safety:
//   All methods are static and stateless.  Safe for concurrent use from any thread.
//
// Security:
//   Non-ASCII characters (codepoints >= 0x80) cause OperationStatus.InvalidData from System.Text.Ascii.
//   The encoding/decoding methods throw ArgumentException on invalid input rather than silently substituting
//   '?' (0x3F) as Encoding.ASCII does -- failing loudly is safer for protocol-critical encoding paths.
//   The lossy overloads (AsciiToCharsLossy) are explicitly opt-in and documented for diagnostic-only use.
//   Throw helpers accept only the input length, not the input data, to prevent sensitive content (e.g.,
//   AUTHINFO credentials) from leaking into exception messages.
//
// Allocation profile:
//   AsciiToBytes:      One heap byte[] (the returned array).  Zero intermediate allocations.
//   AsciiToSpan:       Zero -- writes directly into the caller-supplied span.
//   AsciiToString:     One byte[] copy of source (ref struct capture) + one heap string.
//   AsciiToCharsLossy: Zero -- writes directly into the caller-supplied char span.
//   AsciiBytesLength:  Zero -- returns value.Length.
//   IsAscii:           Zero -- pure predicate.
//
// Cross-platform:
//   Fully portable.  System.Text.Ascii is a BCL API available on all .NET 8 runtimes (Windows x64,
//   Linux x64).  No P/Invoke, no OS-specific APIs.
//
// SIMD applicability:
//   Yes -- all core paths delegate to System.Text.Ascii which is SIMD-vectorised on .NET 8.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;

namespace Vector.NNTP.MessageBus.Utilities
{
    /// <summary>
    /// SIMD-accelerated ASCII encoding, decoding, validation, and lossy conversion helpers.
    /// </summary>
    /// <remarks>
    /// <para><b>Purpose:</b> Centralises ASCII string/byte conversion patterns used across the NNTP session layer,
    /// cache subsystem, hashing utilities, and protocol formatting.  All methods delegate to
    /// <see cref="Ascii"/> (.NET 8+) which uses SSE2/AVX2 vector instructions on x64.</para>
    ///
    /// <para><b>Encoding (string/char -> byte):</b></para>
    /// <list type="bullet">
    ///   <item><description><see cref="AsciiToBytes"/> -- allocates and returns a <c>byte[]</c>.</description></item>
    ///   <item><description><see cref="AsciiToSpan"/> -- writes into a caller-supplied <see cref="Span{T}"/>.
    ///     Zero heap allocation.</description></item>
    /// </list>
    ///
    /// <para><b>Decoding (byte -> string/char):</b></para>
    /// <list type="bullet">
    ///   <item><description><see cref="AsciiToString"/> -- strict decoding; throws on non-ASCII
    ///     bytes.</description></item>
    ///   <item><description><see cref="AsciiToCharsLossy"/> -- lossy decoding for diagnostic output; replaces
    ///     non-ASCII bytes with <c>?</c> (0x3F).</description></item>
    /// </list>
    ///
    /// <para><b>Validation:</b></para>
    /// <list type="bullet">
    ///   <item><description><see cref="IsAscii(ReadOnlySpan{char})"/> -- character span validation.</description></item>
    ///   <item><description><see cref="IsAscii(ReadOnlySpan{byte})"/> -- byte span validation.</description></item>
    /// </list>
    ///
    /// <para><b>Security:</b> Unlike <see cref="Encoding.ASCII"/> which silently replaces non-ASCII characters
    /// with <c>0x3F</c> (<c>?</c>), the strict encoding and decoding methods throw <see cref="ArgumentException"/>
    /// on any codepoint >= 0x80.  Throw helpers accept only the input length to prevent sensitive data (e.g.,
    /// AUTHINFO credentials) from leaking into exception messages.</para>
    ///
    /// <para><b>Thread safety:</b> All methods are <see langword="static"/> with no shared mutable state.  Safe for
    /// concurrent use from any number of threads.</para>
    ///
    /// <para><b>Cross-platform:</b> Fully portable.  <see cref="Ascii"/> is a BCL API available on all
    /// .NET 8 runtimes (Windows x64, Linux x64).  No P/Invoke, no OS-specific APIs.</para>
    /// </remarks>
    internal static class EncodingUtilities
    {

        #region Public Methods — Encoding (string/char -> byte)

        /// <summary>
        /// Encodes a pure-ASCII <see cref="string"/> into a new <c>byte[]</c> using SIMD-accelerated
        /// <see cref="Ascii.FromUtf16(ReadOnlySpan{char}, Span{byte}, out int)"/>.
        /// </summary>
        /// <param name="value">The ASCII string to encode.  Must not be <see langword="null"/> or empty, and must
        /// contain only US-ASCII characters (codepoints 0x00-0x7F).</param>
        /// <returns>A <c>byte[]</c> of exactly <c>value.Length</c> bytes containing the ASCII encoding.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is <see langword="null"/>, empty,
        /// or contains non-ASCII characters (codepoints >= 0x80).</exception>
        /// <remarks>
        /// <para><b>SIMD path:</b> Processes 16-32 characters per vector iteration on x64 (SSE2/AVX2).  For NNTP
        /// message-IDs (~40-80 characters), this is measurably faster than <c>Encoding.ASCII.GetBytes(string)</c>
        /// which uses a scalar byte-by-byte loop.</para>
        ///
        /// <para><b>Non-ASCII rejection:</b> Unlike <see cref="Encoding.ASCII"/> which silently replaces non-ASCII
        /// characters with <c>0x3F</c> (<c>?</c>), this method throws <see cref="ArgumentException"/>.  This is
        /// intentional for protocol-critical paths (NNTP message-IDs per RFC 5322 S3.6.4).</para>
        ///
        /// <para><b>When to use:</b> Use when the caller needs a persistent <c>byte[]</c> reference.  For callers
        /// that own the destination buffer, prefer <see cref="AsciiToSpan"/>.</para>
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte[] AsciiToBytes(string value)
        {
            ArgumentException.ThrowIfNullOrEmpty(value);
            byte[] result = new byte[value.Length];
            if (Ascii.FromUtf16(value, result, out _) != OperationStatus.Done)
                ThrowNonAsciiInput(value.Length, nameof(value));
            return result;
        }

        /// <summary>
        /// Encodes a pure-ASCII character span into a caller-supplied byte span using SIMD-accelerated
        /// <see cref="Ascii.FromUtf16(ReadOnlySpan{char}, Span{byte}, out int)"/>.  Zero heap allocation.
        /// </summary>
        /// <param name="source">The ASCII characters to encode.  Must contain only US-ASCII codepoints (0x00-0x7F).
        /// May be empty (no-op, returns 0).</param>
        /// <param name="destination">The byte span to receive the encoded bytes.  Must be at least
        /// <c>source.Length</c> bytes long.</param>
        /// <returns>The number of bytes written to <paramref name="destination"/>.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="destination"/> is shorter than
        /// <c>source.Length</c>, or when <paramref name="source"/> contains non-ASCII characters.</exception>
        /// <remarks>
        /// <para><b>Zero allocation:</b> No heap allocation on the success path.</para>
        ///
        /// <para><b>Destination validation:</b> Checked eagerly before calling <c>FromUtf16</c>.  This ensures any
        /// non-<see cref="OperationStatus.Done"/> return is unambiguously <see cref="OperationStatus.InvalidData"/>
        /// (non-ASCII input), not <see cref="OperationStatus.DestinationTooSmall"/>.</para>
        ///
        /// <para><b>Empty source:</b> Handled gracefully as a no-op returning 0.  This differs from
        /// <see cref="AsciiToBytes"/> which rejects empty input -- writing zero bytes into an existing span is a
        /// valid no-op, while allocating a zero-length <c>byte[]</c> is wasteful.</para>
        ///
        /// <para><b>When to use:</b> Use when the caller owns the destination buffer (stackalloc, PipeWriter).</para>
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int AsciiToSpan(ReadOnlySpan<char> source, Span<byte> destination)
        {
            if (source.IsEmpty)
                return 0;
            if (destination.Length < source.Length)
                ThrowDestinationTooShort(source.Length, destination.Length, nameof(destination));
            if (Ascii.FromUtf16(source, destination, out int bytesWritten) != OperationStatus.Done)
                ThrowNonAsciiSpanInput(source.Length, nameof(source));
            return bytesWritten;
        }

        #endregion

        #region Public Methods — Decoding (byte -> string)

        /// <summary>
        /// Decodes a pure-ASCII byte span into a new <see cref="string"/> using SIMD-accelerated
        /// <see cref="Ascii.ToUtf16(ReadOnlySpan{byte}, Span{char}, out int)"/>.
        /// </summary>
        /// <param name="source">The ASCII bytes to decode.  Must contain only US-ASCII values (0x00-0x7F).
        /// Must not be empty.</param>
        /// <returns>A <see cref="string"/> of exactly <c>source.Length</c> characters.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="source"/> is empty or contains
        /// non-ASCII bytes (values >= 0x80).</exception>
        /// <remarks>
        /// <para><b>SIMD path:</b> Uses <see cref="Ascii.ToUtf16"/> to widen bytes to chars, processing
        /// 16-32 bytes per vector iteration on x64.</para>
        ///
        /// <para><b>Allocation:</b> One <c>byte[]</c> copy of the source span (required because
        /// <see cref="ReadOnlySpan{T}"/> is a ref struct that cannot be captured in the
        /// <see cref="string.Create{TState}(int, TState, SpanAction{char, TState})"/> closure) plus one
        /// <see cref="string"/>.  For typical NNTP message-IDs (~40-80 bytes), the array copy is
        /// negligible.</para>
        ///
        /// <para><b>Non-ASCII rejection:</b> Throws <see cref="ArgumentException"/> on any byte >= 0x80.  This is
        /// intentional for security-critical paths (AUTHINFO credential extraction) where silent substitution
        /// could produce incorrect authentication data.</para>
        /// </remarks>
        public static string AsciiToString(ReadOnlySpan<byte> source)
        {
            if (source.IsEmpty)
                ThrowEmptySource(nameof(source));
            // Validate ASCII before copying to avoid allocating the byte[] for invalid input.
            if (!Ascii.IsValid(source))
                ThrowNonAsciiByteInput(source.Length, nameof(source));
            // Copy source bytes to a byte[] so the data can be captured in the string.Create callback.
            // ReadOnlySpan<byte> is a ref struct and cannot be captured in a closure.  The byte[] copy is
            // the only safe alternative to 'unsafe' pointer pinning.
            byte[] sourceArray = source.ToArray();
            return string.Create(sourceArray.Length, sourceArray, static (chars, state) =>
            {
                // ASCII is already validated above -- ToUtf16 will always return Done.
                _ = Ascii.ToUtf16(state, chars, out _);
            });
        }

        #endregion

        #region Public Methods — Lossy Decoding (byte -> char)

        /// <summary>
        /// Decodes ASCII bytes into a caller-supplied character span, replacing non-ASCII bytes (>= 0x80) with
        /// <c>?</c> (U+003F).  Zero heap allocation.  SIMD-accelerated on the fast path.
        /// </summary>
        /// <param name="source">The byte span to decode.  May contain non-ASCII bytes.  May be empty (no-op,
        /// returns 0).</param>
        /// <param name="destination">The character span to receive the decoded characters.  Must be at least
        /// <c>source.Length</c> characters long.</param>
        /// <returns>The number of characters written to <paramref name="destination"/>.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="destination"/> is shorter than
        /// <c>source.Length</c>.</exception>
        /// <remarks>
        /// <para><b>Fast path:</b> When all bytes are valid ASCII, delegates to the SIMD-accelerated
        /// <see cref="Ascii.ToUtf16(ReadOnlySpan{byte}, Span{char}, out int)"/>.  No scalar
        /// fallback is entered.</para>
        ///
        /// <para><b>Slow path:</b> When <c>ToUtf16</c> reports <see cref="OperationStatus.InvalidData"/>, falls
        /// back to <see cref="Encoding.ASCII"/> which replaces non-ASCII bytes with <c>?</c> (0x3F).  This is
        /// acceptable for diagnostic logging where lossy conversion is preferred over exceptions.</para>
        ///
        /// <para><b>When to use:</b> Use for diagnostic/logging output where non-ASCII bytes should be rendered
        /// as <c>?</c> rather than causing an exception.  For protocol-critical paths, use
        /// <see cref="AsciiToString"/> which throws on non-ASCII input.</para>
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int AsciiToCharsLossy(ReadOnlySpan<byte> source, Span<char> destination)
        {
            if (source.IsEmpty)
                return 0;
            if (destination.Length < source.Length)
                ThrowDestinationTooShort(source.Length, destination.Length, nameof(destination));
            // SIMD fast path: if all bytes are valid ASCII, ToUtf16 completes in 1-2 vector iterations.
            if (Ascii.ToUtf16(source, destination, out int charsWritten) == OperationStatus.Done)
                return charsWritten;
            // Slow path: non-ASCII bytes present.  Encoding.ASCII replaces them with '?' (0x3F).
            return Encoding.ASCII.GetChars(source, destination);
        }

        #endregion

        #region Public Methods — Validation

        /// <summary>
        /// Returns <see langword="true"/> if every character in <paramref name="value"/> is a US-ASCII codepoint
        /// (0x00-0x7F).  SIMD-accelerated via <see cref="Ascii.IsValid(ReadOnlySpan{char})"/>.
        /// </summary>
        /// <param name="value">The character span to validate.  May be empty (returns <see langword="true"/>).</param>
        /// <returns><see langword="true"/> if all characters are in the range 0x00-0x7F;
        /// <see langword="false"/> if any character has a codepoint >= 0x80.</returns>
        /// <remarks>
        /// <para><b>When to use:</b> Use before encoding when the caller needs to distinguish invalid input from
        /// other errors.  The encoding methods already reject non-ASCII with <see cref="ArgumentException"/>.</para>
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsAscii(ReadOnlySpan<char> value)
        {
            return Ascii.IsValid(value);
        }

        /// <summary>
        /// Returns <see langword="true"/> if every byte in <paramref name="value"/> is a US-ASCII value (0x00-0x7F).
        /// SIMD-accelerated via <see cref="Ascii.IsValid(ReadOnlySpan{byte})"/>.
        /// </summary>
        /// <param name="value">The byte span to validate.  May be empty (returns <see langword="true"/>).</param>
        /// <returns><see langword="true"/> if all bytes are in the range 0x00-0x7F;
        /// <see langword="false"/> if any byte >= 0x80.</returns>
        /// <remarks>
        /// <para><b>Inverted sense vs. ContainsNonAscii:</b> Returns <see langword="true"/> for valid ASCII --
        /// the opposite of <c>NntpParser.ContainsNonAscii</c>.  The positive-sense naming avoids double negation
        /// at call sites.</para>
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsAscii(ReadOnlySpan<byte> value)
        {
            return Ascii.IsValid(value);
        }

        #endregion

        #region Public Methods — Length Calculation

        /// <summary>
        /// Returns the byte length that <see cref="AsciiToBytes"/> would produce for the given ASCII string.
        /// For pure ASCII input, this is always <c>value.Length</c>.
        /// </summary>
        /// <param name="value">The ASCII string to measure.  Must not be <see langword="null"/>.</param>
        /// <returns>The byte length (<c>value.Length</c>).</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is
        /// <see langword="null"/>.</exception>
        /// <remarks>
        /// <para><b>Why a dedicated method?</b> Callers that need the byte length for buffer pre-sizing (e.g.,
        /// <see cref="System.IO.Pipelines.PipeWriter.GetSpan(int)"/>) can avoid a redundant
        /// <see cref="AsciiToBytes"/> call.  Encapsulating the assumption in a named method documents the 1:1
        /// ASCII char-to-byte relationship.</para>
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int AsciiBytesLength(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            return value.Length;
        }

        #endregion

        #region Private Throw Helpers

        /// <summary>
        /// Throws <see cref="ArgumentException"/> when a string or character input contains non-ASCII characters.
        /// Accepts only the length to prevent sensitive content from leaking into exception messages.
        /// </summary>
        /// <remarks>
        /// <para><b>Extraction rationale:</b> The .NET 8 JIT refuses to inline methods containing <c>throw</c>.
        /// Isolating the throw keeps the fast path inlineable.</para>
        ///
        /// <para><b>Security:</b> Accepts only the input length, not the input data.</para>
        /// </remarks>
        [DoesNotReturn]
        private static void ThrowNonAsciiInput(int sourceLength, string paramName)
        {
            throw new ArgumentException($"Input contains non-ASCII characters (length={sourceLength}).", paramName);
        }

        /// <summary>
        /// Throws <see cref="ArgumentException"/> when a character span contains non-ASCII characters.
        /// </summary>
        [DoesNotReturn]
        private static void ThrowNonAsciiSpanInput(int sourceLength, string paramName)
        {
            throw new ArgumentException($"Input span contains non-ASCII characters (source length={sourceLength}).", paramName);
        }

        /// <summary>
        /// Throws <see cref="ArgumentException"/> when a byte span contains non-ASCII bytes.
        /// </summary>
        [DoesNotReturn]
        private static void ThrowNonAsciiByteInput(int sourceLength, string paramName)
        {
            throw new ArgumentException($"Input bytes contain non-ASCII values (source length={sourceLength}).", paramName);
        }

        /// <summary>
        /// Throws <see cref="ArgumentException"/> when an empty byte span is passed to <see cref="AsciiToString"/>.
        /// </summary>
        [DoesNotReturn]
        private static void ThrowEmptySource(string paramName)
        {
            throw new ArgumentException("Source byte span must not be empty.", paramName);
        }

        /// <summary>
        /// Throws <see cref="ArgumentException"/> when the destination span is shorter than the source span.
        /// </summary>
        [DoesNotReturn]
        private static void ThrowDestinationTooShort(int requiredLength, int actualLength, string paramName)
        {
            throw new ArgumentException(
                $"Destination span is too short (required={requiredLength}, actual={actualLength}). " +
                "ASCII encoding requires destination.Length >= source.Length.", paramName);
        }

        #endregion

    }
}
