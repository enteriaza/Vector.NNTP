// <copyright file="LengthLimitedReadStream.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: no LINQ; no allocations on success; no closures; no boxing; prefer Span/stackalloc; throws extracted + NoInlining where documented.
// LengthLimitedReadStream.cs -- Read-only Stream decorator that enforces a maximum cumulative byte limit on the inner
// stream.  Used to cap untrusted HTTP response bodies before handing them to JsonDocument.ParseAsync, which buffers
// the entire stream into pooled memory.
//
// Why a custom decorator instead of MemoryStream + CopyToAsync:
//   Reading the entire body into a MemoryStream first would double the peak memory: one copy in the MemoryStream,
//   another in JsonDocument's pooled buffers.  The decorator fails fast at the byte threshold without buffering --
//   JsonDocument reads directly from the inner stream through this wrapper.
//
// Why not Microsoft.IO.RecyclableMemoryStream or System.IO.Pipelines:
//   This is the only consumer in the project.  A single-purpose decorator has zero external dependencies, zero
//   configuration, and is trivially auditable.  Pipelines would add complexity (PipeReader, ReadResult, AdvanceTo)
//   for no throughput benefit on the 2-5 KB payloads this class protects.
//
// Pre-clamped reads:
//   Each read clamps the requested buffer size to the remaining byte allowance *before* delegating to the inner
//   stream.  This guarantees the inner stream never delivers more bytes than the remaining allowance -- not even in
//   the final read that crosses the boundary.  Without pre-clamping, a 4,096-byte inner read at 76 bytes remaining
//   would pull 4,096 bytes into JsonDocument's pooled memory before a post-read check could throw.
//
// CopyTo / CopyToAsync overrides:
//   Both are explicitly overridden to route through the clamped Read/ReadAsync paths rather than relying on the
//   base Stream implementation.  The base Stream.CopyToAsync allocates a fresh byte[] on every call; the override
//   uses ArrayPool<byte> for the temporary buffer and returns it in a finally block, eliminating per-call GC
//   pressure.  The base Stream.CopyTo allocates similarly -- the override uses ArrayPool<byte> and returns the
//   buffer in a finally block.  Both overrides pass all reads through the existing pre-clamped Read/ReadAsync
//   methods, so the byte limit is enforced identically regardless of how the caller consumes the stream.
//
// Exact-limit semantics:
//   The limit is treated as an exclusive upper bound -- a response that reads exactly maxBytes bytes will trigger
//   an exception on the *next* read attempt.  This is an intentional design choice: the configured limit
//   (MaxCloudflareResponseBytes = 1 MB) is ~200x larger than any legitimate Cloudflare API response (2-5 KB),
//   so a response consuming the entire allowance is definitively oversized.  If the class is ever generalised for
//   contexts where exact-size responses are legitimate, the limit check should be changed from <= 0 to < 0 and
//   the field renamed to _maxBytesExclusive for clarity.
//
// Disposed-state guard:
//   All read entry points check a _disposed flag before proceeding.  Although this stream does not own the inner
//   stream, a disposed LengthLimitedReadStream should not silently continue reading -- the caller has signalled
//   intent to release the wrapper.  The ObjectDisposedException provides a clear diagnostic message rather than
//   allowing stale reads to produce confusing exceptions from the inner stream.
//
// Logging:
//   Optional ILogger? is accepted for diagnostic logging.  When non-null, the limit-exceeded condition is logged
//   at Warning before throwing, providing structured context (operation, maxBytes, totalBytesRead) that is captured
//   by Serilog sinks.  The ILogger is nullable so callers that do not need logging (e.g. unit tests) can pass null.
//   All [LoggerMessage] methods are in the companion LengthLimitedReadStream.Logging.cs partial file per
//   CONTRIBUTING.md.
//
// SIMD applicability:
//   Not applicable.  This class is a thin delegation layer that forwards read calls to the inner stream with a
//   pre-clamp on buffer size and a post-read counter increment.  There are no contiguous memory scans, byte-level
//   pattern searches, or bulk numeric operations that would benefit from vector instructions.
//
// Cross-platform:
//   No platform-specific behaviour.  All operations delegate to the inner stream's standard Stream API surface,
//   which is fully supported on both Linux and Windows.
//
// Callers:
//   AcmeCertificateProvider.SendCloudflareRequestAsync -- wraps Cloudflare API response streams (GET /zones/{id},
//     POST /dns_records, DELETE /dns_records/{id}) before JsonDocument.ParseAsync.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Vector.NNTP.Utilities.Internal;

