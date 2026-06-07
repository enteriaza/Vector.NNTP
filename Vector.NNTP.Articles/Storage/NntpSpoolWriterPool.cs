// <copyright file="NntpSpoolWriterPool.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: spool writer worker pool lifecycle and periodic scaling (invoked about once per second by the hosted service).

using Vector.NNTP.Articles.Metrics;

namespace Vector.NNTP.Articles.Storage
{
    /// <summary>
    /// Owns <see cref="NntpSpoolWriterPump"/> worker tasks, applies scaling policy decisions, and coordinates graceful shutdown.
    /// </summary>
    /// <remarks>
    /// <para><b>Scaling policy:</b></para>
    /// <list type="bullet">
    /// <item><description>Startup (<see cref="StartAsync"/>) always activates one writer under <see cref="_gate"/>.</description></item>
    /// <item><description>Scale-up in <see cref="AdjustWriterCountAsync"/> is immediate when desired count exceeds active count.</description></item>
    /// <item>
    /// <description>
    /// Scale-down requires three consecutive <see cref="AdjustWriterCountAsync"/> calls where desired count is below
    /// active count; scale-up or unchanged desired count resets <see cref="_downscaleHysteresisTicks"/>.
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// <see cref="Hosting.NntpSpoolWriterHostedService"/> ticks every second, computes
    /// <see cref="ComputeDesiredWriterCount"/>, and forwards the result to <see cref="AdjustWriterCountAsync"/>.
    /// Scale transition logs (EventId 700) are emitted from the logging partial when the active count changes.
    /// </para>
    /// <para><b>Threading:</b> All <see cref="_workers"/> and <see cref="_hostStopping"/> mutations occur under
    /// <see cref="_gate"/>. Worker cancellation and <c>Task</c> awaits run outside the lock; scaling logs are written
    /// outside the lock.</para>
    /// <para><b>Lifecycle:</b> Pool instances are single-use. <see cref="StartAsync"/> may succeed only once per
    /// instance; <see cref="StopAsync"/> does not reset <see cref="_started"/>. A create → start → stop → dispose host
    /// sequence matches <see cref="Hosting.NntpSpoolWriterHostedService"/>; start → stop → start is not supported.</para>
    /// </remarks>
    internal sealed partial class NntpSpoolWriterPool
    {
        /// <summary>
        /// Serializes mutations to <see cref="_workers"/>, <see cref="_hostStopping"/>, and hysteresis state.
        /// </summary>
        /// <remarks>
        /// Held during worker list changes, startup token assignment, and hysteresis updates. Never held across
        /// <see cref="Task"/> awaits except where callers already exited the lock before awaiting canceled workers.
        /// </remarks>
        private readonly object _gate = new();

        /// <summary>
        /// Bounded spool queue whose depth is forwarded to <see cref="ISpoolWriterScalingPolicy"/> together with capacity.
        /// </summary>
        /// <remarks>
        /// <see cref="ComputeDesiredWriterCount"/> passes <see cref="NntpSpoolWriteQueue.Depth"/> and
        /// <see cref="NntpSpoolWriteQueue.Capacity"/> to the injected policy. The default
        /// <see cref="ProcessorQueueSpoolWriterScalingPolicy"/> uses depth only.
        /// </remarks>
        private readonly NntpSpoolWriteQueue _queue;

        /// <summary>
        /// Shared pump instance executed by each worker <see cref="Task"/> started by this pool.
        /// </summary>
        private readonly NntpSpoolWriterPump _pump;

        /// <summary>
        /// Policy that maps queue depth (and optionally capacity) to a desired writer count between
        /// <see cref="ISpoolWriterScalingPolicy.MinWriters"/> and <see cref="ISpoolWriterScalingPolicy.MaxWriters"/>.
        /// </summary>
        /// <remarks>
        /// Default registration uses <see cref="ProcessorQueueSpoolWriterScalingPolicy"/>, which scales from fixed-depth
        /// backlog tiers and ignores capacity.
        /// </remarks>
        private readonly ISpoolWriterScalingPolicy _scalingPolicy;

        /// <summary>
        /// Metrics sink updated whenever the published active writer count changes.
        /// </summary>
        private readonly NntpSpoolMetrics _metrics;

        /// <summary>
        /// Logger passed to source-generated scaling diagnostics on the logging partial.
        /// </summary>
        private readonly ILogger<NntpSpoolWriterPool> _logger;

        /// <summary>
        /// Active worker cancellation sources and pump tasks. Mutated only under <see cref="_gate"/>.
        /// </summary>
        private readonly List<Worker> _workers = [];

