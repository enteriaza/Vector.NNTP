// DisposalUtilities.cs -- Best-effort disposal helpers for resources that may be in a terminal or
// already-disposed state.
//
// Provides TryDispose (synchronous) and TryDisposeAsync (asynchronous) wrappers that swallow exceptions thrown
// during disposal, returning them to the caller for optional diagnostic logging.  This eliminates the repetitive
// per-resource try/catch blocks found in Dispose and DisposeAsync implementations across the project (e.g.,
// NntpSession.Dispose, CpuLoadMonitor.Dispose, NntpDatabase.DisposeAsync, SqlGroupRepository.Dispose).
//
// All methods are static and thread-safe.  They do not log directly -- the caught exception is returned so the
// caller can choose the appropriate log level, message template, and structured properties.
//
// Synchronous path (TryDispose, TryDisposeAll): zero heap allocations on the success path.
// Asynchronous path (TryDisposeAsync, TryDisposeAllAsync): zero async state machine allocation when the
// underlying DisposeAsync completes synchronously (the common case).  The async state machine is only
// materialised when the ValueTask returned by DisposeAsync is genuinely incomplete.
//
// Logging:
//   Not applicable.  This class is a static utility with no ILogger dependency.  Caught exceptions are
//   returned to the caller for domain-appropriate logging via [LoggerMessage] partial methods in the
//   caller's own *.Logging.cs file.  This preserves the caller's ability to choose the log level,
//   message template, and structured properties -- the disposal helper has no knowledge of the resource's
//   role or the appropriate severity.
//
// Thread safety:
//   All methods are static and stateless.  Safe for concurrent use from any number of threads without
//   synchronisation.
//
// Cross-platform:
//   Fully portable.  All methods use BCL interfaces (IDisposable, IAsyncDisposable) and value types
//   (ValueTask, ReadOnlySpan).  No P/Invoke, no OS-specific APIs, no architecture-specific intrinsics.
//   Compatible with Windows (x64) and Linux (x64) on .NET 8.
//
// SIMD applicability:
//   Not applicable.  Disposal is a per-resource operation with no bulk data, no contiguous memory buffers,
//   and no vectorisable computation.
//
// Usage:
//   DisposalUtilities.TryDispose(stream);               // fire-and-forget, swallow exception
//   var ex = DisposalUtilities.TryDispose(connection);   // inspect exception for logging
//   await DisposalUtilities.TryDisposeAsync(reader);     // async equivalent

