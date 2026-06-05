// <copyright file="NntpArticleBodyReader.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: chunked dot-stuffed article body reader (TAKETHIS, IHAVE, POST).

using Vector.NNTP.Sockets.Session;

namespace Vector.NNTP.Sockets.Transport
{
    /// <summary>
    /// Reads RFC 3977 dot-stuffed multi-line article bodies from a <see cref="PipeReader"/> with minimal per-line overhead.
    /// </summary>
    internal static class NntpArticleBodyReader
    {
        private static readonly byte[] Crlf = [(byte)'\r', (byte)'\n'];

        /// <summary>
        /// Reads a dot-stuffed body terminated by a lone <c>.</c> line into a single byte array.
        /// </summary>
        /// <param name="reader">Session input pipe.</param>
        /// <param name="context">Connection context for Rx byte accounting.</param>
        /// <param name="maxBodyBytes">Maximum decoded body size (0 disables the limit).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Read outcome and decoded body bytes when complete.</returns>
        internal static ValueTask<NntpArticleBodyReadResult> ReadDotStuffedBodyAsync(
            PipeReader reader,
            NntpConnectionContext context,
            long maxBodyBytes,
            CancellationToken cancellationToken) =>
            ReadDotStuffedBodyCoreAsync(reader, context, maxBodyBytes, accumulate: true, cancellationToken);

