// <copyright file="NntpResponseWriter.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: writes pre-encoded and formatted responses; counts Tx bytes.

using Microsoft.Extensions.Logging;
using Vector.NNTP.Sockets.Session;

namespace Vector.NNTP.Sockets.Responses
{
    /// <summary>
    /// Writes NNTP responses to the session transport and updates byte accounting.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="NntpResponseWriter"/> class.
    /// </remarks>
    /// <param name="writer">Pipe writer for the connection.</param>
    /// <param name="context">Connection context for Tx byte accounting.</param>
    /// <param name="logger">Logger for response logging.</param>
    public sealed class NntpResponseWriter(PipeWriter writer, NntpConnectionContext context, ILogger? logger = null)
    {
        private readonly PipeWriter _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        private readonly NntpConnectionContext _context = context ?? throw new ArgumentNullException(nameof(context));
        private readonly ILogger? _logger = logger;

        /// <summary>
        /// Writes a pre-encoded response including CRLF.
        /// </summary>
        /// <param name="payload">Pre-encoded bytes.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="ValueTask"/> that completes when flushed.</returns>
        public async ValueTask WritePreencodedAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
        {
            if (_logger is not null)
            {
                string? firstLine = ExtractFirstLineFromBytes(payload);
                if (firstLine is not null)
                {
                    _logger.LogResponseLine(firstLine);
                }
            }

            _context.AddTxBytes(payload.Length);
            payload.CopyTo(_writer.GetMemory(payload.Length));
            _writer.Advance(payload.Length);
            await FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Writes a single-line response (adds CRLF).
        /// </summary>
        /// <param name="line">Line without CRLF.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="ValueTask"/> that completes when flushed.</returns>
        public async ValueTask WriteLineAsync(string line, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(line);
            _logger?.LogResponseLine(line);
            int byteCount = Encoding.ASCII.GetByteCount(line) + 2;
            _context.AddTxBytes(byteCount);
            byte[] buffer = ArrayPool<byte>.Shared.Rent(byteCount);
            try
            {
                int written = Encoding.ASCII.GetBytes(line, buffer);
                buffer[written++] = (byte)'\r';
                buffer[written++] = (byte)'\n';
                buffer.AsSpan(0, written).CopyTo(_writer.GetSpan(written));
                _writer.Advance(written);
                await FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        /// <summary>
        /// Writes multiple lines terminated by CRLF, ending with <c>.</c> on its own line for multi-line responses.
        /// </summary>
        /// <param name="initialLine">First status line without CRLF.</param>
        /// <param name="bodyLines">Subsequent lines without CRLF.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="ValueTask"/> that completes when flushed.</returns>
        public async ValueTask WriteMultiLineAsync(string initialLine, IReadOnlyList<string> bodyLines, CancellationToken cancellationToken)
        {
            _logger?.LogResponseLine(initialLine);
            WriteLineNoFlush(initialLine);
            foreach (string line in bodyLines)
            {
                WriteLineNoFlush(line);
            }

            WriteLineNoFlush(".");
            await FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Writes a dot-stuffed article body (binary-safe) terminated by a lone <c>.</c> line.
        /// </summary>
        /// <param name="body">Raw article bytes including header/body separator.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="ValueTask"/> that completes when flushed.</returns>
        public async ValueTask WriteDotStuffedArticleBodyAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
        {
            byte[] bytes = body.ToArray();
            int lineStart = 0;
            for (int i = 0; i <= bytes.Length; i++)
            {
                if (i < bytes.Length && bytes[i] != (byte)'\n')
                {
                    continue;
                }

                int lineEnd = i;
                if (lineEnd > lineStart && bytes[lineEnd - 1] == (byte)'\r')
                {
                    lineEnd--;
                }

                if (lineEnd > lineStart)
                {
                    int lineLength = lineEnd - lineStart;
                    int extra = bytes[lineStart] == (byte)'.' ? 1 : 0;
                    int total = lineLength + extra + 2;
                    byte[] buffer = ArrayPool<byte>.Shared.Rent(total);
                    try
                    {
                        int offset = 0;
                        if (extra > 0)
                        {
                            buffer[offset++] = (byte)'.';
                        }

                        Buffer.BlockCopy(bytes, lineStart, buffer, offset, lineLength);
                        offset += lineLength;
                        buffer[offset++] = (byte)'\r';
                        buffer[offset++] = (byte)'\n';
                        _context.AddTxBytes(offset);
                        buffer.AsMemory(0, offset).CopyTo(_writer.GetMemory(offset));
                        _writer.Advance(offset);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }
                }

                lineStart = i + 1;
            }

            WriteLineNoFlush(".");
            await FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        private void WriteLineNoFlush(string line)
        {
            ArgumentNullException.ThrowIfNull(line);
            int byteCount = Encoding.ASCII.GetByteCount(line) + 2;
            _context.AddTxBytes(byteCount);
            byte[] buffer = ArrayPool<byte>.Shared.Rent(byteCount);
            try
            {
                int written = Encoding.ASCII.GetBytes(line, buffer);
                buffer[written++] = (byte)'\r';
                buffer[written++] = (byte)'\n';
                buffer.AsSpan(0, written).CopyTo(_writer.GetSpan(written));
                _writer.Advance(written);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        /// <summary>
        /// Extracts the first line from pre-encoded bytes (up to CRLF or LF).
        /// </summary>
        /// <param name="bytes">The pre-encoded bytes.</param>
        /// <returns>The first line as a string, or null if extraction fails.</returns>
        private static string? ExtractFirstLineFromBytes(ReadOnlyMemory<byte> bytes)
        {
            ReadOnlySpan<byte> span = bytes.Span;
            int crlfIndex = -1;

            for (int i = 0; i < span.Length - 1; i++)
            {
                if (span[i] == (byte)'\r' && span[i + 1] == (byte)'\n')
                {
                    crlfIndex = i;
                    break;
                }
            }

            if (crlfIndex < 0)
            {
                // Try just LF
                for (int i = 0; i < span.Length; i++)
                {
                    if (span[i] == (byte)'\n')
                    {
                        crlfIndex = i;
                        break;
                    }
                }
            }

            if (crlfIndex >= 0)
            {
                try
                {
                    return Encoding.ASCII.GetString(span[..crlfIndex]);
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }

        private async ValueTask FlushAsync(CancellationToken cancellationToken)
        {
            FlushResult flush = await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            if (flush.IsCanceled)
            {
                throw new OperationCanceledException(cancellationToken);
            }
        }
    }
}
