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

        /// <summary>
        /// Gets a value indicating whether the stream can be read from.
        /// </summary>
        /// <returns>A value indicating whether the stream can be read from.</returns>
        public override bool CanRead => _reader.CanRead;

        /// <summary>
        /// Gets a value indicating whether the stream can be seeked.
        /// </summary>
        /// <returns>A value indicating whether the stream can be seeked.</returns>
        public override bool CanSeek => false;

        /// <summary>
        /// Gets a value indicating whether the stream can be written to.
        /// </summary>
        /// <returns>A value indicating whether the stream can be written to.</returns>
        public override bool CanWrite => _writer.CanWrite;

        /// <summary>
        /// Gets the length of the stream.
        /// </summary>
        /// <returns>The length of the stream.</returns>
        public override long Length => throw new NotSupportedException();

        /// <summary>
        /// Gets the position of the stream.
        /// </summary>
        /// <returns>The position of the stream.</returns>
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /// <summary>
        /// Flushes the stream.
        /// </summary>
        public override void Flush()
        {
            _writer.Flush();
        }

        /// <summary>
        /// Flushes the stream asynchronously.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the stream is flushed.</returns>
        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return _writer.FlushAsync(cancellationToken);
        }

        /// <summary>
        /// Reads a sequence of bytes from the stream into the buffer.
        /// </summary>
        /// <param name="buffer">The buffer to read the bytes into.</param>
        /// <param name="offset">The offset in the buffer to start reading the bytes into.</param>
        /// <param name="count">The number of bytes to read from the stream.</param>
        /// <returns>The number of bytes read from the stream.</returns>
        public override int Read(byte[] buffer, int offset, int count)
        {
            return _reader.Read(buffer, offset, count);
        }

        /// <summary>
        /// Reads a sequence of bytes from the stream into the buffer.
        /// </summary>
        /// <param name="buffer">The buffer to read the bytes into.</param>
        /// <returns>The number of bytes read from the stream.</returns>
        public override int Read(Span<byte> buffer)
        {
            return _reader.Read(buffer);
        }

        /// <summary>
        /// Reads a sequence of bytes from the stream into the buffer asynchronously.
        /// </summary>
        /// <param name="buffer">The buffer to read the bytes into.</param>
        /// <param name="offset">The offset in the buffer to start reading the bytes into.</param>
        /// <param name="count">The number of bytes to read from the stream.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the bytes are read from the stream.</returns>
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return _reader.ReadAsync(buffer, offset, count, cancellationToken);
        }

        /// <summary>
        /// Reads a sequence of bytes from the stream into the buffer asynchronously.
        /// </summary>
        /// <param name="buffer">The buffer to read the bytes into.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the bytes are read from the stream.</returns>
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return _reader.ReadAsync(buffer, cancellationToken);
        }

        /// <summary>
        /// Seeks to a new position in the stream.
        /// </summary>
        /// <param name="offset">The offset to seek to.</param>
        /// <param name="origin">The origin of the seek.</param>
        /// <returns>The new position in the stream.</returns>
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Sets the length of the stream.
        /// </summary>
        /// <param name="value">The new length of the stream.</param>
        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Writes a sequence of bytes to the stream.
        /// </summary>
        /// <param name="buffer">The buffer to write the bytes from.</param>
        /// <param name="offset">The offset in the buffer to start writing the bytes from.</param>
        /// <param name="count">The number of bytes to write to the stream.</param>
        public override void Write(byte[] buffer, int offset, int count)
        {
            _writer.Write(buffer, offset, count);
        }

        /// <summary>
        /// Writes a sequence of bytes to the stream.
        /// </summary>
        /// <param name="buffer">The buffer to write the bytes from.</param>
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _writer.Write(buffer);
        }

        /// <summary>
        /// Writes a sequence of bytes to the stream asynchronously.
        /// </summary>
        /// <param name="buffer">The buffer to write the bytes from.</param>
        /// <param name="offset">The offset in the buffer to start writing the bytes from.</param>
        /// <param name="count">The number of bytes to write to the stream.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the bytes are written to the stream.</returns>
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return _writer.WriteAsync(buffer, offset, count, cancellationToken);
        }

        /// <summary>
        /// Writes a sequence of bytes to the stream asynchronously.
        /// </summary>
        /// <param name="buffer">The buffer to write the bytes from.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the bytes are written to the stream.</returns>
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return _writer.WriteAsync(buffer, cancellationToken);
        }

        /// <summary>
        /// Disposes the stream.
        /// </summary>
        /// <param name="disposing">Whether the stream is being disposed.</param>
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

        /// <summary>
        /// Disposes the stream asynchronously.
        /// </summary>
        /// <returns>A task that completes when the stream is disposed.</returns>
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