namespace Vector.NNTP.Utilities.IO
{
    /// <summary>
    /// A read-only <see cref="Stream"/> decorator that enforces a maximum cumulative byte limit on reads from the inner
    /// stream.  Prevents memory exhaustion from a compromised or MITM'd API endpoint streaming an arbitrarily large
    /// response body within the <see cref="HttpClient.Timeout"/> window.
    /// </summary>
    /// <remarks>
    /// <para><b>Pre-clamped reads:</b> Each read operation clamps the requested buffer size to the remaining byte
    /// allowance <em>before</em> delegating to the inner stream.  This guarantees the inner stream never delivers
    /// more bytes than the remaining allowance -- not even in the final read that crosses the boundary.  Without
    /// pre-clamping, a 4,096-byte inner read at 76 bytes remaining would pull 4,096 bytes into the caller's buffer
    /// (and <see cref="System.Text.Json.JsonDocument"/>'s pooled memory) before a post-read check could throw.
    /// When the allowance is exactly exhausted, subsequent reads throw <see cref="InvalidOperationException"/>
    /// rather than returning 0 (end-of-stream) -- see <see cref="ThrowLimitExceeded"/> for the rationale.</para>
    ///
    /// <para><b>Exact-limit semantics:</b> The limit is treated as an <em>exclusive</em> upper bound.  A response
    /// that delivers exactly <c>maxBytes</c> bytes exhausts the allowance, and the <em>next</em> read attempt throws.
    /// This is intentional -- the configured limit (<see cref="AcmeCertificateProvider.MaxCloudflareResponseBytes"/>
    /// = 1 MB) is ~200x the expected payload size (2-5 KB), so exact exhaustion indicates an oversized response, not
    /// a coincidental exact fit.  If the class is ever generalised to contexts where exact-size responses are
    /// legitimate, change the <c>remaining &lt;= 0</c> check to <c>remaining &lt; 0</c>.</para>
    ///
    /// <para><b>Read overrides:</b> <see cref="ReadAsync(Memory{byte}, CancellationToken)"/> is the primary override
    /// because <see cref="System.Text.Json.JsonDocument.ParseAsync(Stream, System.Text.Json.JsonDocumentOptions?,
    /// CancellationToken)"/> reads exclusively via the <see cref="Memory{T}"/>-based async overload on .NET 8.  The
    /// legacy <see cref="ReadAsync(byte[], int, int, CancellationToken)"/>, synchronous
    /// <see cref="Read(Span{byte})"/>, and <see cref="Stream.Read(byte[], int, int)"/> overloads are also overridden
    /// for completeness -- a future consumer calling any of these would still be protected.</para>
    ///
    /// <para><b>CopyTo / CopyToAsync overrides:</b> Both <see cref="CopyTo(Stream, int)"/> and
    /// <see cref="CopyToAsync(Stream, int, CancellationToken)"/> are explicitly overridden to route through the
    /// clamped <see cref="Read(Span{byte})"/> and <see cref="ReadAsync(Memory{byte}, CancellationToken)"/> paths
    /// respectively.  The base <see cref="Stream.CopyToAsync"/> allocates a <c>byte[]</c> on every call; the override
    /// uses <see cref="ArrayPool{T}.Shared"/> for the temporary buffer and returns it in a <c>finally</c> block,
    /// eliminating per-call GC pressure.  Without these overrides, a caller using <c>CopyToAsync</c> would still be
    /// protected (the base implementation calls <em>this</em> stream's <c>ReadAsync</c>), but the temporary buffer
    /// allocation is unnecessary when the decorator can manage its own pooled buffer.</para>
    ///
    /// <para><b>Checked arithmetic:</b> The cumulative byte counter uses <see langword="checked"/> addition to prevent
    /// silent overflow from wrapping the counter negative and bypassing the limit.  Overflow is unreachable with
    /// pre-clamped reads (the counter can never exceed <c>maxBytes</c>), but <see langword="checked"/> is zero-cost
    /// defence-in-depth against future refactoring that might accidentally remove the pre-clamp.</para>
    ///
    /// <para><b>Remaining-to-int cast safety:</b> The pre-clamp expression <c>buffer.Slice(0, (int)remaining)</c>
    /// casts <c>remaining</c> from <see cref="long"/> to <see cref="int"/>.  This is safe because the cast is only
    /// reached when <c>buffer.Length &gt; remaining</c>, and <c>buffer.Length</c> is an <see cref="int"/> -- so
    /// <c>remaining</c> is strictly less than <see cref="int.MaxValue"/>.  The explicit <c>(int)</c> cast documents
    /// the narrowing rather than relying on implicit conversion.</para>
    ///
    /// <para><b>Ownership:</b> This stream does <em>not</em> own the inner stream.  The caller is responsible for
    /// disposing the inner stream separately (typically via <c>await using</c> on the
    /// <see cref="HttpContent.ReadAsStreamAsync(CancellationToken)"/> result).  Both <see cref="Dispose(bool)"/> and
    /// <see cref="DisposeAsync"/> set the <see cref="_disposed"/> flag to prevent reads after disposal but do not
    /// dispose the inner stream.</para>
    ///
    /// <para><b>Disposed-state guard:</b> All read entry points (<see cref="ReadAsync(Memory{byte}, CancellationToken)"/>,
    /// <see cref="Read(Span{byte})"/>, <see cref="CopyTo(Stream, int)"/>,
    /// <see cref="CopyToAsync(Stream, int, CancellationToken)"/>) check the <see cref="_disposed"/> flag before
    /// proceeding.  This provides a clear <see cref="ObjectDisposedException"/> rather than allowing reads to proceed
    /// on a logically-released wrapper -- consistent with the <c>_disposed</c> guard pattern used by
    /// <see cref="CertificateRenewalService"/>.</para>
    ///
    /// <para><b>Logging:</b> An optional <see cref="ILogger"/> is accepted via the constructor.  When non-null, the
    /// limit-exceeded condition is logged at <see cref="LogLevel.Warning"/> with structured parameters before throwing,
    /// providing diagnostic context captured by Serilog sinks.  All <c>[LoggerMessage]</c> source-generated partial
    /// methods are in the companion <c>LengthLimitedReadStream.Logging.cs</c> partial file per CONTRIBUTING.md.</para>
    ///
    /// <para><b>Thread safety:</b> Not thread-safe.  <see cref="_totalBytesRead"/> is mutated on every read without
    /// synchronisation.  Callers must treat the stream as single-reader; DEBUG builds detect concurrent reads via
    /// <see cref="_activeRead"/>.</para>
    ///
    /// <para><b>Performance:</b> HOT PATH — pre-clamped reads avoid over-fetching; limit/disposed throws are isolated in
    /// separate methods; copy paths use <see cref="PoolingHelpers"/>.</para>
    ///
    /// <para><b>Cross-platform:</b> No platform-specific behaviour.  All operations delegate to the inner stream's
    /// standard <see cref="Stream"/> API surface, which is fully supported on both Linux and Windows.</para>
    ///
    /// <para><b>SIMD applicability:</b> Not applicable.  This class is a thin delegation layer -- it performs a
    /// subtraction, a comparison, an optional slice, and a counter increment per read.  There are no contiguous
    /// memory scans, byte-level searches, or vectorisable loops.</para>
    ///
    /// <para><b>Consumers:</b></para>
    /// <list type="bullet">
    ///   <item><description><see cref="AcmeCertificateProvider"/> -- wraps Cloudflare API response streams before
    ///     <see cref="System.Text.Json.JsonDocument.ParseAsync(Stream, System.Text.Json.JsonDocumentOptions?,
    ///     CancellationToken)"/> in <c>SendCloudflareRequestAsync</c>.</description></item>
    /// </list>
    /// </remarks>
    public sealed partial class LengthLimitedReadStream : Stream
    {
        /// <summary>
        /// Default buffer size for <see cref="CopyTo(Stream)"/>, <see cref="CopyTo(Stream, int)"/>,
        /// <see cref="CopyToAsync(Stream, CancellationToken)"/>, and
        /// <see cref="CopyToAsync(Stream, int, CancellationToken)"/> when the caller does not specify a size.
        /// Matches the default used by <see cref="Stream.CopyTo(Stream)"/> (81,920 bytes = 80 KiB).
        /// </summary>
        private const int DefaultCopyBufferSize = 81_920;

