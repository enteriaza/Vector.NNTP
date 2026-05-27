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
}
