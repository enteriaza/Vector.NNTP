// <copyright file="LengthLimitedReadStream.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// LengthLimitedReadStream.cs -- Read-only Stream decorator that enforces a maximum cumulative byte limit on the inner stream.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace Vector.NNTP.Utilities.IO;

/// <summary>
/// A read-only <see cref="Stream"/> decorator that enforces a maximum cumulative byte limit on reads from the inner
/// stream.
/// </summary>
/// <remarks>
/// <para><b>Pre-clamped reads:</b> Each read operation clamps the requested buffer size to the remaining allowance
/// before delegating to the inner stream. This ensures the inner stream cannot deliver more bytes than permitted.</para>
///
/// <para><b>Ownership:</b> This wrapper does not own the inner stream; the caller is responsible for disposing the
/// inner stream separately.</para>
/// </remarks>
public sealed class LengthLimitedReadStream : Stream
{
    private const int DefaultCopyBufferSize = 81_920;

    private static readonly Action<ILogger, string, long, long, Exception?> LogLimitExceeded =
        LoggerMessage.Define<string, long, long>(
            LogLevel.Warning,
            new EventId(300, nameof(LogLimitExceeded)),
            "Utilities: Response for {Operation} exceeded the {MaxBytes:N0}-byte safety limit at {TotalBytesRead:N0} bytes read");

    private readonly Stream _inner;
    private readonly long _maxBytes;
    private readonly string _operation;
    private readonly ILogger? _logger;

    private long _totalBytesRead;
    private int _disposed;

#if DEBUG
    private int _activeRead;
#endif

    /// <summary>
    /// Initializes a new instance of the <see cref="LengthLimitedReadStream"/> class.
    /// </summary>
    /// <param name="inner">The underlying stream. Must support reading.</param>
    /// <param name="maxBytes">The maximum number of bytes that may be read before an exception is thrown.</param>
    /// <param name="operation">Short operation descriptor for diagnostics (must not contain secrets).</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="inner"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="inner"/> is not readable or when
    /// <paramref name="operation"/> is null/empty/whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxBytes"/> is not positive.</exception>
    public LengthLimitedReadStream(Stream inner, long maxBytes, string operation, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        if (!inner.CanRead)
        {
            ThrowInnerNotReadable(nameof(inner));
        }

        this._inner = inner;
        this._maxBytes = maxBytes;
        this._operation = operation;
        this._logger = logger;
    }

    /// <inheritdoc/>
    public override bool CanRead => true;

    /// <inheritdoc/>
    public override bool CanSeek => false;

    /// <inheritdoc/>
    public override bool CanWrite => false;

    /// <inheritdoc/>
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc/>
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count) => this.Read(buffer.AsSpan(offset, count));

    /// <inheritdoc/>
    public override int Read(Span<byte> buffer)
    {
        this.ThrowIfDisposed();

#if DEBUG
        this.EnterRead();
#endif

        try
        {
            long remaining = this._maxBytes - this._totalBytesRead;
            if (remaining <= 0)
            {
                this.ThrowLimitExceeded();
            }

            if ((long)buffer.Length > remaining)
            {
                buffer = buffer.Slice(0, (int)remaining);
            }

            int read = this._inner.Read(buffer);
            this._totalBytesRead = checked(this._totalBytesRead + read);
            return read;
        }
        finally
        {
#if DEBUG
            this.ExitRead();
#endif
        }
    }

    /// <inheritdoc/>
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        this.ThrowIfDisposed();

#if DEBUG
        this.EnterRead();
#endif

        try
        {
            long remaining = this._maxBytes - this._totalBytesRead;
            if (remaining <= 0)
            {
                this.ThrowLimitExceeded();
            }

            if ((long)buffer.Length > remaining)
            {
                buffer = buffer.Slice(0, (int)remaining);
            }

            return this.ReadAsyncCore(buffer, cancellationToken);
        }
        finally
        {
#if DEBUG
            this.ExitRead();
#endif
        }
    }

    /// <inheritdoc/>
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => this.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc/>
    public override void CopyTo(Stream destination, int bufferSize)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (bufferSize <= 0)
        {
            bufferSize = DefaultCopyBufferSize;
        }

        byte[] rented = ArrayPool<byte>.Shared.Rent(bufferSize);

        try
        {
            int read;
            while ((read = this.Read(rented, 0, rented.Length)) != 0)
            {
                destination.Write(rented, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <inheritdoc/>
    public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (bufferSize <= 0)
        {
            bufferSize = DefaultCopyBufferSize;
        }

        byte[] rented = ArrayPool<byte>.Shared.Rent(bufferSize);

        try
        {
            int read;
            while ((read = await this.ReadAsync(rented, 0, rented.Length, cancellationToken).ConfigureAwait(false)) != 0)
            {
                await destination.WriteAsync(rented.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void Flush() => throw new NotSupportedException();

    /// <inheritdoc/>
    public override Task FlushAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override ValueTask DisposeAsync()
    {
        _ = Interlocked.Exchange(ref this._disposed, 1);
        return base.DisposeAsync();
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        _ = Interlocked.Exchange(ref this._disposed, 1);
        base.Dispose(disposing);
    }

    /// <summary>
    /// Throws when the inner stream is not readable.
    /// </summary>
    /// <param name="paramName">The argument name.</param>
    [DoesNotReturn]
    private static void ThrowInnerNotReadable(string paramName) =>
        throw new ArgumentException("Inner stream must support reading.", paramName);

    /// <summary>
    /// Performs the underlying asynchronous read and updates the cumulative byte counter.
    /// </summary>
    /// <param name="buffer">Destination buffer.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Bytes read.</returns>
    private async ValueTask<int> ReadAsyncCore(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        int read = await this._inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        this._totalBytesRead = checked(this._totalBytesRead + read);
        return read;
    }

    /// <summary>
    /// Throws <see cref="ObjectDisposedException"/> if the wrapper has been disposed.
    /// </summary>
    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref this._disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(LengthLimitedReadStream));
        }
    }

    /// <summary>
    /// Throws when the byte limit is exceeded.
    /// </summary>
    [DoesNotReturn]
    private void ThrowLimitExceeded()
    {
        if (this._logger is not null)
        {
            LogLimitExceeded(this._logger, this._operation, this._maxBytes, this._totalBytesRead, null);
        }

        throw new InvalidOperationException(
            $"Response exceeded the configured safety limit (operation='{this._operation}', maxBytes={this._maxBytes}, totalBytesRead={this._totalBytesRead}).");
    }

#if DEBUG
    /// <summary>
    /// Debug-only re-entrancy guard for detecting concurrent reads.
    /// </summary>
    private void EnterRead()
    {
        if (Interlocked.Exchange(ref this._activeRead, 1) != 0)
        {
            throw new InvalidOperationException("Concurrent reads detected on LengthLimitedReadStream.");
        }
    }

    /// <summary>
    /// Debug-only re-entrancy guard exit.
    /// </summary>
    private void ExitRead()
    {
        _ = Interlocked.Exchange(ref this._activeRead, 0);
    }
#endif
}