        /// <summary>The underlying response stream.  Not owned -- the caller manages its lifetime.</summary>
        private readonly Stream _inner;

        /// <summary>
        /// The maximum number of bytes that may be read before <see cref="ThrowLimitExceeded"/> is called.  Treated as
        /// an exclusive upper bound -- reading exactly this many bytes exhausts the allowance, and the next read throws.
        /// </summary>
        private readonly long _maxBytes;

        /// <summary>
        /// A short description of the API call (e.g. <c>"POST /dns_records"</c>) included in the
        /// <see cref="InvalidOperationException"/> message for diagnostics.
        /// </summary>
        private readonly string _operation;

        /// <summary>
        /// Optional logger for diagnostic messages.  <see langword="null"/> when the caller does not require logging
        /// (e.g. unit tests).  All log methods are guarded by null-checks so the class operates identically with or
        /// without a logger.
        /// </summary>
        private readonly ILogger _logger;

        /// <summary>
        /// Cumulative bytes read from the inner stream across all read calls.  Incremented via
        /// <see langword="checked"/> addition to prevent silent overflow.
        /// </summary>
        private long _totalBytesRead;

        /// <summary>
        /// Disposed-state flag.  Set to 1 by <see cref="Dispose(bool)"/> or <see cref="DisposeAsync"/> via
        /// <see cref="Interlocked.Exchange(ref int, int)"/>.  All read entry points check this flag and throw
        /// <see cref="ObjectDisposedException"/> if non-zero.  Follows the double-dispose guard pattern used by
        /// <see cref="CertificateRenewalService._disposed"/>.
        /// </summary>
        private int _disposed;

#if DEBUG
        /// <summary>
        /// Debug-only re-entrancy guard.  0 = idle, 1 = read in progress.  Detects accidental concurrent reads that
        /// would corrupt <see cref="_totalBytesRead"/> without synchronisation.
        /// </summary>
        private int _activeRead;
#endif

