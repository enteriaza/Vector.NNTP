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
    /// </remarks>
    internal sealed class PrefixedReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly bool _leaveInnerOpen;
        private ReadOnlyMemory<byte> _prefix;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="PrefixedReadStream"/> class.
        /// </summary>
        /// <param name="inner">Inner stream.</param>
        /// <param name="prefix">Prefix bytes to replay before the inner stream.</param>
        /// <param name="leaveInnerOpen">Whether to leave the inner stream open on dispose.</param>
        public PrefixedReadStream(Stream inner, ReadOnlyMemory<byte> prefix, bool leaveInnerOpen)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _prefix = prefix;
            _leaveInnerOpen = leaveInnerOpen;
        }

        /// <inheritdoc />
        public override bool CanRead => !_disposed && _inner.CanRead;

        /// <inheritdoc />
        public override bool CanSeek => false;

        /// <inheritdoc />
        public override bool CanWrite => !_disposed && _inner.CanWrite;

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
            ObjectDisposedException.ThrowIf(_disposed, this);
            _inner.Flush();
        }

        /// <inheritdoc />
        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _inner.FlushAsync(cancellationToken);
        }

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
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

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _inner.WriteAsync(buffer, cancellationToken);
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

        /// <inheritdoc />
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

