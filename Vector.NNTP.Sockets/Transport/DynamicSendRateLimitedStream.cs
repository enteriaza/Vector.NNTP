// <copyright file="DynamicSendRateLimitedStream.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Sockets.Transport
{
    /// <summary>
    /// Applies a dynamically adjustable outbound bytes-per-second cap.
    /// </summary>
    public sealed class DynamicSendRateLimitedStream : Stream, IDynamicSendRateLimiter
    {
        private readonly Stream inner;
        private readonly bool leaveInnerOpen;
        private long maxSendBytesPerSecond;
        private long writeWindowStartTicks = Environment.TickCount64;
        private long writeBytesInWindow;
        private bool disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="DynamicSendRateLimitedStream"/> class.
        /// </summary>
        /// <param name="inner">Underlying stream.</param>
        /// <param name="initialMaxSendBytesPerSecond">Initial cap in bytes per second.</param>
        /// <param name="leaveInnerOpen">Whether to leave the inner stream open on dispose.</param>
        public DynamicSendRateLimitedStream(Stream inner, long initialMaxSendBytesPerSecond, bool leaveInnerOpen)
        {
            ArgumentNullException.ThrowIfNull(inner);
            this.inner = inner;
            maxSendBytesPerSecond = initialMaxSendBytesPerSecond;
            this.leaveInnerOpen = leaveInnerOpen;
        }

        /// <inheritdoc />
        public override bool CanRead => inner.CanRead;

        /// <inheritdoc />
        public override bool CanSeek => false;

        /// <inheritdoc />
        public override bool CanWrite => inner.CanWrite;

        /// <inheritdoc />
        public override long Length => throw new NotSupportedException();

        /// <inheritdoc />
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /// <summary>
        /// Gets the current max send bytes per second.
        /// </summary>
        public long MaxSendBytesPerSecond => Interlocked.Read(ref maxSendBytesPerSecond);

        /// <summary>
        /// Updates the outbound rate cap.
        /// </summary>
        /// <param name="newMaxSendBytesPerSecond">New cap in bytes per second.</param>
        public void UpdateMaxSendBytesPerSecond(long newMaxSendBytesPerSecond)
        {
            _ = Interlocked.Exchange(ref maxSendBytesPerSecond, newMaxSendBytesPerSecond);
        }

        /// <inheritdoc />
        public override void Flush()
        {
            inner.Flush();
        }

        /// <inheritdoc />
        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return inner.FlushAsync(cancellationToken);
        }

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return inner.ReadAsync(buffer, cancellationToken);
        }

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            long cap = Interlocked.Read(ref maxSendBytesPerSecond);
            if (buffer.Length <= 0 || cap <= 0)
            {
                await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
                return;
            }

            int offset = 0;
            while (offset < buffer.Length)
            {
                cap = Interlocked.Read(ref maxSendBytesPerSecond);
                if (cap <= 0)
                {
                    await inner.WriteAsync(buffer[offset..], cancellationToken).ConfigureAwait(false);
                    return;
                }

                int sliceBytes = buffer.Length - offset;
                if (sliceBytes > cap)
                {
                    sliceBytes = cap > int.MaxValue ? int.MaxValue : (int)cap;
                }

                await ThrottleWriteAsync(sliceBytes, cap, cancellationToken).ConfigureAwait(false);
                await inner.WriteAsync(buffer.Slice(offset, sliceBytes), cancellationToken).ConfigureAwait(false);
                writeBytesInWindow += sliceBytes;
                offset += sliceBytes;
            }
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
            if (disposing && !disposed)
            {
                disposed = true;
                if (!leaveInnerOpen)
                {
                    inner.Dispose();
                }
            }

            base.Dispose(disposing);
        }

        /// <inheritdoc />
        public override async ValueTask DisposeAsync()
        {
            if (!disposed)
            {
                disposed = true;
                if (!leaveInnerOpen)
                {
                    await inner.DisposeAsync().ConfigureAwait(false);
                }
            }

            await base.DisposeAsync().ConfigureAwait(false);
        }

        private async Task ThrottleWriteAsync(int nextBytes, long cap, CancellationToken cancellationToken)
        {
            while (true)
            {
                long now = Environment.TickCount64;
                if (now - writeWindowStartTicks >= 1000)
                {
                    writeWindowStartTicks = now;
                    writeBytesInWindow = 0;
                }

                if (writeBytesInWindow + nextBytes <= cap)
                {
                    return;
                }

                await Task.Delay(25, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