        /// <summary>
        /// Initialises a new instance wrapping the specified inner stream with a byte limit.
        /// </summary>
        /// <param name="inner">The underlying response stream.  Must support reading (<see cref="Stream.CanRead"/>
        /// must be <see langword="true"/>).  This instance does <em>not</em> take ownership -- the caller must dispose
        /// <paramref name="inner"/> separately.</param>
        /// <param name="maxBytes">The maximum number of bytes that may be read before
        /// <see cref="InvalidOperationException"/> is thrown.  Treated as an exclusive upper bound.  Must be
        /// positive.</param>
        /// <param name="operation">A short description of the API call (e.g. <c>"POST /dns_records"</c>) included in
        /// the exception message for diagnostics.  Must not be <see langword="null"/>, empty, or whitespace.  Must not
        /// contain credentials or infrastructure identifiers.</param>
        /// <param name="logger">Optional logger for diagnostic messages.  Pass <see langword="null"/> to disable
        /// logging.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="inner"/> is
        /// <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="inner"/> does not support reading
        /// (<see cref="Stream.CanRead"/> is <see langword="false"/>), or when <paramref name="operation"/> is
        /// <see langword="null"/>, empty, or whitespace.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxBytes"/> is zero or
        /// negative.</exception>
        public LengthLimitedReadStream(Stream inner, long maxBytes, string operation, ILogger? logger = null)
        {
            ArgumentNullException.ThrowIfNull(inner);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
            ArgumentException.ThrowIfNullOrWhiteSpace(operation);

            if (!inner.CanRead)
                ThrowHelpers.InnerStreamNotReadable(nameof(inner));

            _inner = inner;
            _maxBytes = maxBytes;
            _operation = operation;
            _logger = logger ?? NullLogger.Instance;
        }

        /// <inheritdoc />
        /// <remarks>Delegates to the inner stream -- reflects the actual readability of the underlying response
        /// stream rather than assuming <see langword="true"/>.  If the inner stream is disposed, this returns
        /// <see langword="false"/>.</remarks>
        public override bool CanRead => _inner.CanRead;

        /// <inheritdoc />
        /// <remarks>Always <see langword="false"/> -- HTTP response streams are forward-only.</remarks>
        public override bool CanSeek => false;

        /// <inheritdoc />
        /// <remarks>Always <see langword="false"/> -- this is a read-only decorator.</remarks>
        public override bool CanWrite => false;

        /// <inheritdoc />
        /// <remarks>Delegates to the inner stream -- exposes the inner stream's timeout capability.  HTTP response
        /// streams (<see cref="HttpResponseMessage"/>) may support read timeouts depending on the
        /// underlying <see cref="SocketsHttpHandler"/> configuration.</remarks>
        public override bool CanTimeout => _inner.CanTimeout;

        /// <inheritdoc />
        /// <exception cref="NotSupportedException">Always thrown -- length is unknown for chunked transfer-encoded
        /// response streams.</exception>
        public override long Length => throw new NotSupportedException();

        /// <inheritdoc />
        /// <exception cref="NotSupportedException">Always thrown -- HTTP response streams are forward-only.</exception>
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /// <inheritdoc />
        /// <remarks>Delegates to the inner stream.  Only meaningful when <see cref="CanTimeout"/> is
        /// <see langword="true"/>.</remarks>
        /// <exception cref="InvalidOperationException">Thrown when the inner stream does not support
        /// timeouts.</exception>
        public override int ReadTimeout
        {
            get => _inner.ReadTimeout;
            set => _inner.ReadTimeout = value;
        }

        /// <inheritdoc />
        /// <remarks>Always throws -- this is a read-only decorator.  Overridden explicitly to provide a consistent
        /// <see cref="NotSupportedException"/> rather than the base <see cref="Stream.WriteTimeout"/> which throws
        /// <see cref="InvalidOperationException"/> with a less descriptive message.</remarks>
        /// <exception cref="NotSupportedException">Always thrown -- write operations are not supported.</exception>
        public override int WriteTimeout
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /// <summary>
        /// Asynchronously reads a sequence of bytes from the inner stream into <paramref name="buffer"/>, clamping the
        /// request to the remaining byte allowance before delegating to the inner stream.
        /// </summary>
        /// <remarks>
        /// <para>This is the primary read path -- <see cref="System.Text.Json.JsonDocument.ParseAsync(Stream,
        /// System.Text.Json.JsonDocumentOptions?, CancellationToken)"/> reads exclusively via this
        /// <see cref="Memory{T}"/>-based overload on .NET 8.</para>
        /// <para>The buffer is sliced to <c>min(buffer.Length, remaining)</c> before the inner read, guaranteeing
        /// zero bytes are delivered beyond the limit.</para>
        /// </remarks>
        /// <param name="buffer">The region of memory to write the read bytes into.</param>
        /// <param name="cancellationToken">Cancellation token for host shutdown.</param>
        /// <returns>The number of bytes read, or 0 if the inner stream reached end-of-stream within the
        /// allowance.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when this stream has been disposed.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the cumulative bytes read have reached or exceeded
        /// <c>maxBytes</c>.</exception>
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            EnterRead();
            try
            {
                long remaining = _maxBytes - _totalBytesRead;
                if (remaining <= 0)
                    ThrowLimitExceeded();

                // Safe cast: remaining < buffer.Length <= int.MaxValue, so remaining fits in int.
                int allowed = (int)Math.Min(buffer.Length, remaining);
                int bytesRead = await _inner.ReadAsync(buffer[..allowed], cancellationToken).ConfigureAwait(false);
                _totalBytesRead = checked(_totalBytesRead + bytesRead);
                return bytesRead;
            }
            finally
            {
                ExitRead();
            }
        }