        /// <summary>
        /// Discards a pipelined dot-stuffed body until the lone-dot terminator without enforcing <see cref="NntpServerOptions.MaxArtSize"/>.
        /// </summary>
        /// <param name="reader">Session input pipe.</param>
        /// <param name="context">Connection context for Rx byte accounting.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="ValueTask"/> that completes when the terminator line is consumed.</returns>
        internal static async ValueTask DrainDotStuffedBodyAsync(
            PipeReader reader,
            NntpConnectionContext context,
            CancellationToken cancellationToken)
        {
            _ = await ReadDotStuffedBodyCoreAsync(
                reader,
                context,
                maxBodyBytes: 0,
                accumulate: false,
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads or drains a dot-stuffed body until the lone-dot terminator.
        /// </summary>
        /// <param name="reader">Session input pipe.</param>
        /// <param name="context">Connection context for Rx byte accounting.</param>
        /// <param name="maxBodyBytes">Maximum decoded body size when accumulating (0 disables the limit).</param>
        /// <param name="accumulate">When <see langword="false"/>, bytes are discarded instead of copied.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Read outcome when accumulating; otherwise a complete result with an empty body.</returns>
        private static async ValueTask<NntpArticleBodyReadResult> ReadDotStuffedBodyCoreAsync(
            PipeReader reader,
            NntpConnectionContext context,
            long maxBodyBytes,
            bool accumulate,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(reader);
            ArgumentNullException.ThrowIfNull(context);
            ArrayBufferWriter<byte> body = new();
            while (true)
            {
                ReadResult result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                ReadOnlySequence<byte> buffer = result.Buffer;
                (SequencePosition consumed, bool terminated, body, bool exceededMaxSize) = ProcessAvailableLines(
                    buffer,
                    body,
                    maxBodyBytes,
                    accumulate);
                AdvanceConsumedBytes(reader, context, buffer, consumed);
                if (exceededMaxSize)
                {
                    return NntpArticleBodyReadResult.ExceededMaxSize();
                }

                if (terminated)
                {
                    byte[] decoded = !accumulate || body.WrittenCount == 0 ? Array.Empty<byte>() : body.WrittenSpan.ToArray();
                    return NntpArticleBodyReadResult.Complete(decoded);
                }

                if (result.IsCompleted)
                {
                    break;
                }

                if (consumed.Equals(buffer.Start))
                {
                    reader.AdvanceTo(buffer.Start, buffer.End);
                }
            }

            byte[] partial = !accumulate || body.WrittenCount == 0 ? Array.Empty<byte>() : body.WrittenSpan.ToArray();
            return NntpArticleBodyReadResult.Complete(partial);
        }

        /// <summary>
        /// Advances the pipe reader through consumed bytes and updates Rx accounting.
        /// </summary>
        /// <param name="reader">Session input pipe.</param>
        /// <param name="context">Connection context for Rx byte accounting.</param>
        /// <param name="buffer">Buffer returned by the most recent <see cref="PipeReader.ReadAsync"/> call.</param>
        /// <param name="consumed">Position through which lines were processed.</param>
        private static void AdvanceConsumedBytes(
            PipeReader reader,
            NntpConnectionContext context,
            ReadOnlySequence<byte> buffer,
            SequencePosition consumed)
        {
            if (consumed.Equals(buffer.Start))
            {
                return;
            }

            int byteCount = (int)buffer.Slice(0, consumed).Length;
            if (byteCount > 0)
            {
                context.AddRxBytes(byteCount);
            }

            reader.AdvanceTo(consumed);
        }

        /// <summary>
        /// Scans a pipe buffer for complete CRLF lines and appends decoded payloads until a terminator or partial line remains.
        /// </summary>
        /// <param name="buffer">Unread pipe buffer.</param>
        /// <param name="body">Accumulated body writer.</param>
        /// <param name="maxBodyBytes">Maximum decoded body size (0 disables the limit).</param>
        /// <param name="accumulate">When <see langword="false"/>, line payloads are not copied.</param>
        /// <returns>Consumed position, whether the lone-dot terminator was found, the updated body writer, and whether the size limit was exceeded.</returns>
        private static (SequencePosition Consumed, bool Terminated, ArrayBufferWriter<byte> Body, bool ExceededMaxSize) ProcessAvailableLines(
            ReadOnlySequence<byte> buffer,
            ArrayBufferWriter<byte> body,
            long maxBodyBytes,
            bool accumulate)
        {
            SequencePosition consumed = buffer.Start;
            SequenceReader<byte> sequenceReader = new(buffer);
            bool terminated = false;

            while (sequenceReader.TryReadTo(out ReadOnlySequence<byte> line, (byte)'\n', advancePastDelimiter: true))
            {
                consumed = sequenceReader.Position;
                if (ProcessLine(line, body, maxBodyBytes, accumulate, out body, out bool lineTerminated, out bool exceededMaxSize))
                {
                    if (exceededMaxSize)
                    {
                        return (consumed, false, body, true);
                    }

                    terminated = lineTerminated;
                    break;
                }
            }

            return (consumed, terminated, body, false);
        }

        /// <summary>
        /// Processes one CRLF-delimited line from the pipe buffer.
        /// </summary>
        /// <param name="line">Line bytes without the trailing <c>LF</c> (and without <c>CR</c> when present).</param>
        /// <param name="body">Accumulated body writer.</param>
        /// <param name="maxBodyBytes">Maximum decoded body size (0 disables the limit).</param>
        /// <param name="accumulate">When <see langword="false"/>, line payloads are not copied.</param>
        /// <param name="updatedBody">Writer after appending the line, when not terminated.</param>
        /// <param name="terminated">Whether the lone-dot terminator was consumed.</param>
        /// <param name="exceededMaxSize">Whether appending the line would exceed <paramref name="maxBodyBytes"/>.</param>
        /// <returns><see langword="true"/> when reading should stop for this buffer.</returns>
        private static bool ProcessLine(
            ReadOnlySequence<byte> line,
            ArrayBufferWriter<byte> body,
            long maxBodyBytes,
            bool accumulate,
            out ArrayBufferWriter<byte> updatedBody,
            out bool terminated,
            out bool exceededMaxSize)
        {
            line = StripCarriageReturn(line);
            if (IsDotTerminator(line))
            {
                updatedBody = body;
                terminated = true;
                exceededMaxSize = false;
                return true;
            }

            if (accumulate)
            {
                ReadOnlySequence<byte> payload = line;
                if (IsDotStuffed(line))
                {
                    payload = line.Slice(1);
                }

                if (!FitsWithinMaxSize(body, payload, maxBodyBytes))
                {
                    updatedBody = body;
                    terminated = false;
                    exceededMaxSize = true;
                    return true;
                }

                AppendSequence(body, payload);
                body.Write(Crlf);
            }

            updatedBody = body;
            terminated = false;
            exceededMaxSize = false;
            return false;
        }

        /// <summary>
        /// Strips a trailing <c>CR</c> from a line sequence when present.
        /// </summary>
        /// <param name="line">Line sequence.</param>
        /// <returns>Line without trailing <c>CR</c>.</returns>
        private static ReadOnlySequence<byte> StripCarriageReturn(ReadOnlySequence<byte> line)
        {
            if (line.Length == 0)
            {
                return line;
            }

            if (line.IsSingleSegment)
            {
                ReadOnlySpan<byte> span = line.First.Span;
                return span[^1] == (byte)'\r' ? line.Slice(0, line.Length - 1) : line;
            }

            ReadOnlySpan<byte> last = line.Slice(line.Length - 1, 1).First.Span;
            return last[0] == (byte)'\r' ? line.Slice(0, line.Length - 1) : line;
        }

        /// <summary>
        /// Determines whether a line is the dot-stuffed body terminator.
        /// </summary>
        /// <param name="line">Line without CRLF.</param>
        /// <returns><see langword="true"/> when the line is exactly <c>.</c>.</returns>
        private static bool IsDotTerminator(ReadOnlySequence<byte> line) =>
            line.Length == 1 && line.First.Span[0] == (byte)'.';

        /// <summary>
        /// Determines whether a line is dot-stuffed (leading <c>..</c>).
        /// </summary>
        /// <param name="line">Line without CRLF.</param>
        /// <returns><see langword="true"/> when the line begins with <c>..</c>.</returns>
        private static bool IsDotStuffed(ReadOnlySequence<byte> line)
        {
            if (line.Length < 2)
            {
                return false;
            }

            if (line.First.Length >= 2)
            {
                ReadOnlySpan<byte> span = line.First.Span;
                return span[0] == (byte)'.' && span[1] == (byte)'.';
            }

            return line.First.Span[0] == (byte)'.' && line.Slice(1, 1).First.Span[0] == (byte)'.';
        }

        /// <summary>
        /// Determines whether appending a payload and trailing CRLF would stay within <paramref name="maxBodyBytes"/>.
        /// </summary>
        /// <param name="body">Accumulated body writer.</param>
        /// <param name="payload">Line payload to append.</param>
        /// <param name="maxBodyBytes">Maximum decoded body size (0 disables the limit).</param>
        /// <returns><see langword="true"/> when the append is allowed.</returns>
        private static bool FitsWithinMaxSize(
            ArrayBufferWriter<byte> body,
            ReadOnlySequence<byte> payload,
            long maxBodyBytes)
        {
            if (maxBodyBytes <= 0)
            {
                return true;
            }

            long projected = body.WrittenCount + payload.Length + Crlf.Length;
            return projected <= maxBodyBytes;
        }

        /// <summary>
        /// Appends a <see cref="ReadOnlySequence{T}"/> to the body writer.
        /// </summary>
        /// <param name="body">Destination writer.</param>
        /// <param name="sequence">Bytes to append.</param>
        private static void AppendSequence(ArrayBufferWriter<byte> body, ReadOnlySequence<byte> sequence)
        {
            if (sequence.IsSingleSegment)
            {
                body.Write(sequence.First.Span);
                return;
            }

            foreach (ReadOnlyMemory<byte> segment in sequence)
            {
                body.Write(segment.Span);
            }
        }
    }
}
