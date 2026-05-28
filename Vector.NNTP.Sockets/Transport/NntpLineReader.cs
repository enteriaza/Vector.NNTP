// <copyright file="NntpLineReader.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: CRLF line framing from PipeReader with SequenceReader.

using Vector.NNTP.Sockets.Session;

namespace Vector.NNTP.Sockets.Transport
{
    /// <summary>
    /// Reads CRLF-terminated lines from a <see cref="PipeReader"/> and updates Rx byte accounting.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="NntpLineReader"/> class.
    /// </remarks>
    /// <param name="reader">Input pipe reader.</param>
    /// <param name="context">Connection context for Rx accounting.</param>
    internal sealed class NntpLineReader(PipeReader reader, NntpConnectionContext context)
    {
        private readonly PipeReader _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        private readonly NntpConnectionContext _context = context ?? throw new ArgumentNullException(nameof(context));

        /// <summary>
        /// Reads one line without CRLF as raw bytes or reports completion when the connection closes.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A read result indicating whether a line was read.</returns>
        internal async ValueTask<NntpByteLineReadResult> ReadLineBytesAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                ReadResult result = await _reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                ReadOnlySequence<byte> buffer = result.Buffer;

                if (TryParseLine(buffer, out ReadOnlySequence<byte> line, out SequencePosition consumed))
                {
                    int byteCount = (int)buffer.Slice(0, consumed).Length;
                    _context.AddRxBytes(byteCount);
                    byte[] bytes = CopySequence(line);
                    _reader.AdvanceTo(consumed);
                    return NntpByteLineReadResult.LineRead(bytes);
                }

                if (result.IsCompleted)
                {
                    _reader.AdvanceTo(buffer.End);
                    return NntpByteLineReadResult.Completed;
                }

                _reader.AdvanceTo(buffer.Start, buffer.End);
            }
        }

        private static byte[] CopySequence(ReadOnlySequence<byte> sequence)
        {
            if (sequence.IsSingleSegment)
            {
                return sequence.First.Span.ToArray();
            }

            byte[] buffer = new byte[(int)sequence.Length];
            sequence.CopyTo(buffer);
            return buffer;
        }

        /// <summary>
        /// Reads one line without CRLF into a string or returns false when the connection closes.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Decoded line without CRLF, or <see langword="null"/> when the connection closes.</returns>
        internal async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                ReadResult result = await _reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                ReadOnlySequence<byte> buffer = result.Buffer;

                if (TryParseLine(buffer, out ReadOnlySequence<byte> line, out SequencePosition consumed))
                {
                    int byteCount = (int)buffer.Slice(0, consumed).Length;
                    _context.AddRxBytes(byteCount);
                    string lineChars = DecodeLine(line);
                    _reader.AdvanceTo(consumed);
                    return lineChars;
                }

                if (result.IsCompleted)
                {
                    _reader.AdvanceTo(buffer.End);
                    return null;
                }

                _reader.AdvanceTo(buffer.Start, buffer.End);
            }
        }

        /// <summary>
        /// Tries to parse a line from the buffer.
        /// </summary>
        /// <param name="buffer">The buffer to parse.</param>
        /// <param name="line">The parsed line.</param>
        /// <param name="consumed">The position of the consumed bytes.</param>
        /// <returns>True if the line was parsed successfully, false otherwise.</returns>
        private static bool TryParseLine(
            ReadOnlySequence<byte> buffer,
            out ReadOnlySequence<byte> line,
            out SequencePosition consumed)
        {
            SequenceReader<byte> reader = new(buffer);
            if (!reader.TryReadTo(out line, (byte)'\n', advancePastDelimiter: true))
            {
                consumed = default;
                return false;
            }

            if (line.Length > 0)
            {
                ReadOnlySpan<byte> last = line.Slice(line.Length - 1, 1).First.Span;
                if (last[0] == (byte)'\r')
                {
                    line = line.Slice(0, line.Length - 1);
                }
            }

            consumed = reader.Position;
            return true;
        }

        /// <summary>
        /// Decodes a line from the buffer.
        /// </summary>
        /// <param name="line">The line to decode.</param>
        /// <returns>The decoded line.</returns>
        private static string DecodeLine(ReadOnlySequence<byte> line)
        {
            if (line.IsSingleSegment)
            {
                return Encoding.ASCII.GetString(line.First.Span);
            }

            byte[] rented = ArrayPool<byte>.Shared.Rent((int)line.Length);
            try
            {
                line.CopyTo(rented);
                return Encoding.ASCII.GetString(rented.AsSpan(0, (int)line.Length));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    /// <summary>
    /// Represents one read attempt from <see cref="NntpLineReader"/> in raw bytes.
    /// </summary>
    internal readonly struct NntpByteLineReadResult
    {
        private readonly ReadOnlyMemory<byte> _line;

        private NntpByteLineReadResult(bool isCompleted, ReadOnlyMemory<byte> line)
        {
            IsCompleted = isCompleted;
            _line = line;
        }

        /// <summary>
        /// Gets a value indicating whether the reader completed.
        /// </summary>
        internal bool IsCompleted { get; }

        /// <summary>
        /// Gets the line without CRLF.
        /// </summary>
        internal ReadOnlyMemory<byte> Line => _line;

        /// <summary>
        /// Gets a value indicating whether the line is the dot terminator.
        /// </summary>
        internal bool IsDotTerminator => _line.Length == 1 && _line.Span[0] == (byte)'.';

        /// <summary>
        /// Gets a value indicating whether the line is dot-stuffed.
        /// </summary>
        internal bool IsDotStuffed => _line.Length >= 2 && _line.Span[0] == (byte)'.' && _line.Span[1] == (byte)'.';

        /// <summary>
        /// Gets a completion result.
        /// </summary>
        internal static NntpByteLineReadResult Completed => new(true, ReadOnlyMemory<byte>.Empty);

        /// <summary>
        /// Creates a line result.
        /// </summary>
        /// <param name="line">Line without CRLF.</param>
        /// <returns>Result.</returns>
        internal static NntpByteLineReadResult LineRead(byte[] line) => new(false, line);
    }
}