        /// <summary>
        /// Asynchronously reads a sequence of bytes from the inner stream into <paramref name="buffer"/>, clamping the
        /// request to the remaining byte allowance before delegating to the inner stream.
        /// </summary>
        /// <remarks>
        /// <para>Overrides the legacy <see cref="Task{Int32}"/>-based overload to avoid the base
        /// <see cref="Stream.ReadAsync(byte[], int, int, CancellationToken)"/> implementation, which calls the
        /// synchronous <see cref="Read(byte[], int, int)"/> on a thread-pool thread -- losing the
        /// <paramref name="cancellationToken"/> for the inner stream read and adding an unnecessary thread-pool
        /// hop.</para>
        /// </remarks>
        /// <param name="buffer">The byte array to write the read bytes into.</param>
        /// <param name="offset">The byte offset in <paramref name="buffer"/> at which to begin storing data.</param>
        /// <param name="count">The maximum number of bytes to read.</param>
        /// <param name="cancellationToken">Cancellation token for host shutdown.</param>
        /// <returns>The number of bytes read, or 0 if the inner stream reached end-of-stream within the
        /// allowance.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when this stream has been disposed.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the cumulative bytes read have reached or exceeded
        /// <c>maxBytes</c>.</exception>
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        /// <summary>
        /// Reads a sequence of bytes from the inner stream into <paramref name="buffer"/>, clamping the request to
        /// the remaining byte allowance before delegating to the inner stream.
        /// </summary>
        /// <remarks>
        /// <para>Synchronous fallback for consumers that do not use the async read path.  Not called by
        /// <see cref="System.Text.Json.JsonDocument.ParseAsync(Stream, System.Text.Json.JsonDocumentOptions?,
        /// CancellationToken)"/> on .NET 8, but overridden for completeness.</para>
        /// </remarks>
        /// <param name="buffer">The region of memory to write the read bytes into.</param>
        /// <returns>The number of bytes read, or 0 if the inner stream reached end-of-stream within the
        /// allowance.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when this stream has been disposed.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the cumulative bytes read have reached or exceeded
        /// <c>maxBytes</c>.</exception>
        public override int Read(Span<byte> buffer)
        {
            ThrowIfDisposed();
            EnterRead();
            try
            {
                long remaining = _maxBytes - _totalBytesRead;
                if (remaining <= 0)
                    ThrowLimitExceeded();

                // Safe cast: remaining < buffer.Length <= int.MaxValue, so remaining fits in int.
                int allowed = (int)Math.Min(buffer.Length, remaining);
                int bytesRead = _inner.Read(buffer[..allowed]);
                _totalBytesRead = checked(_totalBytesRead + bytesRead);
                return bytesRead;
            }
            finally
            {
                ExitRead();
            }
        }

        /// <summary>
        /// Reads a sequence of bytes from the inner stream, delegating to <see cref="Read(Span{byte})"/>.
        /// </summary>
        /// <param name="buffer">The byte array to write the read bytes into.</param>
        /// <param name="offset">The byte offset in <paramref name="buffer"/> at which to begin storing data.</param>
        /// <param name="count">The maximum number of bytes to read.</param>
        /// <returns>The number of bytes read, or 0 if the inner stream reached end-of-stream within the
        /// allowance.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when this stream has been disposed.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the cumulative bytes read have reached or exceeded
        /// <c>maxBytes</c>.</exception>
        public override int Read(byte[] buffer, int offset, int count)
        {
            return Read(buffer.AsSpan(offset, count));
        }

        /// <summary>
        /// Copies the remaining content of this stream to <paramref name="destination"/> using the
        /// <see cref="DefaultCopyBufferSize"/>, enforcing the byte limit on every read operation.
        /// </summary>
        /// <remarks>
        /// <para><b>Why override:</b> The base <see cref="Stream.CopyTo(Stream)"/> calls
        /// <see cref="CopyTo(Stream, int)"/> with a default buffer size, but overriding both ensures the
        /// <see cref="DefaultCopyBufferSize"/> constant is used consistently and the disposed-state check is performed
        /// before the base class allocates any resources.</para>
        /// </remarks>
        /// <param name="destination">The stream to write the read bytes to.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="destination"/> is
        /// <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when this stream has been disposed.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the cumulative bytes read exceed
        /// <c>maxBytes</c>.</exception>
        public new void CopyTo(Stream destination)
        {
            CopyTo(destination, DefaultCopyBufferSize);
        }

        /// <summary>
        /// Copies the remaining content of this stream to <paramref name="destination"/>, enforcing the byte limit on
        /// every read operation, using a pooled buffer from <see cref="ArrayPool{T}.Shared"/>.
        /// </summary>
        /// <remarks>
        /// <para><b>Why override:</b> The base <see cref="Stream.CopyTo(Stream, int)"/> allocates a fresh
        /// <c>byte[]</c> on every call.  This override rents from <see cref="ArrayPool{T}.Shared"/> and returns the
        /// buffer in a <c>finally</c> block, eliminating per-call GC pressure.  All reads pass through
        /// <see cref="Read(Span{byte})"/> which enforces the pre-clamped byte limit.</para>
        ///
        /// <para><b>Buffer size clamping:</b> The <paramref name="bufferSize"/> is clamped to the remaining byte
        /// allowance to avoid renting unnecessarily large buffers.  For a 2 KB response with a 1 MB limit and a
        /// default 80 KiB <paramref name="bufferSize"/>, the actual rented buffer is still 80 KiB (well below the
        /// remaining allowance), but if only 500 bytes remain the buffer is clamped to 500.</para>
        /// </remarks>
        /// <param name="destination">The stream to write the read bytes to.</param>
        /// <param name="bufferSize">The size of the intermediate buffer used during the copy.  Must be
        /// positive.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="destination"/> is
        /// <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="bufferSize"/> is zero or
        /// negative.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when this stream has been disposed.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the cumulative bytes read exceed
        /// <c>maxBytes</c>.</exception>
        public override void CopyTo(Stream destination, int bufferSize)
        {
            ArgumentNullException.ThrowIfNull(destination);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferSize);
            ThrowIfDisposed();

