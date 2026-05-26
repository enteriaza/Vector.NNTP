// TaskUtilities.cs -- Lightweight helpers for observing fire-and-forget Task exceptions to prevent
// TaskScheduler.UnobservedTaskException events, and shared async delay/cancellation primitives for
// background worker loops.
//
// Without these helpers, every fire-and-forget site and every background worker loop duplicates the same boilerplate.
// These helpers provide single, tested locations with clear documentation.
//
// While .NET 8 defaults to swallowing unobserved task exceptions (ThrowUnobservedTaskExceptions=false), explicitly
// observing them is a defensive measure that:
//   1. Prevents diagnostic noise from the UnobservedTaskException event handler in Program.Logging.cs.
//   2. Protects against process termination if the compatibility switch is ever enabled.
//   3. Makes the intent explicit at call sites -- "we know this task may fault, and we're ok with it."
//
// All methods are static and thread-safe.  No shared mutable state exists.
//
// Cross-platform:
//   Fully portable.  All methods use BCL APIs (Task, CancellationToken) available on all .NET 8 runtimes
//   (Windows x64, Linux x64).  No P/Invoke, no OS-specific APIs.
//
// SIMD applicability:
//   Not applicable.  All operations are Task continuation registration and async delay -- no data buffers or
//   vectorisable paths.

using System.Runtime.CompilerServices;

namespace MessageBus.Utilities
{
    /// <summary>
    /// Lightweight helpers for observing fire-and-forget <see cref="Task"/> exceptions to prevent
    /// <see cref="TaskScheduler.UnobservedTaskException"/> events during GC finalisation, and shared async
    /// delay/cancellation primitives for background worker loops.
    /// </summary>
    /// <remarks>
    /// <para><b>Rationale:</b> When a <see cref="Task"/> completes in a faulted state and no code ever observes its
    /// <see cref="Task.Exception"/> property (via <see langword="await"/>, <c>.Result</c>, <c>.Wait()</c>, or
    /// <c>.Exception</c>), the GC finaliser raises <see cref="TaskScheduler.UnobservedTaskException"/>.  In .NET 8
    /// this does not terminate the process by default (<c>ThrowUnobservedTaskExceptions=false</c>), but the event
    /// still fires — causing diagnostic noise in the handler registered in <c>Program.Logging.cs</c> and risking
    /// process termination if the compatibility switch is ever enabled.</para>
    ///
    /// <para><b>Pattern replaced:</b> Multiple call sites duplicated the same continuation pattern:</para>
    /// <code>
    /// task?.ContinueWith(
    ///     static t =&gt; { _ = t.Exception; },
    ///     CancellationToken.None,
    ///     TaskContinuationOptions.OnlyOnFaulted,
    ///     TaskScheduler.Default);
    /// </code>
    /// <para>This class centralises that pattern into a single method with clear semantics.</para>
    ///
    /// <para><b>DelayOrCancelledAsync:</b> Background worker loops frequently need a cancellable delay that returns
    /// a boolean instead of throwing <see cref="OperationCanceledException"/>.  This avoids the exception propagating
    /// through <see langword="catch"/> blocks where it would mask the original exception context.</para>
    ///
    /// <para><b>Thread safety:</b> All methods are <see langword="static"/> with no shared mutable state.  Safe for
    /// concurrent use from any number of threads without synchronisation.</para>
    ///
    /// <para><b>Allocation characteristics:</b></para>
    /// <list type="bullet">
    ///   <item><description><see cref="ObserveException(Task)"/> — when the task is <see langword="null"/>, already
    ///     completed successfully, or already cancelled: zero allocation.  When the task is already faulted: zero
    ///     allocation (reads <see cref="Task.Exception"/> inline).  When the task is still running: one
    ///     <see cref="Task"/> continuation object (~72 bytes) — the same cost as the manual
    ///     <see cref="Task.ContinueWith(Action{Task}, CancellationToken, TaskContinuationOptions, TaskScheduler)"/>
    ///     pattern it replaces.</description></item>
    ///   <item><description><see cref="ObserveExceptions{T}(List{Task{T}}, Task)"/> — iterates the list once, calling
    ///     <see cref="ObserveException(Task)"/> per element (skipping the excluded task).  Zero allocation when all
    ///     tasks are completed or cancelled; one continuation per still-running task.</description></item>
    ///   <item><description><see cref="DelayOrCancelledAsync(TimeSpan, CancellationToken)"/> — on the cancellation path, checks
    ///     <see cref="CancellationToken.IsCancellationRequested"/> before awaiting to avoid allocating the
    ///     <see cref="OperationCanceledException"/> that <see cref="Task.Delay(TimeSpan, CancellationToken)"/>
    ///     would throw.  On the normal path, zero allocation when the delay completes synchronously (very short
    ///     delays).  One async state machine allocation (~100 bytes) for genuinely asynchronous
    ///     delays.</description></item>
    /// </list>
    ///
    /// <para><b>Cross-platform:</b> Fully portable.  All methods use BCL APIs (<see cref="Task"/>,
    /// <see cref="CancellationToken"/>) available on all .NET 8 runtimes (Windows x64, Linux x64).  No P/Invoke,
    /// no OS-specific APIs.</para>
    ///
    /// <para><b>SIMD applicability:</b> Not applicable.  All operations are Task continuation registration and async
    /// delay — no data buffers or vectorisable paths.</para>
    ///
    /// <para><b>Logging:</b> This class contains no logging.  It is a stateless utility with no DI dependencies.
    /// Callers are responsible for logging when they use these helpers.</para>
    /// </remarks>
    internal static class TaskUtilities
    {

