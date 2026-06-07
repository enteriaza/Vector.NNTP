// <copyright file="PrefixedReadStream.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: stream wrapper that replays a read prefix.

namespace Vector.NNTP.Sockets.Transport
{
    /// <summary>
    /// Wraps an inner stream and replays a fixed prefix before delegating reads to the inner stream.
    /// </summary>
    /// <remarks>
    /// This is used to safely consume a connection preamble (for example HAProxy PROXY) while preserving any
    /// bytes already read beyond the preamble for the subsequent protocol consumer (TLS handshake or NNTP session).
    /// Only the async <see cref="ReadAsync(Memory{byte}, CancellationToken)"/> path is supported for reads.
    /// </remarks>
    internal sealed class PrefixedReadStream : Stream
    {
        /// <summary>
        /// Underlying stream receiving reads after the prefix is exhausted.
        /// </summary>
        private readonly Stream _inner;

        /// <summary>
        /// When <see langword="true"/>, disposal does not close <see cref="_inner"/>.
        /// </summary>
        private readonly bool _leaveInnerOpen;

        /// <summary>
        /// Remaining prefix bytes replayed before the first inner read.
        /// </summary>
        private ReadOnlyMemory<byte> _prefix;

        /// <summary>
        /// Indicates whether this wrapper has been disposed.
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="PrefixedReadStream"/> class.
        /// </summary>
        /// <param name="inner">Inner stream.</param>
        /// <param name="prefix">Prefix bytes to replay before the inner stream.</param>
        /// <param name="leaveInnerOpen">Whether to leave the inner stream open on dispose.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="inner"/> is null.</exception>
        public PrefixedReadStream(Stream inner, ReadOnlyMemory<byte> prefix, bool leaveInnerOpen)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _prefix = prefix;
            _leaveInnerOpen = leaveInnerOpen;
        }

        /// <summary>
        /// Gets a value indicating whether reads are supported and the stream is not disposed.
        /// </summary>
        public override bool CanRead => !_disposed && _inner.CanRead;

        /// <summary>
        /// Gets a value indicating whether seeking is supported.
        /// </summary>
        /// <remarks>Always <see langword="false"/>.</remarks>
        public override bool CanSeek => false;

        /// <summary>
        /// Gets a value indicating whether writes are supported when not disposed.
        /// </summary>
        public override bool CanWrite => !_disposed && _inner.CanWrite;

        /// <summary>
        /// Gets the stream length.
        /// </summary>
        /// <exception cref="NotSupportedException">Always thrown.</exception>
        public override long Length => throw new NotSupportedException();

        /// <summary>
        /// Gets the stream position.
        /// </summary>
        /// <exception cref="NotSupportedException">Always thrown on get or set.</exception>
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /// <summary>
        /// Flushes the inner stream when not disposed.
        /// </summary>
        /// <exception cref="ObjectDisposedException">Thrown when the wrapper is disposed.</exception>
        public override void Flush()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _inner.Flush();
        }

        /// <summary>
        /// Asynchronously flushes the inner stream when not disposed.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that completes when the inner flush finishes.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when the wrapper is disposed.</exception>
        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _inner.FlushAsync(cancellationToken);
        }

        /// <summary>
        /// Synchronous reads are not supported on this wrapper.
        /// </summary>
        /// <exception cref="NotSupportedException">Always thrown.</exception>
        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Reads from the prefix buffer first, then delegates to the inner stream.
        /// </summary>
        /// <param name="buffer">Destination buffer.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Bytes copied from the prefix or read from the inner stream.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when the wrapper is disposed.</exception>
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_prefix.Length > 0)
            {
                int toCopy = buffer.Length < _prefix.Length ? buffer.Length : _prefix.Length;
                _prefix.Span.Slice(0, toCopy).CopyTo(buffer.Span);
                _prefix = _prefix.Slice(toCopy);
                return new ValueTask<int>(toCopy);
            }

            return _inner.ReadAsync(buffer, cancellationToken);
        }

        /// <summary>
        /// Synchronous writes are not supported on this wrapper.
        /// </summary>
        /// <exception cref="NotSupportedException">Always thrown.</exception>
        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Delegates asynchronous writes to the inner stream when not disposed.
        /// </summary>
        /// <param name="buffer">Bytes to write.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that completes when the inner write finishes.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when the wrapper is disposed.</exception>
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _inner.WriteAsync(buffer, cancellationToken);
        }

        /// <summary>
        /// Seeking is not supported on this forward-only wrapper.
        /// </summary>
        /// <exception cref="NotSupportedException">Always thrown.</exception>
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Setting length is not supported on this wrapper.
        /// </summary>
        /// <exception cref="NotSupportedException">Always thrown.</exception>
        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Disposes the inner stream when <paramref name="leaveInnerOpen"/> was <see langword="false"/> at construction.
        /// </summary>
        /// <param name="disposing">Whether dispose was invoked from <see cref="IDisposable.Dispose"/>.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                if (!_leaveInnerOpen)
                {
                    _inner.Dispose();
                }
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// Asynchronously disposes the inner stream when <paramref name="leaveInnerOpen"/> was <see langword="false"/> at construction.
        /// </summary>
        /// <returns>A task that completes when disposal finishes.</returns>
        public override async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                if (!_leaveInnerOpen)
                {
                    await _inner.DisposeAsync().ConfigureAwait(false);
                }
            }

            await base.DisposeAsync().ConfigureAwait(false);
        }
    }
}