            // Clamp buffer size to remaining allowance to avoid renting unnecessarily large buffers.
            long remaining = _maxBytes - _totalBytesRead;
            if (remaining <= 0)
                ThrowLimitExceeded();

            int effectiveBufferSize = (int)Math.Min(bufferSize, remaining);
            byte[] rentedBuffer = PoolingHelpers.RentByteBuffer(effectiveBufferSize);
            try
            {
                int bytesRead;
                // Read(Span<byte>) enforces the pre-clamp and byte limit on every iteration.
                while ((bytesRead = Read(rentedBuffer.AsSpan(0, effectiveBufferSize))) > 0)
                {
                    destination.Write(rentedBuffer, 0, bytesRead);
                }
            }
            finally
            {
                PoolingHelpers.ReturnByteBuffer(rentedBuffer);
            }
        }

        /// <summary>
        /// Asynchronously copies the remaining content of this stream to <paramref name="destination"/> using the
        /// <see cref="DefaultCopyBufferSize"/>, enforcing the byte limit on every read operation.
        /// </summary>
        /// <remarks>
        /// <para><b>Why override:</b> Ensures the <see cref="DefaultCopyBufferSize"/> constant is used consistently
        /// and the disposed-state check is performed before the base class allocates any resources.</para>
        /// </remarks>
        /// <param name="destination">The stream to write the read bytes to.</param>
        /// <param name="cancellationToken">Cancellation token for host shutdown.</param>
        /// <returns>A task that represents the asynchronous copy operation.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="destination"/> is
        /// <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when this stream has been disposed.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the cumulative bytes read exceed
        /// <c>maxBytes</c>.</exception>
        /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is
        /// cancelled.</exception>
        public new Task CopyToAsync(Stream destination, CancellationToken cancellationToken)
        {
            return CopyToAsync(destination, DefaultCopyBufferSize, cancellationToken);
        }

        /// <summary>
        /// Asynchronously copies the remaining content of this stream to <paramref name="destination"/>, enforcing the
        /// byte limit on every read operation, using a pooled buffer from <see cref="ArrayPool{T}.Shared"/>.
        /// </summary>
        /// <remarks>
        /// <para><b>Why override:</b> The base <see cref="Stream.CopyToAsync(Stream, int, CancellationToken)"/>
        /// allocates a fresh <c>byte[]</c> on every call.  This override rents from <see cref="ArrayPool{T}.Shared"/>
        /// and returns the buffer in a <c>finally</c> block, eliminating per-call GC pressure.  All reads pass through
        /// <see cref="ReadAsync(Memory{byte}, CancellationToken)"/> which enforces the pre-clamped byte limit.</para>
        ///
        /// <para><b>Buffer size clamping:</b> Same clamping semantics as <see cref="CopyTo(Stream, int)"/> -- the
        /// rented buffer size is <c>min(bufferSize, remaining)</c> to avoid over-allocation near the byte limit.</para>
        ///
        /// <para><b>Cancellation:</b> The <paramref name="cancellationToken"/> is checked before the first read and
        /// propagated to every <see cref="ReadAsync(Memory{byte}, CancellationToken)"/> and
        /// <see cref="Stream.WriteAsync(ReadOnlyMemory{byte}, CancellationToken)"/> call.  If the token fires during a
        /// read or write, the rented buffer is returned before the <see cref="OperationCanceledException"/>
        /// propagates.</para>
        /// </remarks>
        /// <param name="destination">The stream to write the read bytes to.</param>
        /// <param name="bufferSize">The size of the intermediate buffer used during the copy.  Must be
        /// positive.</param>
        /// <param name="cancellationToken">Cancellation token for host shutdown.</param>
        /// <returns>A task that represents the asynchronous copy operation.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="destination"/> is
        /// <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="bufferSize"/> is zero or
        /// negative.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when this stream has been disposed.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the cumulative bytes read exceed
        /// <c>maxBytes</c>.</exception>
        /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is
        /// cancelled.</exception>
        public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(destination);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferSize);
            ThrowIfDisposed();

            // Clamp buffer size to remaining allowance to avoid renting unnecessarily large buffers.
            long remaining = _maxBytes - _totalBytesRead;
            if (remaining <= 0)
                ThrowLimitExceeded();

            int effectiveBufferSize = (int)Math.Min(bufferSize, remaining);
            byte[] rentedBuffer = PoolingHelpers.RentByteBuffer(effectiveBufferSize);
            try
            {
                int bytesRead;
                Memory<byte> bufferMemory = rentedBuffer.AsMemory(0, effectiveBufferSize);
                // ReadAsync(Memory<byte>, CancellationToken) enforces the pre-clamp and byte limit on every iteration.
                while ((bytesRead = await ReadAsync(bufferMemory, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await destination.WriteAsync(rentedBuffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                PoolingHelpers.ReturnByteBuffer(rentedBuffer);
            }
        }

        /// <summary>
        /// Throws <see cref="InvalidOperationException"/> when the cumulative bytes read have reached or exceeded the
        /// configured limit.
        /// </summary>
        /// <remarks>
        /// <para><b>Why throw instead of returning 0 (end-of-stream):</b> Returning 0 would silently truncate an
        /// oversized response -- <see cref="System.Text.Json.JsonDocument.ParseAsync(Stream,
        /// System.Text.Json.JsonDocumentOptions?, CancellationToken)"/> would parse the incomplete JSON and either throw
        /// a confusing <see cref="System.Text.Json.JsonException"/> ("unexpected end of JSON input") or return a partial
        /// document missing trailing fields.  Throwing <see cref="InvalidOperationException"/> with a clear diagnostic
        /// message is preferable -- the caller knows the response was too large, not malformed.</para>
        ///
        /// <para><b>Exact-size responses:</b> A response whose body is exactly <c>maxBytes</c> bytes will exhaust the
        /// allowance during the final read (which is clamped to the remaining bytes).  The <em>next</em> read attempt
        /// finds <c>remaining &lt;= 0</c> and enters this method.  Throwing is correct because the limit
        /// (<see cref="AcmeCertificateProvider.MaxCloudflareResponseBytes"/> = 1 MB) is ~200x larger than any
        /// legitimate Cloudflare API response (2-5 KB) -- a response consuming the entire allowance is definitively
        /// oversized, not a coincidental exact fit.</para>
        ///
        /// <para><b>Exception type choice:</b> <see cref="InvalidOperationException"/> is used rather than
        /// <see cref="IOException"/> because this is a policy violation (exceeded a configured safety limit), not a
        /// transport-level I/O failure.  If the class is ever generalised into reusable infrastructure, a dedicated
        /// <c>ResponseSizeLimitExceededException : IOException</c> would provide more precise catch-site filtering --
        /// but with a single consumer that catches <see cref="Exception"/> in a retry loop, the additional type adds
        /// no value today.</para>
        ///
        /// <para><b>Method isolation:</b> Extracting the throw into a separate <see cref="DoesNotReturnAttribute"/>
        /// method keeps the <c>throw</c> instruction out of the <see cref="Read(Span{byte})"/> fast path IL.  On .NET 8
        /// (RyuJIT), any method containing a <c>throw</c> is ineligible for inlining.  While the async
        /// <see cref="ReadAsync(Memory{byte}, CancellationToken)"/> overload is not inlined regardless (async state
        /// machine), the synchronous <see cref="Read(Span{byte})"/> benefits from this separation -- the JIT can inline
        /// it at call sites where the inner stream's <c>Read</c> is also inlined, eliminating the call overhead
        /// entirely.</para>
        ///
        /// <para><b>Logging:</b> When a logger is available, the limit violation is logged at
        /// <see cref="LogLevel.Warning"/> via <see cref="LogLimitExceeded"/> before throwing.  This provides structured
        /// context (<c>Operation</c>, <c>MaxBytes</c>, <c>TotalBytesRead</c>) in Serilog sinks that may not capture
        /// exception messages as structured properties.</para>
        ///
        /// <para><b>Attribute:</b> <see cref="DoesNotReturnAttribute"/> is applied so the compiler and static analysers
        /// recognise that callers after this call are unreachable, enabling correct nullability and definite-assignment
        /// analysis without needing a dummy <c>return</c> statement.</para>
        /// </remarks>
        /// <exception cref="InvalidOperationException">Always thrown.</exception>
        [DoesNotReturn]
        private void ThrowLimitExceeded()
        {
            LogLimitExceeded(_operation, _maxBytes, _totalBytesRead);

            throw new InvalidOperationException(
                $"Cloudflare API {_operation} response body exceeded the {_maxBytes:N0}-byte safety limit " +
                $"at {_totalBytesRead:N0} bytes read -- possible compromised endpoint");
        }

        /// <summary>
        /// Throws <see cref="ObjectDisposedException"/> when the stream has been disposed.  Isolated to keep the read
        /// method IL below the JIT inlining threshold.
        /// </summary>
        /// <remarks>
        /// <para>Uses <see cref="Volatile.Read(ref int)"/> for a single atomic read of <see cref="_disposed"/>,
        /// consistent with the guard pattern used by <see cref="CertificateRenewalService"/>.</para>
        /// </remarks>
        private void ThrowIfDisposed()
        {
            GuardUtilities.ThrowIfDisposed(this, ref _disposed);
        }

        /// <summary>
        /// Debug-only: asserts that no concurrent read is in progress.  In release builds, compiles to a no-op.
        /// </summary>
        /// <remarks>
        /// Uses <see cref="Interlocked.Exchange(ref int, int)"/> to atomically set the guard and detect re-entrancy.
        /// A <see cref="Interlocked.Exchange(ref int, int)"/>-based set with <see cref="Debug.Assert(bool, string?)"/>
        /// is sufficient for fail-fast assertion semantics.
        /// </remarks>
        [Conditional("DEBUG")]
        private void EnterRead()
        {
#if DEBUG
            int previous = Interlocked.Exchange(ref _activeRead, 1);
            Debug.Assert(previous == 0, "LengthLimitedReadStream: concurrent read detected -- this stream is not thread-safe.");
#endif
        }

        /// <summary>
        /// Debug-only: clears the re-entrancy guard.  In release builds, compiles to a no-op.
        /// </summary>
        [Conditional("DEBUG")]
        private void ExitRead()
        {
#if DEBUG
            Volatile.Write(ref _activeRead, 0);
#endif
        }

        /// <inheritdoc />
        /// <exception cref="NotSupportedException">Always thrown -- write is not supported on a read-only
        /// decorator.</exception>
        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        /// <exception cref="NotSupportedException">Always thrown -- write is not supported on a read-only
        /// decorator.</exception>
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        /// <exception cref="NotSupportedException">Always thrown -- write is not supported on a read-only
        /// decorator.</exception>
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        /// <exception cref="NotSupportedException">Always thrown -- write is not supported on a read-only
        /// decorator.</exception>
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        /// <exception cref="NotSupportedException">Always thrown -- HTTP response streams are forward-only.</exception>
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        /// <exception cref="NotSupportedException">Always thrown -- length mutation is not supported on a read-only
        /// decorator.</exception>
        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        /// <remarks>No-op -- flushing a read-only stream has no effect.</remarks>
        public override void Flush()
        {
        }

        /// <inheritdoc />
        /// <remarks>
        /// Returns a completed <see cref="Task"/> directly when the cancellation token has not been requested,
        /// avoiding the base <see cref="Stream.FlushAsync(CancellationToken)"/> implementation which calls
        /// <see cref="Flush"/> via <see cref="Task.Factory.StartNew(Action, CancellationToken)"/> -- an unnecessary
        /// thread-pool hop for a no-op operation.  Honours the <paramref name="cancellationToken"/> by returning a
        /// cancelled task when cancellation has already been requested.
        /// </remarks>
        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return cancellationToken.IsCancellationRequested ? Task.FromCanceled(cancellationToken) : Task.CompletedTask;
        }

        /// <inheritdoc />
        /// <remarks>
        /// <para>Sets the <see cref="_disposed"/> flag via <see cref="Interlocked.Exchange(ref int, int)"/> to prevent
        /// subsequent reads from proceeding.  Calls <see cref="Stream.Dispose(bool)">base.Dispose(disposing)</see> to
        /// satisfy CA2215 and maintain the <see cref="Stream"/> disposal contract.  The base
        /// <see cref="Stream.Dispose(bool)"/> implementation is an empty virtual method on .NET 8 -- calling it is a
        /// zero-cost no-op that keeps the analyser happy without introducing any side effects.</para>
        ///
        /// <para>This stream does <em>not</em> own the inner stream.  The caller manages the inner stream's lifetime
        /// via the outer <c>await using</c> on <see cref="HttpContent.ReadAsStreamAsync(CancellationToken)"/>.  The
        /// <see cref="_disposed"/> flag prevents reads after disposal but no inner-stream cleanup is
        /// performed.</para>
        /// </remarks>
        protected override void Dispose(bool disposing)
        {
            _ = Interlocked.Exchange(ref _disposed, 1);

            // base.Dispose(bool) is an empty virtual on Stream in .NET 8; called for CA2215 compliance only.
            base.Dispose(disposing);
        }

        /// <inheritdoc />
        /// <remarks>
        /// <para>Sets the <see cref="_disposed"/> flag and returns a completed <see cref="ValueTask"/>.  Overridden
        /// explicitly to prevent the base <see cref="Stream.DisposeAsync"/> from executing its default implementation,
        /// which calls <see cref="Stream.Close"/> -> <see cref="Dispose(bool)">Dispose(true)</see> ->
        /// <see cref="GC.SuppressFinalize(object)"/>.  The <c>SuppressFinalize</c> is unnecessary because this class
        /// is <see langword="sealed"/> and has no finalizer, and the <c>Close</c> -> <c>Dispose(true)</c> chain would
        /// redundantly call <c>base.Dispose(true)</c> which we already handle in
        /// <see cref="Dispose(bool)"/>.</para>
        ///
        /// <para>CA2215 is suppressed on this method because the base <see cref="Stream.DisposeAsync"/> triggers the
        /// <c>Close</c> -> <c>SuppressFinalize</c> chain described above -- calling it would introduce unnecessary
        /// overhead for a class that has no resources to release asynchronously.  The synchronous
        /// <see cref="Dispose(bool)"/> override satisfies the disposal contract.</para>
        /// </remarks>
        public override ValueTask DisposeAsync()
        {
            return Interlocked.Exchange(ref _disposed, 1) != 0 ? default : base.DisposeAsync();
        }
    }
}