namespace MessageBus.Utilities
{
    /// <summary>
    /// Best-effort disposal helpers for resources that may be in a terminal or already-disposed state.
    /// </summary>
    /// <remarks>
    /// <para><b>Rationale:</b> Shutdown, error-recovery, and cleanup paths frequently need to dispose resources that
    /// may already be broken -- a faulted connection, a disposed socket, or a timed-out stream.  Wrapping each disposal
    /// in its own <c>try</c>/<c>catch</c> is the project-wide convention, but inlining the pattern at every call site
    /// is verbose and error-prone. This class centralises the pattern into a single, tested location.</para>
    ///
    /// <para><b>Exception return vs. logging:</b> Caught exceptions are returned to the caller rather than logged
    /// directly.  This preserves the caller's ability to choose the log level, message template, and structured
    /// properties -- the disposal helper has no knowledge of the resource's role or the appropriate severity.  Callers
    /// that do not need the exception can simply discard the return value.</para>
    ///
    /// <para><b>Thread safety:</b> All methods are <see langword="static"/> and stateless.  Safe for concurrent use
    /// from any number of threads without synchronisation.</para>
    ///
    /// <para><b>Allocation characteristics:</b></para>
    /// <list type="bullet">
    ///   <item><description><see cref="TryDispose"/> -- zero heap allocations on the success path.  On the failure path,
    ///     the exception object is created by the callee's <see cref="IDisposable.Dispose"/> implementation, not by this
    ///     method.</description></item>
    ///   <item><description><see cref="TryDisposeAsync"/> -- returns a completed <see cref="ValueTask{TResult}"/> when
    ///     <see cref="IAsyncDisposable.DisposeAsync"/> completes synchronously, avoiding async state machine allocation.
    ///     The async continuation (<see cref="AwaitDisposeAsync"/>) is only entered when the underlying disposal is
    ///     genuinely asynchronous.</description></item>
    ///   <item><description><see cref="TryDisposeAllAsync"/> -- same fast-path optimisation per element.  The outer method
    ///     is <see langword="async"/> because at least one element may be genuinely asynchronous, but synchronous
    ///     completions within the loop avoid per-element state machine overhead by using the inline
    ///     <see cref="ValueTask.IsCompletedSuccessfully"/> check.</description></item>
    /// </list>
    ///
    /// <para><b>Cross-platform:</b> Fully portable.  All methods use BCL interfaces (<see cref="IDisposable"/>,
    /// <see cref="IAsyncDisposable"/>) and value types (<see cref="ValueTask"/>, <see cref="ReadOnlySpan{T}"/>).
    /// No P/Invoke, no OS-specific APIs, and no architecture-specific intrinsics.  Compatible with Windows (x64) and
    /// Linux (x64) on .NET 8.</para>
    ///
    /// <para><b>SIMD applicability:</b> Not applicable.  Disposal is a per-resource operation with no bulk data, no
    /// contiguous memory buffers, and no vectorisable computation.</para>
    /// </remarks>
    internal static class DisposalUtilities
    {

        #region Synchronous Disposal