        /// <summary>
        /// Published active writer count mirrored from <see cref="_workers"/> under <see cref="_gate"/>.
        /// </summary>
        /// <remarks>
        /// Written via <see cref="SetActiveWriterCountUnsafe"/> and read lock-free through <see cref="ActiveWriterCount"/>.
        /// </remarks>
        private int _activeWriterCount;

        /// <summary>
        /// Host shutdown token linked into every worker <see cref="CancellationTokenSource"/>, assigned under <see cref="_gate"/>.
        /// </summary>
        private CancellationToken _hostStopping;

        /// <summary>
        /// Single-use startup latch (<c>0</c> = never started, <c>1</c> = started at least once).
        /// </summary>
        /// <remarks>
        /// Set by the winning <see cref="StartAsync"/> caller via atomic compare-exchange and never cleared by
        /// <see cref="StopAsync"/>. Repeat <see cref="StartAsync"/> calls after the first are no-ops; restart after stop
        /// requires a new pool instance.
        /// </remarks>
        private int _started;

        /// <summary>
        /// Consecutive <see cref="AdjustWriterCountAsync"/> observations where desired writers are below the active count.
        /// </summary>
        /// <remarks>
        /// Reset to zero on scale-up, unchanged desired count, or after a successful scale-down. Scale-down executes when
        /// the value reaches three.
        /// </remarks>
        private int _downscaleHysteresisTicks;

        /// <summary>
        /// Initializes a new instance of the <see cref="NntpSpoolWriterPool"/> class.
        /// </summary>
        /// <param name="queue">Bounded transit spool write queue shared with socket threads.</param>
        /// <param name="pump">Writer pump executed by each pool worker.</param>
        /// <param name="scalingPolicy">Policy that computes desired writer counts from queue pressure.</param>
        /// <param name="metrics">Spool metrics recorder shared with the queue and pump.</param>
        /// <param name="logger">Category logger for pool scaling diagnostics.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when any dependency parameter is <see langword="null"/>.
        /// </exception>
        public NntpSpoolWriterPool(
            NntpSpoolWriteQueue queue,
            NntpSpoolWriterPump pump,
            ISpoolWriterScalingPolicy scalingPolicy,
            NntpSpoolMetrics metrics,
            ILogger<NntpSpoolWriterPool> logger)
        {
            ArgumentNullException.ThrowIfNull(queue);
            ArgumentNullException.ThrowIfNull(pump);
            ArgumentNullException.ThrowIfNull(scalingPolicy);
            ArgumentNullException.ThrowIfNull(metrics);
            ArgumentNullException.ThrowIfNull(logger);

            _queue = queue;
            _pump = pump;
            _scalingPolicy = scalingPolicy;
            _metrics = metrics;
            _logger = logger;
        }

        /// <summary>
        /// Gets the current active writer count without acquiring <see cref="_gate"/>.
        /// </summary>
        /// <remarks>
        /// Returns the value last published by <see cref="SetActiveWriterCountUnsafe"/> during startup, scaling, or
        /// shutdown. Suitable for frequent reads from the hosted scaling loop.
        /// </remarks>
        public int ActiveWriterCount => Volatile.Read(ref _activeWriterCount);

