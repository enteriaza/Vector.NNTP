// <copyright file="TaskUtilities.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: diagnostics/formatting/startup validation; readability over micro-optimization; allocations acceptable.
// TaskUtilities.cs -- Helpers for observing fire-and-forget Task exceptions and shared cancellable delay primitives.

namespace Vector.NNTP.Utilities.Async
{
    /// <summary>
    /// Helpers for observing fire-and-forget <see cref="Task"/> exceptions to prevent
    /// <see cref="TaskScheduler.UnobservedTaskException"/> events, and shared delay/cancellation primitives for background
    /// loops.
    /// </summary>
    /// <remarks>
    /// <para><b>Rationale:</b> Unobserved task exceptions are swallowed by default on modern runtimes, but the
    /// <see cref="TaskScheduler.UnobservedTaskException"/> event still fires and can create diagnostic noise. Observing
    /// exceptions makes the intent explicit and protects against compatibility-switch changes.</para>
    /// </remarks>
    public static class TaskUtilities
    {
        /// <summary>
        /// Cached continuation delegate that marks a faulted task's exception as observed.
        /// </summary>
        private static readonly Action<Task> ObserveExceptionContinuation = static t => _ = t.Exception;

        /// <summary>
        /// Ensures a task's exception (if any) is observed.
        /// </summary>
        /// <param name="task">The task to observe. May be <see langword="null"/>.</param>
        public static void ObserveException(Task? task)
        {
            if (task is null)
            {
                return;
            }

            if (task.IsCompleted)
            {
                if (task.IsFaulted)
                {
                    _ = task.Exception;
                }

                return;
            }

            _ = task.ContinueWith(
                ObserveExceptionContinuation,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }

        /// <summary>
        /// Observes exceptions on all tasks in <paramref name="tasks"/>, optionally skipping one task that has already been
        /// awaited.
        /// </summary>
        /// <typeparam name="T">The task result type.</typeparam>
        /// <param name="tasks">The task list. Must not be <see langword="null"/>.</param>
        /// <param name="excludeTask">A task to skip (reference equality), or <see langword="null"/> to observe all.</param>
        public static void ObserveExceptions<T>(List<Task<T>> tasks, Task? excludeTask)
        {
            ArgumentNullException.ThrowIfNull(tasks);

            for (int i = 0; i < tasks.Count; i++)
            {
                Task<T> task = tasks[i];
                if (ReferenceEquals(task, excludeTask))
                {
                    continue;
                }

                ObserveException(task);
            }
        }

        /// <summary>
        /// Observes exceptions on all tasks in <paramref name="tasks"/>, optionally skipping one task that has already been
        /// awaited.
        /// </summary>
        /// <param name="tasks">The task list. Must not be <see langword="null"/>.</param>
        /// <param name="excludeTask">A task to skip (reference equality), or <see langword="null"/> to observe all.</param>
        public static void ObserveExceptions(IReadOnlyList<Task> tasks, Task? excludeTask = null)
        {
            ArgumentNullException.ThrowIfNull(tasks);

            for (int i = 0; i < tasks.Count; i++)
            {
                Task task = tasks[i];
                if (ReferenceEquals(task, excludeTask))
                {
                    continue;
                }

                ObserveException(task);
            }
        }

        /// <summary>
        /// Delays for <paramref name="delay"/> unless <paramref name="ct"/> is cancelled. Returns a boolean instead of
        /// throwing <see cref="OperationCanceledException"/>.
        /// </summary>
        /// <param name="delay">The delay duration.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns><see langword="true"/> if the delay elapsed; <see langword="false"/> if cancelled.</returns>
        public static async Task<bool> DelayOrCancelledAsync(TimeSpan delay, CancellationToken ct)
        {
            if (ct.IsCancellationRequested)
            {
                return false;
            }

            try
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return false;
            }
        }

        /// <summary>
        /// Delays for <paramref name="delayMs"/> milliseconds unless <paramref name="ct"/> is cancelled. Returns a boolean
        /// instead of throwing <see cref="OperationCanceledException"/>.
        /// </summary>
        /// <param name="delayMs">Delay in milliseconds.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns><see langword="true"/> if the delay elapsed; <see langword="false"/> if cancelled.</returns>
        public static Task<bool> DelayOrCancelledAsync(int delayMs, CancellationToken ct)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(delayMs);
            return DelayOrCancelledAsync(TimeSpan.FromMilliseconds(delayMs), ct);
        }
    }
}