        /// <summary>
        /// Disposes an <see cref="IDisposable"/> resource, swallowing any exception.  Returns the caught exception
        /// (if any) for optional caller logging.
        /// </summary>
        /// <param name="disposable">The resource to dispose.  May be <see langword="null"/> (no-op).</param>
        /// <returns>The exception thrown by <see cref="IDisposable.Dispose"/>, or <see langword="null"/> on
        /// success or when <paramref name="disposable"/> is <see langword="null"/>.</returns>
        /// <remarks>
        /// <para><b>Usage:</b> Use in shutdown, error-recovery, and cleanup paths where the resource may already be
        /// in a broken state (e.g., a faulted connection, a disposed socket, or a timed-out stream).</para>
        ///
        /// <para><b>Inlining:</b> The <see cref="System.Runtime.CompilerServices.MethodImplAttribute"/> with
        /// <see cref="System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining"/> is intentionally omitted.
        /// The .NET 8 JIT does not inline methods containing <c>try</c>/<c>catch</c> blocks regardless of the
        /// attribute -- applying it would be misleading.</para>
        ///
        /// <para><b>Example:</b></para>
        /// <code>
        /// var ex = DisposalUtilities.TryDispose(connection);
        /// if (ex is not null) LogDisposeError(ex);
        /// </code>
        /// </remarks>
        public static Exception? TryDispose(IDisposable? disposable)
        {
            if (disposable is null)
                return null;
            try
            {
                disposable.Dispose();
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        /// <summary>
        /// Disposes each <see cref="IDisposable"/> resource in <paramref name="disposables"/> independently,
        /// swallowing exceptions from each.  Returns the first caught exception (if any) for optional caller logging.
        /// </summary>
        /// <param name="disposables">The resources to dispose, in order.  Individual elements may be
        /// <see langword="null"/> (skipped).  The span itself may be empty (no-op).</param>
        /// <returns>The first exception thrown by any <see cref="IDisposable.Dispose"/> call, or
        /// <see langword="null"/> if all disposals succeeded.</returns>
        /// <remarks>
        /// <para><b>Guarantee:</b> Every non-<see langword="null"/> element is disposed regardless of whether a
        /// preceding disposal threw.</para>
        ///
        /// <para><b>First-exception semantics:</b> Only the first exception is returned.  Subsequent exceptions are
        /// silently swallowed.  This matches the project convention where disposal errors are logged at Debug level
        /// -- losing a second disposal error in the same shutdown sequence is acceptable.</para>
        ///
        /// <para><b>Local capture:</b> Each span element is read into a local variable before the <c>try</c> block
        /// to avoid a redundant span indexer re-read inside the exception-handling region.</para>
        ///
        /// <para><b>Example:</b></para>
        /// <code>
        /// var ex = DisposalUtilities.TryDisposeAll([compressStream, transportStream, networkStream]);
        /// if (ex is not null) LogDisposeError(ex);
        /// </code>
        /// </remarks>
        public static Exception? TryDisposeAll(ReadOnlySpan<IDisposable?> disposables)
        {
            Exception? first = null;
            for (int i = 0; i < disposables.Length; i++)
            {
                IDisposable? resource = disposables[i];
                if (resource is null)
                    continue;
                try
                {
                    resource.Dispose();
                }
                catch (Exception ex)
                {
                    first ??= ex;
                }
            }
            return first;
        }

        #endregion

        #region Asynchronous Disposal

        /// <summary>
        /// Asynchronously disposes an <see cref="IAsyncDisposable"/> resource, swallowing any exception.  Returns
        /// the caught exception (if any) for optional caller logging.
        /// </summary>
        /// <param name="disposable">The resource to dispose.  May be <see langword="null"/> (no-op).</param>
        /// <returns>A <see cref="ValueTask{TResult}"/> that completes with the exception thrown by
        /// <see cref="IAsyncDisposable.DisposeAsync"/>, or <see langword="null"/> on success or when
        /// <paramref name="disposable"/> is <see langword="null"/>.</returns>
        /// <remarks>
        /// <para><b>Usage:</b> Use in async shutdown and error-recovery paths, or the catch block of an async
        /// method that needs to release a <see cref="System.Data.Common.DbCommand"/> or
        /// <see cref="System.Data.Common.DbConnection"/>.</para>
        ///
        /// <para><b>Synchronous fast path:</b> Most <see cref="IAsyncDisposable.DisposeAsync"/> implementations
        /// complete synchronously (buffered streams, completed commands, already-disposed resources).  When the
        /// returned <see cref="ValueTask"/> reports <see cref="ValueTask.IsCompletedSuccessfully"/>, this method
        /// returns a completed <see cref="ValueTask{TResult}"/> directly -- no async state machine is allocated.
        /// The async continuation (<see cref="AwaitDisposeAsync"/>) is only invoked when the disposal is genuinely
        /// incomplete.</para>
        ///
        /// <para><b>Faulted fast path:</b> When <see cref="IAsyncDisposable.DisposeAsync"/> returns a synchronously
        /// faulted <see cref="ValueTask"/> (i.e., <see cref="ValueTask.IsCompleted"/> is <see langword="true"/> but
        /// <see cref="ValueTask.IsCompletedSuccessfully"/> is <see langword="false"/>), the exception is extracted
        /// via <see cref="ExtractException"/> without re-throwing.  This path also avoids the async state
        /// machine.</para>
        ///
        /// <para><b>Synchronous throw:</b> If <see cref="IAsyncDisposable.DisposeAsync"/> itself throws
        /// synchronously (before returning a <see cref="ValueTask"/>), the exception is caught and returned
        /// directly.  This covers malformed <see cref="IAsyncDisposable"/> implementations that throw from the
        /// method body rather than returning a faulted <see cref="ValueTask"/>.</para>
        ///
        /// <para><b>Example:</b></para>
        /// <code>
        /// var ex = await DisposalUtilities.TryDisposeAsync(reader);
        /// if (ex is not null) LogDisposeError(ex);
        /// </code>
        /// </remarks>
        public static ValueTask<Exception?> TryDisposeAsync(IAsyncDisposable? disposable)
        {
            if (disposable is null)
                return default;
            ValueTask vt;
            try
            {
                vt = disposable.DisposeAsync();
            }
            catch (Exception ex)
            {
                // DisposeAsync() itself threw synchronously (before returning a ValueTask).
                return new ValueTask<Exception?>(ex);
            }
            // Fast path: synchronous completion (common for buffered streams, completed commands, etc.).
            if (vt.IsCompletedSuccessfully)
                return default;
            // Synchronously faulted: extract exception without awaiting to avoid state machine allocation.
            if (vt.IsCompleted)
                return new ValueTask<Exception?>(ExtractException(vt));
            // Genuinely async: fall back to the async state machine.
            return AwaitDisposeAsync(vt);
        }

        /// <summary>
        /// Asynchronously disposes each <see cref="IAsyncDisposable"/> resource in <paramref name="disposables"/>
        /// independently, swallowing exceptions from each.  Returns the first caught exception (if any) for optional
        /// caller logging.
        /// </summary>
        /// <param name="disposables">The resources to dispose, in order.  Individual elements may be
        /// <see langword="null"/> (skipped).  The array may be empty (no-op).</param>
        /// <returns>A <see cref="ValueTask{TResult}"/> that completes with the first exception thrown by any
        /// <see cref="IAsyncDisposable.DisposeAsync"/> call, or <see langword="null"/> if all disposals
        /// succeeded.</returns>
        /// <remarks>
        /// <para><b>Guarantee:</b> Every non-<see langword="null"/> element is disposed regardless of whether a
        /// preceding disposal threw.  Disposals are awaited sequentially -- not concurrently -- to match the
        /// ordered-disposal semantics expected by connection/command/reader stacks.</para>
        ///
        /// <para><b>Parameter type:</b> Accepts <c>params IAsyncDisposable?[]</c> rather than
        /// <see cref="ReadOnlySpan{T}"/> because <c>async</c> methods cannot use <c>ref struct</c> locals.  The
        /// <c>params</c> array is allocated by the caller (or by the compiler for inline argument lists), but this
        /// is acceptable because async disposal paths are not allocation-sensitive hot paths.</para>
        ///
        /// <para><b>Per-element fast path:</b> Each element's <see cref="IAsyncDisposable.DisposeAsync"/> result is
        /// checked for synchronous completion before awaiting.  When all elements complete synchronously (the common
        /// case), the loop executes without suspending -- the async state machine is allocated by the compiler but
        /// never transitions through an await suspension point, keeping overhead minimal.</para>
        ///
        /// <para><b>Faulted-sync handling:</b> Unlike <see cref="TryDisposeAsync"/>, the per-element loop does not
        /// use <see cref="ExtractException"/> for synchronously-faulted <see cref="ValueTask"/>s.  Instead, the
        /// faulted task falls through to <c>await vt</c>, which re-throws into the existing <c>catch</c> block.
        /// This is an intentional design choice: the <see langword="async"/> state machine is already allocated for
        /// the outer loop, so the throw-and-catch overhead (~1 us) is negligible compared to the I/O cost of
        /// multiple disposals.  The simpler code path reduces maintenance surface without measurable performance
        /// impact.</para>
        ///
        /// <para><b>Local capture:</b> Each array element is read into a local variable before the <c>try</c> block
        /// to avoid a redundant array indexer re-read inside the exception-handling region.</para>
        ///
        /// <para><b>Example:</b></para>
        /// <code>
        /// var ex = await DisposalUtilities.TryDisposeAllAsync(reader, command, connection);
        /// if (ex is not null) LogDisposeError(ex);
        /// </code>
        /// </remarks>
        public static async ValueTask<Exception?> TryDisposeAllAsync(params IAsyncDisposable?[] disposables)
        {
            Exception? first = null;
            for (int i = 0; i < disposables.Length; i++)
            {
                IAsyncDisposable? resource = disposables[i];
                if (resource is null)
                    continue;
                try
                {
                    ValueTask vt = resource.DisposeAsync();
                    // Inline synchronous-completion check avoids suspending the state machine when
                    // DisposeAsync completes immediately (the common case for most IAsyncDisposable
                    // implementations).
                    if (!vt.IsCompletedSuccessfully)
                        await vt.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    first ??= ex;
                }
            }
            return first;
        }

        #endregion

        #region Private Async Helpers

        /// <summary>
        /// Awaits a genuinely-asynchronous <see cref="ValueTask"/> from <see cref="IAsyncDisposable.DisposeAsync"/>
        /// and swallows any exception.  This is the cold path for <see cref="TryDisposeAsync"/> -- only invoked when
        /// the <see cref="ValueTask"/> was not completed synchronously.
        /// </summary>
        /// <param name="vt">The incomplete <see cref="ValueTask"/> to await.</param>
        /// <returns>The exception thrown during async disposal, or <see langword="null"/> on success.</returns>
        /// <remarks>
        /// <para><b>Separation rationale:</b> Isolating the <see langword="async"/> state machine into this helper
        /// allows <see cref="TryDisposeAsync"/> to remain a non-async method.  On the synchronous fast path (the
        /// overwhelmingly common case), no state machine is allocated at all -- only the genuinely-async path pays the
        /// allocation cost.</para>
        /// </remarks>
        private static async ValueTask<Exception?> AwaitDisposeAsync(ValueTask vt)
        {
            try
            {
                await vt.ConfigureAwait(false);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        /// <summary>
        /// Extracts the exception from a synchronously-faulted <see cref="ValueTask"/> without re-throwing.
        /// </summary>
        /// <param name="vt">A completed, faulted <see cref="ValueTask"/>.</param>
        /// <returns>The exception captured in the <see cref="ValueTask"/>, or a fallback
        /// <see cref="InvalidOperationException"/> if no inner exception could be extracted (should not occur in
        /// practice -- all faulted tasks carry an exception).</returns>
        /// <remarks>
        /// <para><b>Why not GetResult:</b> Calling <see cref="ValueTask.GetAwaiter()"/> and then
        /// <c>GetResult()</c> on a faulted <see cref="ValueTask"/> would re-throw the exception.  Converting to
        /// <see cref="Task"/> via <see cref="ValueTask.AsTask"/> allows inspecting <see cref="Task.Exception"/>
        /// without throwing.</para>
        ///
        /// <para><b>AsTask on completed ValueTask:</b> <see cref="ValueTask.AsTask"/> on an already-completed
        /// <see cref="ValueTask"/> returns a cached or pre-allocated <see cref="Task"/> -- no additional async
        /// machinery is created.  The <see cref="AggregateException"/> wrapper is unwrapped via
        /// <see cref="Exception.InnerException"/> to return the original disposal exception.</para>
        ///
        /// <para><b>Defensive try/catch:</b> The outer <c>try</c>/<c>catch</c> guards against the theoretical case
        /// where <see cref="ValueTask.AsTask"/> itself throws (not expected for a completed <see cref="ValueTask"/>
        /// on .NET 8, but defensive against future runtime changes or corrupted state).</para>
        /// </remarks>
        private static Exception ExtractException(ValueTask vt)
        {
            try
            {
                // AsTask() on a completed ValueTask returns a cached or already-completed Task -- no additional
                // async machinery is created.  The AggregateException wrapper is unwrapped via InnerException.
                Task task = vt.AsTask();
                return task.Exception?.InnerException
                    ?? (Exception?)task.Exception
                    ?? new InvalidOperationException("DisposeAsync returned a faulted ValueTask with no exception.");
            }
            catch (Exception ex)
            {
                // Defensive: if AsTask() itself throws (should not happen for a completed ValueTask).
                return ex;
            }
        }

        #endregion

    }
}