        /// <summary>
        /// Starts the pool once and ensures at least one writer task is running.
        /// </summary>
        /// <param name="hostStopping">Host shutdown token linked into each worker cancellation source.</param>
        /// <returns>A completed task; subsequent calls are no-ops.</returns>
        /// <remarks>
        /// <para>
        /// Uses an atomic compare-exchange on <see cref="_started"/> so only the first caller mutates worker state.
        /// <paramref name="hostStopping"/> and the initial worker are created under <see cref="_gate"/>.
        /// </para>
        /// <para>
        /// Subsequent calls return <see cref="Task.CompletedTask"/> without starting workers, including after
        /// <see cref="StopAsync"/> has drained the pool. Pool instances are not restartable.
        /// </para>
        /// </remarks>
        public Task StartAsync(CancellationToken hostStopping)
        {
            if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
            {
                return Task.CompletedTask;
            }

            lock (_gate)
            {
                _hostStopping = hostStopping;
                AddWorkersUnsafe(1);
                this.SetActiveWriterCountUnsafe(_workers.Count);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Computes the desired writer count from current queue depth and configured capacity via the injected policy.
        /// </summary>
        /// <returns>
        /// A value between <see cref="ISpoolWriterScalingPolicy.MinWriters"/> and
        /// <see cref="ISpoolWriterScalingPolicy.MaxWriters"/> from the injected scaling policy.
        /// </returns>
        /// <remarks>
        /// Forwards <see cref="NntpSpoolWriteQueue.Depth"/> and <see cref="NntpSpoolWriteQueue.Capacity"/> to
        /// <see cref="ISpoolWriterScalingPolicy.ComputeDesiredWriters(long, int)"/>. The default
        /// <see cref="ProcessorQueueSpoolWriterScalingPolicy"/> ignores capacity and scales from absolute depth tiers.
        /// </remarks>
        public int ComputeDesiredWriterCount()
        {
            long depth = _queue.Depth;
            return _scalingPolicy.ComputeDesiredWriters(depth, _queue.Capacity);
        }

        /// <summary>
        /// Adjusts the active worker count toward a policy-derived target, applying internal downscale hysteresis.
        /// </summary>
        /// <param name="desiredWriterCount">
        /// Target writer count, typically from <see cref="ComputeDesiredWriterCount"/>. Must lie within scaling policy bounds.
        /// </param>
        /// <param name="cancellationToken">
        /// Token for awaiting canceled workers during scale-down. Host shutdown cancellation propagates; worker
        /// scale-down cancellation is swallowed.
        /// </param>
        /// <returns>A task that completes after worker additions or scale-down drains finish.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="desiredWriterCount"/> is outside
        /// <see cref="ISpoolWriterScalingPolicy.MinWriters"/>..<see cref="ISpoolWriterScalingPolicy.MaxWriters"/>.
        /// </exception>
        /// <remarks>
        /// <para><b>Under <see cref="_gate"/>:</b></para>
        /// <list type="number">
        /// <item><description>Scale-up adds workers immediately and resets hysteresis.</description></item>
        /// <item><description>Scale-down increments hysteresis and removes workers only when hysteresis reaches three.</description></item>
        /// <item><description>Unchanged desired count resets hysteresis without removing workers.</description></item>
        /// </list>
        /// <para>
        /// Removed workers are canceled and awaited outside the lock. An information log is emitted outside the lock when
        /// the active count changes.
        /// </para>
        /// </remarks>
        public async Task AdjustWriterCountAsync(
            int desiredWriterCount,
            CancellationToken cancellationToken)
        {
            if (desiredWriterCount < _scalingPolicy.MinWriters || desiredWriterCount > _scalingPolicy.MaxWriters)
            {
                throw new ArgumentOutOfRangeException(nameof(desiredWriterCount));
            }

            List<Worker>? canceledWorkers = null;
            int previousCount;
            int newCount;
            lock (_gate)
            {
                previousCount = _workers.Count;
                if (desiredWriterCount > previousCount)
                {
                    _downscaleHysteresisTicks = 0;
                    AddWorkersUnsafe(desiredWriterCount - previousCount);
                }
                else if (desiredWriterCount < previousCount)
                {
                    _downscaleHysteresisTicks++;
                    if (_downscaleHysteresisTicks >= 3)
                    {
                        _downscaleHysteresisTicks = 0;
                        int toRemove = previousCount - desiredWriterCount;
                        canceledWorkers = RemoveWorkersUnsafe(toRemove);
                    }
                }
                else
                {
                    _downscaleHysteresisTicks = 0;
                }

                newCount = _workers.Count;
                this.SetActiveWriterCountUnsafe(newCount);
            }

            if (canceledWorkers is not null && canceledWorkers.Count > 0)
            {
                foreach (Worker worker in canceledWorkers)
                {
                    worker.CancellationTokenSource.Cancel();
                }

                foreach (Worker worker in canceledWorkers)
                {
                    try
                    {
                        await worker.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (OperationCanceledException)
                    {
                        // Worker exited due to scale-down cancellation.
                    }
                    catch (Exception)
                    {
                        // Worker failure is already logged by the pump loop.
                    }
                    finally
                    {
                        worker.CancellationTokenSource.Dispose();
                    }
                }
            }

            if (newCount != previousCount)
            {
                SpoolWriterPoolScaled(
                    _logger,
                    previousCount,
                    newCount,
                    _queue.Depth,
                    _queue.Capacity);
            }
        }

        /// <summary>
        /// Completes queue admission, cancels all workers, and awaits pump task termination.
        /// </summary>
        /// <param name="cancellationToken">Host shutdown token for awaiting worker tasks.</param>
        /// <returns>A task that completes when all worker tasks have exited and cancellation sources are disposed.</returns>
        /// <remarks>
        /// <para>
        /// <see cref="NntpSpoolWriteQueue.Complete"/> is called before worker cancellation so remaining queued items can
        /// be drained naturally when the channel completes rather than being left unread.
        /// </para>
        /// <para>Worker and host <see cref="OperationCanceledException"/> outcomes during drain are swallowed.</para>
        /// <para>
        /// Does not reset <see cref="_started"/>; a later <see cref="StartAsync"/> call remains a no-op. Hosts that need
        /// a fresh writer pool must construct a new <see cref="NntpSpoolWriterPool"/> instance.
        /// </para>
        /// </remarks>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _queue.Complete();

            List<Worker> workers;
            lock (_gate)
            {
                workers = [.. _workers];
                _workers.Clear();
                this.SetActiveWriterCountUnsafe(0);
            }

            foreach (Worker worker in workers)
            {
                worker.CancellationTokenSource.Cancel();
            }

            foreach (Worker worker in workers)
            {
                try
                {
                    await worker.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Host or worker cancellation during shutdown drain.
                }
                catch (Exception)
                {
                    // Worker loop handles its own fault logging.
                }
                finally
                {
                    worker.CancellationTokenSource.Dispose();
                }
            }
        }

        /// <summary>
        /// Publishes the active writer count for lock-free reads and metrics emission.
        /// </summary>
        /// <param name="count">Active worker count after a mutation performed under <see cref="_gate"/>.</param>
        /// <remarks>
        /// Caller must hold <see cref="_gate"/>. Updates <see cref="_activeWriterCount"/> with a volatile write and
        /// forwards the value to <see cref="NntpSpoolMetrics.SetActiveWriters"/>.
        /// </remarks>
        private void SetActiveWriterCountUnsafe(int count)
        {
            Volatile.Write(ref _activeWriterCount, count);
            _metrics.SetActiveWriters(count);
        }

        /// <summary>
        /// Starts new pump worker tasks and appends them to <see cref="_workers"/>.
        /// </summary>
        /// <param name="count">Number of workers to add.</param>
        /// <remarks>
        /// <para>
        /// Caller must hold <see cref="_gate"/> and must have assigned <see cref="_hostStopping"/>. Each worker links
        /// <see cref="_hostStopping"/> into a dedicated <see cref="CancellationTokenSource"/> and runs
        /// <see cref="NntpSpoolWriterPump.RunAsync"/> on a thread-pool thread via <see cref="Task.Run(Func{Task}, CancellationToken)"/>.
        /// </para>
        /// </remarks>
        private void AddWorkersUnsafe(int count)
        {
            for (int i = 0; i < count; i++)
            {
                CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(_hostStopping);
                Task task = Task.Run(() => _pump.RunAsync(cts.Token), CancellationToken.None);
                _workers.Add(new Worker(cts, task));
            }
        }

        /// <summary>
        /// Detaches the most recently added workers from <see cref="_workers"/> for cancellation outside the lock.
        /// </summary>
        /// <param name="count">Number of workers to remove from the tail of the active list.</param>
        /// <returns>Removed worker descriptors to cancel, await, and dispose outside <see cref="_gate"/>.</returns>
        /// <remarks>
        /// Caller must hold <see cref="_gate"/>. Workers are not canceled here; scale-down awaits happen in
        /// <see cref="AdjustWriterCountAsync"/> and shutdown awaits in <see cref="StopAsync"/>.
        /// </remarks>
        private List<Worker> RemoveWorkersUnsafe(int count)
        {
            List<Worker> removed = [];
            for (int i = 0; i < count && _workers.Count > 0; i++)
            {
                int last = _workers.Count - 1;
                removed.Add(_workers[last]);
                _workers.RemoveAt(last);
            }

            return removed;
        }

        /// <summary>
        /// Tracks one spool writer worker's cancellation source and execution task.
        /// </summary>
        /// <param name="CancellationTokenSource">Linked token source combining host stop and per-worker cancel.</param>
        /// <param name="Task">Thread-pool task running <see cref="NntpSpoolWriterPump.RunAsync"/>.</param>
        /// <remarks>
        /// Instances are created by <see cref="AddWorkersUnsafe"/> and removed by <see cref="RemoveWorkersUnsafe"/> under
        /// <see cref="_gate"/>. Scale-down and shutdown cancel the source, await <paramref name="Task"/>, then dispose the
        /// source outside the lock.
        /// </remarks>
        private sealed record Worker(
            CancellationTokenSource CancellationTokenSource,
            Task Task);
    }
}
