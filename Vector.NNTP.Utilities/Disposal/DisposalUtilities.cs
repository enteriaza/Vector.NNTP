// <copyright file="DisposalUtilities.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: diagnostics/formatting/startup validation; readability over micro-optimization; allocations acceptable.
// DisposalUtilities.cs -- Best-effort disposal helpers for resources that may be in a terminal or already-disposed state.
//
// Provides TryDispose (synchronous) and TryDisposeAsync (asynchronous) wrappers that swallow exceptions thrown during
// disposal, returning them to the caller for optional diagnostic logging. This eliminates repetitive per-resource
// try/catch blocks in Dispose and DisposeAsync implementations.
//
// Allocation characteristics:
//   - TryDispose / TryDisposeAll: zero allocations on the success path.
//   - TryDisposeAsync: zero async-state-machine allocation when DisposeAsync completes synchronously; a state machine
//     is materialised only when the underlying ValueTask is genuinely incomplete.
//
// Thread safety:
//   All methods are static and stateless. Safe for concurrent use from any number of threads.

namespace Vector.NNTP.Utilities.Disposal
{
    /// <summary>
    /// Best-effort disposal helpers for resources that may be in a terminal or already-disposed state.
    /// </summary>
    /// <remarks>
    /// <para><b>Rationale:</b> Shutdown, error-recovery, and cleanup paths frequently need to dispose resources that may
    /// already be broken -- a faulted connection, a disposed socket, or a timed-out stream. Wrapping each disposal in its
    /// own <c>try</c>/<c>catch</c> is verbose and error-prone; this type centralises the pattern.</para>
    ///
    /// <para><b>Exception return vs. logging:</b> Caught exceptions are returned to the caller rather than logged directly.
    /// The disposal helper has no knowledge of the resource's role or the appropriate severity.</para>
    ///
    /// <para><b>Thread safety:</b> All methods are <see langword="static"/> and stateless. Safe for concurrent use from any
    /// number of threads without synchronisation.</para>
    ///
    /// <para><b>Performance:</b> COLD PATH -- success paths avoid allocations where possible; async paths may materialise
    /// state machines when disposal is genuinely asynchronous.</para>
    /// </remarks>
    public static class DisposalUtilities
    {
        /// <summary>
        /// Disposes an <see cref="IDisposable"/> resource, swallowing any exception. Returns the caught exception (if any)
        /// for optional caller logging.
        /// </summary>
        /// <param name="disposable">The resource to dispose. May be <see langword="null"/> (no-op).</param>
        /// <returns>The exception thrown by <see cref="IDisposable.Dispose"/>, or <see langword="null"/> on success or when
        /// <paramref name="disposable"/> is <see langword="null"/>.</returns>
        public static Exception? TryDispose(IDisposable? disposable)
        {
            if (disposable is null)
            {
                return null;
            }

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
        /// Disposes each <see cref="IDisposable"/> resource in <paramref name="disposables"/> independently, swallowing
        /// exceptions from each. Returns the first caught exception (if any) for optional caller logging.
        /// </summary>
        /// <param name="disposables">The resources to dispose, in order. Individual elements may be <see langword="null"/>
        /// (skipped). The span itself may be empty (no-op).</param>
        /// <returns>The first exception thrown by any <see cref="IDisposable.Dispose"/> call, or <see langword="null"/> if
        /// all disposals succeeded.</returns>
        public static Exception? TryDisposeAll(ReadOnlySpan<IDisposable?> disposables)
        {
            Exception? first = null;

            for (int i = 0; i < disposables.Length; i++)
            {
                IDisposable? resource = disposables[i];
                if (resource is null)
                {
                    continue;
                }

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

        /// <summary>
        /// Asynchronously disposes an <see cref="IAsyncDisposable"/> resource, swallowing any exception. Returns the caught
        /// exception (if any) for optional caller logging.
        /// </summary>
        /// <param name="disposable">The resource to dispose. May be <see langword="null"/> (no-op).</param>
        /// <returns>A <see cref="ValueTask{TResult}"/> that completes with the exception thrown by
        /// <see cref="IAsyncDisposable.DisposeAsync"/>, or <see langword="null"/> on success or when
        /// <paramref name="disposable"/> is <see langword="null"/>.</returns>
        public static ValueTask<Exception?> TryDisposeAsync(IAsyncDisposable? disposable)
        {
            if (disposable is null)
            {
                return default;
            }

            ValueTask vt;
            try
            {
                vt = disposable.DisposeAsync();
            }
            catch (Exception ex)
            {
                return new ValueTask<Exception?>(ex);
            }

            return vt.IsCompletedSuccessfully
                ? default
                : vt.IsCompleted ? new ValueTask<Exception?>(ExtractException(vt)) : AwaitDisposeAsync(vt);
        }

        /// <summary>
        /// Asynchronously disposes each resource in <paramref name="disposables"/> independently, swallowing exceptions
        /// from each and returning the first caught exception (if any).
        /// </summary>
        /// <param name="disposables">The resources to dispose, in order. Individual elements may be <see langword="null"/>
        /// (skipped).</param>
        /// <returns>A task that completes with the first disposal exception, or <see langword="null"/> if all disposals
        /// succeeded.</returns>
        public static async ValueTask<Exception?> TryDisposeAllAsync(params IAsyncDisposable?[] disposables)
        {
            ArgumentNullException.ThrowIfNull(disposables);

            Exception? first = null;

            for (int i = 0; i < disposables.Length; i++)
            {
                IAsyncDisposable? resource = disposables[i];
                if (resource is null)
                {
                    continue;
                }

                ValueTask vt;
                try
                {
                    vt = resource.DisposeAsync();
                }
                catch (Exception ex)
                {
                    first ??= ex;
                    continue;
                }

                if (vt.IsCompletedSuccessfully)
                {
                    continue;
                }

                if (vt.IsCompleted)
                {
                    first ??= ExtractException(vt);
                    continue;
                }

                try
                {
                    await vt.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    first ??= ex;
                }
            }

            return first;
        }

        /// <summary>
        /// Awaits a disposal <see cref="ValueTask"/>, converting success to <see langword="null"/> and failure to the
        /// caught exception.
        /// </summary>
        /// <param name="vt">The disposal task.</param>
        /// <returns><see langword="null"/> on success; the caught exception on failure.</returns>
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
        /// Extracts the exception from a synchronously completed, unsuccessful <see cref="ValueTask"/>.
        /// </summary>
        /// <param name="vt">The completed task.</param>
        /// <returns>The exception observed from <paramref name="vt"/>.</returns>
        private static Exception ExtractException(ValueTask vt)
        {
            try
            {
                vt.GetAwaiter().GetResult();
                return new InvalidOperationException("ValueTask completed unsuccessfully, but no exception was observed.");
            }
            catch (Exception ex)
            {
                return ex;
            }
        }
    }
}
