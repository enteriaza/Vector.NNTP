// <copyright file="NntpZLibSessionStream.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: bidirectional ZLIB compression wrapper for RFC 8054 COMPRESS DEFLATE.

namespace Vector.NNTP.Sockets.Compression
{
    using System.IO.Compression;

    /// <summary>
    /// Wraps a bidirectional stream with independent ZLIB compress and decompress contexts per RFC 8054.
    /// </summary>
    /// <remarks>
    /// <para>After COMPRESS DEFLATE succeeds, all subsequent bytes on the connection use ZLIB framing in each direction.</para>
    /// </remarks>
    internal sealed class NntpZLibSessionStream : Stream
    {
        private readonly Stream _inner;
        private readonly ZLibStream _reader;
        private readonly ZLibStream _writer;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="NntpZLibSessionStream"/> class.
        /// </summary>
        /// <param name="inner">Underlying cleartext or TLS-protected stream.</param>
        public NntpZLibSessionStream(Stream inner)
        {
            this._inner = inner ?? throw new ArgumentNullException(nameof(inner));
            this._reader = new ZLibStream(inner, CompressionMode.Decompress, leaveOpen: true);
            this._writer = new ZLibStream(inner, CompressionMode.Compress, leaveOpen: true);
        }

        /// <inheritdoc />
        public override bool CanRead => this._reader.CanRead;

        /// <inheritdoc />
        public override bool CanSeek => false;

        /// <inheritdoc />
        public override bool CanWrite => this._writer.CanWrite;

        /// <inheritdoc />
        public override long Length => throw new NotSupportedException();

        /// <inheritdoc />
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override void Flush() => this._writer.Flush();

        /// <inheritdoc />
        public override Task FlushAsync(CancellationToken cancellationToken) => this._writer.FlushAsync(cancellationToken);

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count) => this._reader.Read(buffer, offset, count);

        /// <inheritdoc />
        public override int Read(Span<byte> buffer) => this._reader.Read(buffer);

        /// <inheritdoc />
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            this._reader.ReadAsync(buffer, offset, count, cancellationToken);

        /// <inheritdoc />
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            this._reader.ReadAsync(buffer, cancellationToken);

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        /// <inheritdoc />
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count) => this._writer.Write(buffer, offset, count);

        /// <inheritdoc />
        public override void Write(ReadOnlySpan<byte> buffer) => this._writer.Write(buffer);

        /// <inheritdoc />
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            this._writer.WriteAsync(buffer, offset, count, cancellationToken);

        /// <inheritdoc />
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            this._writer.WriteAsync(buffer, cancellationToken);

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing && !this._disposed)
            {
                this._disposed = true;
                this._reader.Dispose();
                this._writer.Dispose();
            }

            base.Dispose(disposing);
        }

        /// <inheritdoc />
        public override async ValueTask DisposeAsync()
        {
            if (!this._disposed)
            {
                this._disposed = true;
                await this._reader.DisposeAsync().ConfigureAwait(false);
                await this._writer.DisposeAsync().ConfigureAwait(false);
            }

            await base.DisposeAsync().ConfigureAwait(false);
        }
    }
}