        #region Constants

        /// <summary>
        /// Cached <see langword="static"/> delegate for the
        /// <see cref="Task.ContinueWith(Action{Task}, CancellationToken, TaskContinuationOptions, TaskScheduler)"/>
        /// continuation that observes a faulted task's exception.
        /// </summary>
        /// <remarks>
        /// <para><b>Rationale:</b> Although the C# compiler caches <see langword="static"/> lambdas in a hidden
        /// <c>&lt;&gt;O</c> field automatically, extracting the delegate into an explicit field makes the caching
        /// intent visible in the source and guarantees a single delegate allocation for the lifetime of the process
        /// regardless of compiler version or optimisation level.</para>
        ///
        /// <para><b>Allocation:</b> One <see cref="Action{T}"/> delegate (~32 bytes) allocated at class load time.
        /// Shared across all <see cref="ObserveException(Task?)"/> calls for the process lifetime.</para>
        /// </remarks>
        private static readonly Action<Task> ObserveExceptionContinuation = static t => _ = t.Exception;

        #endregion

        #region Public Methods — Single Task

        /// <summary>
        /// Ensures that a fire-and-forget <see cref="Task"/>'s exception (if any) is observed, preventing
        /// <see cref="TaskScheduler.UnobservedTaskException"/> from firing during GC finalisation.
        /// </summary>
        /// <param name="task">The task to observe.  May be <see langword="null"/> (no-op), already completed,
        /// or still running.</param>
        /// <remarks>
        /// <para><b>Completion states:</b></para>
        /// <list type="bullet">
        ///   <item><description><see langword="null"/>: No-op.</description></item>
        ///   <item><description>Already faulted: Reads <see cref="Task.Exception"/> inline to mark it observed.
        ///     Zero allocation.</description></item>
        ///   <item><description>Already completed successfully or cancelled: No-op — no exception to
        ///     observe.</description></item>
        ///   <item><description>Still running: Attaches a lightweight continuation via
        ///     <see cref="TaskContinuationOptions.OnlyOnFaulted"/> that reads <see cref="Task.Exception"/> when
        ///     the task eventually faults.  If the task completes successfully or is cancelled, the continuation
        ///     is never invoked (zero overhead).</description></item>
        /// </list>
        ///
        /// <para><b>Cached delegate:</b> The continuation delegate is the pre-allocated
        /// <see cref="ObserveExceptionContinuation"/> field — a <see langword="static"/> lambda that captures
        /// nothing and simply reads <see cref="Task.Exception"/> to mark the exception as observed.  No per-call
        /// delegate allocation occurs.</para>
        ///
        /// <para><b>TaskScheduler:</b> Uses <see cref="TaskScheduler.Default"/> (thread-pool) rather than
        /// <see cref="TaskContinuationOptions.ExecuteSynchronously"/> because the observation continuation should
        /// not execute on the faulting thread (which may be an I/O completion thread or a timer callback thread
        /// with scheduling constraints).</para>
        /// </remarks>
        public static void ObserveException(Task? task)
        {
            if (task is null)
                return;
            if (task.IsCompleted)
            {
                // Already done — observe inline if faulted, otherwise nothing to do.
                if (task.IsFaulted)
                    _ = task.Exception;
                return;
            }
            // Still running — attach a continuation that fires only on fault.
            _ = task.ContinueWith(
                ObserveExceptionContinuation,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }

        #endregion

        #region Public Methods — Task List

        /// <summary>
        /// Observes exceptions on all tasks in <paramref name="tasks"/>, optionally skipping one task that has
        /// already been awaited.  Prevents <see cref="TaskScheduler.UnobservedTaskException"/> events from
        /// abandoned fire-and-forget tasks in a list.
        /// </summary>
        /// <typeparam name="T">The result type of the tasks.</typeparam>
        /// <param name="tasks">The list of in-flight tasks — some may still be running, some may already be
        /// completed, faulted, or cancelled.  Must not be <see langword="null"/>.</param>
        /// <param name="excludeTask">A task that has already been awaited and should be skipped.  Pass
        /// <see langword="null"/> to observe all tasks in the list.</param>
        /// <remarks>
        /// <para><b>Use case:</b> Multi-hedged request patterns launch multiple concurrent tasks and return as
        /// soon as the first one succeeds.  The remaining tasks are abandoned — their
        /// <see cref="CancellationTokenSource"/> is cancelled, but they may fault before the cancellation propagates.
        /// This method ensures any such faulted tasks have their exceptions observed.</para>
        ///
        /// <para><b>Exclude semantics:</b> The <paramref name="excludeTask"/> is compared by reference via
        /// <see cref="object.ReferenceEquals"/>.  This avoids the overhead of a value-equality check and matches
        /// the semantics of <c>Task.WhenAny</c> which returns the winning task by reference.  At most one
        /// task is skipped — if the list contains multiple references to the same task object (which should not
        /// occur), only the first is skipped.</para>
        ///
        /// <para><b>Delegation:</b> Each non-excluded task is observed via <see cref="ObserveException(Task?)"/>,
        /// which provides the same fast-path checks and continuation attachment as the single-task overload.</para>
        ///
        /// <para><b>Cost:</b> One iteration over <paramref name="tasks"/> (<c>O(n)</c> where <c>n</c> is typically
        /// ≤ 3 for peer hedge tasks).  Per-task cost is zero for completed/cancelled tasks and one continuation
        /// (~72 bytes) for still-running tasks.</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="tasks"/> is
        /// <see langword="null"/>.</exception>
        public static void ObserveExceptions<T>(List<Task<T>> tasks, Task? excludeTask)
        {
            ArgumentNullException.ThrowIfNull(tasks);
            for (int i = 0; i < tasks.Count; i++)
            {
                Task<T> task = tasks[i];
                if (ReferenceEquals(task, excludeTask))
                    continue;
                ObserveException(task);
            }
        }

        /// <summary>
        /// Observes exceptions on all non-generic tasks in <paramref name="tasks"/>, optionally skipping one task
        /// that has already been awaited.  Non-generic counterpart to
        /// <see cref="ObserveExceptions{T}(List{Task{T}}, Task?)"/> for callers that work with <see cref="Task"/>
        /// rather than <see cref="Task{TResult}"/>.
        /// </summary>
        /// <param name="tasks">The collection of in-flight tasks.  Must not be <see langword="null"/>.  Accepts
        /// <see cref="IReadOnlyList{T}"/> to support both <see cref="List{T}"/> and array-backed collections
        /// without requiring a specific concrete type.</param>
        /// <param name="excludeTask">A task that has already been awaited and should be skipped.  Pass
        /// <see langword="null"/> to observe all tasks in the list.</param>
        /// <remarks>
        /// <para><b>Use case:</b> Callers that work with <c>ICollection&lt;Task&gt;</c> snapshots from
        /// <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey,TValue}.Values"/>
        /// can convert to a list/array and call this overload to observe all abandoned session tasks in a single
        /// call.</para>
        ///
        /// <para><b>Interface choice:</b> Accepts <see cref="IReadOnlyList{T}"/> rather than
        /// <see cref="IEnumerable{T}"/> to enable indexed iteration without allocating an enumerator.  Both
        /// <see cref="List{T}"/> and <c>Task[]</c> implement <see cref="IReadOnlyList{T}"/>, covering the primary
        /// use cases.</para>
        ///
        /// <para><b>Delegation:</b> Each non-excluded task is observed via <see cref="ObserveException(Task?)"/>,
        /// identical to the generic overload.</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="tasks"/> is
        /// <see langword="null"/>.</exception>
        public static void ObserveExceptions(IReadOnlyList<Task> tasks, Task? excludeTask = null)
        {
            ArgumentNullException.ThrowIfNull(tasks);
            for (int i = 0; i < tasks.Count; i++)
            {
                Task task = tasks[i];
                if (ReferenceEquals(task, excludeTask))
                    continue;
                ObserveException(task);
            }
        }

        #endregion

        #region Public Methods — Cancellable Delay

        /// <summary>
        /// Awaits a delay, returning <see langword="true"/> if the delay completed normally or <see langword="false"/>
        /// if cancellation was requested.  This avoids <see cref="OperationCanceledException"/> propagating through
        /// <see langword="catch"/> blocks where it would mask the original exception context.
        /// </summary>
        /// <param name="delay">The duration to wait.  Values ≤ <see cref="TimeSpan.Zero"/> return
        /// <see langword="true"/> immediately (no-op delay).</param>
        /// <param name="ct">Cancellation token.  When already cancelled on entry, returns <see langword="false"/>
        /// immediately without allocating.</param>
        /// <returns><see langword="true"/> if the delay elapsed normally; <see langword="false"/> if cancelled.</returns>
        /// <remarks>
        /// <para><b>Use case:</b> Background worker loops <c>AdaptiveTuningLoopAsync</c>) that need a cancellable delay
        /// between iterations.  Without this helper, each worker must wrap <see cref="Task.Delay(TimeSpan, CancellationToken)"/>
        /// in a <c>try</c>/<c>catch (OperationCanceledException)</c> block — duplicating the same boilerplate and risking
        /// accidental exception swallowing when the catch is placed incorrectly.</para>
        ///
        /// <para><b>Pattern:</b> Returns a boolean instead of throwing, enabling the caller to use a simple
        /// <c>if (!await DelayOrCancelledAsync(...))</c> guard rather than a try/catch block.  This is cleaner in
        /// loops with multiple delay points (each would otherwise need its own try/catch).</para>
        ///
        /// <para><b>Fast paths:</b></para>
        /// <list type="bullet">
        ///   <item><description>Already cancelled: <see cref="CancellationToken.IsCancellationRequested"/> is checked
        ///     before calling <see cref="Task.Delay(TimeSpan, CancellationToken)"/>, returning <see langword="false"/>
        ///     immediately without allocating the <see cref="OperationCanceledException"/> that <c>Task.Delay</c>
        ///     would throw.  This eliminates a ~200-byte exception allocation on the cancellation hot
        ///     path.</description></item>
        ///   <item><description>Zero or negative delay: Returns <see langword="true"/> immediately without entering
        ///     the timer subsystem.</description></item>
        /// </list>
        ///
        /// <para><b>Allocation:</b> Zero allocation on all fast paths (already cancelled, zero delay, synchronous
        /// timer completion).  One async state machine allocation (~100 bytes) for genuinely asynchronous delays.
        /// On the async cancellation path, the <see cref="OperationCanceledException"/> allocated by
        /// <see cref="Task.Delay(TimeSpan, CancellationToken)"/> is caught and discarded — no additional allocation
        /// beyond the exception itself.</para>
        ///
        /// <para><b>Cross-platform:</b> Fully portable.  <see cref="Task.Delay(TimeSpan, CancellationToken)"/> and
        /// <see cref="OperationCanceledException"/> are BCL APIs available on all .NET 8 runtimes.</para>
        /// </remarks>
        public static async Task<bool> DelayOrCancelledAsync(TimeSpan delay, CancellationToken ct)
        {
            // Fast path: already cancelled — avoid the Task.Delay allocation and the OperationCanceledException
            // that it would throw (~200 bytes saved).
            if (ct.IsCancellationRequested)
                return false;
            // Fast path: zero or negative delay — no need to enter the timer subsystem.
            if (delay <= TimeSpan.Zero)
                return true;
            try
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        /// <summary>
        /// Awaits a delay specified in milliseconds, returning <see langword="true"/> if the delay completed normally
        /// or <see langword="false"/> if cancellation was requested.  Convenience overload that avoids the
        /// <see cref="TimeSpan"/> construction at call sites that already have a millisecond value.
        /// </summary>
        /// <param name="delayMs">The duration to wait in milliseconds.  Values ≤ 0 return <see langword="true"/>
        /// immediately (no-op delay).</param>
        /// <param name="ct">Cancellation token.  When already cancelled on entry, returns <see langword="false"/>
        /// immediately without allocating.</param>
        /// <returns><see langword="true"/> if the delay elapsed normally; <see langword="false"/> if cancelled.</returns>
        /// <remarks>
        /// <para><b>Delegation:</b> Forwards to <see cref="DelayOrCancelledAsync(TimeSpan, CancellationToken)"/>
        /// after converting the millisecond value to <see cref="TimeSpan"/> via
        /// <see cref="TimeSpan.FromMilliseconds(double)"/>.  The conversion is a trivial multiplication — no heap
        /// allocation.</para>
        ///
        /// <para><b>Use case:</b> Callers that compute delays from <see cref="RetryUtilities.CalculateBackOff"/>
        /// (which returns <see cref="int"/> milliseconds) can call this overload directly without constructing a
        /// <see cref="TimeSpan"/>.</para>
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Task<bool> DelayOrCancelledAsync(int delayMs, CancellationToken ct)
        {
            return DelayOrCancelledAsync(TimeSpan.FromMilliseconds(delayMs), ct);
        }

        #endregion

    }
}
