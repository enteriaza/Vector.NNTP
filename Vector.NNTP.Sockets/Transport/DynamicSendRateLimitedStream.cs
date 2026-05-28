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

        /// <summary>
        /// Gets a value indicating whether the stream can read.
        /// </summary>
        /// <returns>True if the stream can read, false otherwise.</returns>
        public override bool CanRead => inner.CanRead;

        /// <summary>
        /// Gets a value indicating whether the stream can seek.
        /// </summary>
        /// <returns>True if the stream can seek, false otherwise.</returns>
        public override bool CanSeek => false;

        /// <summary>
        /// Gets a value indicating whether the stream can write.
        /// </summary>
        /// <returns>True if the stream can write, false otherwise.</returns>
        public override bool CanWrite => inner.CanWrite;

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

        /// <summary>
        /// Flushes the stream.
        /// </summary>
        /// <returns>A task that completes when the stream is flushed.</returns>
        public override void Flush()
        {
            inner.Flush();
        }

        /// <summary>
        /// Flushes the stream asynchronously.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the stream is flushed.</returns>
        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return inner.FlushAsync(cancellationToken);
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
            throw new NotSupportedException();
        }

        /// <summary>
        /// Reads a sequence of bytes from the stream into the buffer asynchronously.
        /// </summary>
        /// <param name="buffer">The buffer to read the bytes into.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the bytes are read from the stream.</returns>
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return inner.ReadAsync(buffer, cancellationToken);
        }

        /// <summary>
        /// Writes a sequence of bytes to the stream.
        /// </summary>
        /// <param name="buffer">The buffer to write the bytes from.</param>
        /// <param name="offset">The offset in the buffer to start writing the bytes from.</param>
        /// <param name="count">The number of bytes to write to the stream.</param>
        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Writes a sequence of bytes to the stream asynchronously.
        /// </summary>
        /// <param name="buffer">The buffer to write the bytes from.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the bytes are written to the stream.</returns>
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
        /// Disposes the stream.
        /// </summary>
        /// <param name="disposing">Whether the stream is being disposed.</param>
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

        /// <summary>
        /// Disposes the stream asynchronously.
        /// </summary>
        /// <returns>A task that completes when the stream is disposed.</returns>
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

        /// <summary>
        /// Throttles the write to the stream.
        /// </summary>
        /// <param name="nextBytes">The number of bytes to write to the stream.</param>
        /// <param name="cap">The cap in bytes per second.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the write is throttled.</returns>
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
