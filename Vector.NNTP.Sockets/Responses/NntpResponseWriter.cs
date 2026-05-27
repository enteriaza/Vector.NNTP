// <copyright file="NntpResponseWriter.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: writes pre-encoded and formatted responses; counts Tx bytes.

namespace Vector.NNTP.Sockets.Responses
{
    using Session;

    /// <summary>
    /// Writes NNTP responses to the session transport and updates byte accounting.
    /// </summary>
    public sealed class NntpResponseWriter
    {
        private readonly PipeWriter _writer;
        private readonly NntpConnectionContext _context;

        /// <summary>
        /// Initializes a new instance of the <see cref="NntpResponseWriter"/> class.
        /// </summary>
        /// <param name="writer">Pipe writer for the connection.</param>
        /// <param name="context">Connection context for Tx byte accounting.</param>
        public NntpResponseWriter(PipeWriter writer, NntpConnectionContext context)
        {
            this._writer = writer ?? throw new ArgumentNullException(nameof(writer));
            this._context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <summary>
        /// Writes a pre-encoded response including CRLF.
        /// </summary>
        /// <param name="payload">Pre-encoded bytes.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="ValueTask"/> that completes when flushed.</returns>
        public async ValueTask WritePreencodedAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
        {
            this._context.AddTxBytes(payload.Length);
            payload.CopyTo(this._writer.GetMemory(payload.Length));
            this._writer.Advance(payload.Length);
            FlushResult flush = await this._writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            if (flush.IsCanceled)
            {
                throw new OperationCanceledException(cancellationToken);
            }
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
            int byteCount = Encoding.ASCII.GetByteCount(line) + 2;
            this._context.AddTxBytes(byteCount);
            byte[] buffer = ArrayPool<byte>.Shared.Rent(byteCount);
            try
            {
                int written = Encoding.ASCII.GetBytes(line, buffer);
                buffer[written++] = (byte)'\r';
                buffer[written++] = (byte)'\n';
                buffer.AsSpan(0, written).CopyTo(this._writer.GetSpan(written));
                this._writer.Advance(written);
                FlushResult flush = await this._writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                if (flush.IsCanceled)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
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
            await this.WriteLineAsync(initialLine, cancellationToken).ConfigureAwait(false);
            foreach (string line in bodyLines)
            {
                await this.WriteLineAsync(line, cancellationToken).ConfigureAwait(false);
            }

            await this.WriteLineAsync(".", cancellationToken).ConfigureAwait(false);
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
                        this._context.AddTxBytes(offset);
                        buffer.AsMemory(0, offset).CopyTo(this._writer.GetMemory(offset));
                        this._writer.Advance(offset);
                        FlushResult flush = await this._writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                        if (flush.IsCanceled)
                        {
                            throw new OperationCanceledException(cancellationToken);
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }
                }

                lineStart = i + 1;
            }

            await this.WriteLineAsync(".", cancellationToken).ConfigureAwait(false);
        }
    }
}
