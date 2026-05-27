// <copyright file="NntpZLibSessionStream.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: bidirectional ZLIB compression wrapper for RFC 8054 COMPRESS DEFLATE.

using System.IO.Compression;

namespace Vector.NNTP.Sockets.Compression
{
    /// <summary>
    /// Wraps a bidirectional stream with independent ZLIB compress and decompress contexts per RFC 8054.
    /// </summary>
    /// <remarks>
    /// <para>After COMPRESS DEFLATE succeeds, all subsequent bytes on the connection use ZLIB framing in each direction.</para>
    /// </remarks>
    /// <remarks>
    /// Initializes a new instance of the <see cref="NntpZLibSessionStream"/> class.
    /// </remarks>
    /// <param name="inner">Underlying cleartext or TLS-protected stream.</param>
    internal sealed class NntpZLibSessionStream(Stream inner) : Stream
    {
        private readonly Stream _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        private readonly ZLibStream _reader = new(inner, CompressionMode.Decompress, leaveOpen: true);
        private readonly ZLibStream _writer = new(inner, CompressionMode.Compress, leaveOpen: true);
        private bool _disposed;

        /// <inheritdoc />
        public override bool CanRead => _reader.CanRead;

        /// <inheritdoc />
        public override bool CanSeek => false;

        /// <inheritdoc />
        public override bool CanWrite => _writer.CanWrite;

        /// <inheritdoc />
        public override long Length => throw new NotSupportedException();

        /// <inheritdoc />
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override void Flush()
        {
            _writer.Flush();
        }

        /// <inheritdoc />
        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return _writer.FlushAsync(cancellationToken);
        }

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count)
        {
            return _reader.Read(buffer, offset, count);
        }

        /// <inheritdoc />
        public override int Read(Span<byte> buffer)
        {
            return _reader.Read(buffer);
        }

        /// <inheritdoc />
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return _reader.ReadAsync(buffer, offset, count, cancellationToken);
        }

        /// <inheritdoc />
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return _reader.ReadAsync(buffer, cancellationToken);
        }

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count)
        {
            _writer.Write(buffer, offset, count);
        }

        /// <inheritdoc />
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _writer.Write(buffer);
        }

        /// <inheritdoc />
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return _writer.WriteAsync(buffer, offset, count, cancellationToken);
        }

        /// <inheritdoc />
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return _writer.WriteAsync(buffer, cancellationToken);
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                _reader.Dispose();
                _writer.Dispose();
            }

            base.Dispose(disposing);
        }

        /// <inheritdoc />
        public override async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                await _reader.DisposeAsync().ConfigureAwait(false);
                await _writer.DisposeAsync().ConfigureAwait(false);
            }

            await base.DisposeAsync().ConfigureAwait(false);
        }
    }
}
