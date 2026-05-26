// <copyright file="HostHealthTracker.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HostHealthTracker.cs -- Per-broker-host reconnect backoff and temporary suppression after TCP failures.
//
// Registered in DI for future pool host-rotation integration. Applies full-jitter delays between attempts to a failing
// entry in RabbitMQOptions.Hosts.
//
// Thread safety:
//   Dictionary guarded by lock; per-host AttemptCount uses Interlocked.

using MessageBus.Configuration;

namespace MessageBus.Connections
{
    /// <summary>
    /// Tracks per-broker-host reconnect backoff and temporary suppression after connection failures.
    /// </summary>
    /// <remarks>
    /// <para><b>Purpose:</b> When opening a new TCP connection, the pool may rotate across
    /// <see cref="RabbitMQOptions.Hosts"/>. This type prevents hammering a failing endpoint by applying full-jitter delays
    /// between attempts.</para>
    /// <para><b>Configuration:</b> Uses <see cref="RabbitMQOptions.PoolReconnectBaseDelayMs"/> and
    /// <see cref="RabbitMQOptions.PoolReconnectMaxDelayMs"/>.</para>
    /// <para><b>Thread safety:</b> Dictionary access is serialized with a lock; per-host counters use
    /// <see cref="Interlocked"/>.</para>
    /// </remarks>
    public sealed class HostHealthTracker
    {
        /// <summary>Bound RabbitMQ options snapshot.</summary>
        private readonly IOptions<RabbitMQOptions> _options;

        /// <summary>Per-host-index failure and suppression state.</summary>
        private readonly Dictionary<int, HostState> _hosts = [];

        /// <summary>Initializes a new instance of the <see cref="HostHealthTracker"/> class.</summary>
        /// <param name="options">RabbitMQ options providing reconnect delay bounds.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
        public HostHealthTracker(IOptions<RabbitMQOptions> options)
        {
            ArgumentNullException.ThrowIfNull(options);
            _options = options;
        }

        /// <summary>
        /// Computes the next reconnect delay for a host using exponential cap and full jitter.
        /// </summary>
        /// <param name="hostIndex">Zero-based index into <see cref="RabbitMQOptions.Hosts"/>.</param>
        /// <returns>Delay before the next reconnect attempt to that host.</returns>
        /// <remarks>
        /// <para>Each call increments the host's attempt counter. Jitter is uniform in
        /// [<see cref="RabbitMQOptions.PoolReconnectBaseDelayMs"/>, cap].</para>
        /// </remarks>
        public TimeSpan GetReconnectDelay(int hostIndex)
        {
            RabbitMQOptions options = _options.Value;
            HostState state = GetOrCreate(hostIndex);
            int attempt = Interlocked.Increment(ref state.AttemptCount);
            int cap = Math.Min(options.PoolReconnectMaxDelayMs, options.PoolReconnectBaseDelayMs * (1 << Math.Min(attempt, 10)));
            int jitter = Random.Shared.Next(options.PoolReconnectBaseDelayMs, cap + 1);
            return TimeSpan.FromMilliseconds(jitter);
        }

        /// <summary>Resets failure state after a successful connection to the host.</summary>
        /// <param name="hostIndex">Zero-based index into <see cref="RabbitMQOptions.Hosts"/>.</param>
        public void RecordSuccess(int hostIndex)
        {
            HostState state = GetOrCreate(hostIndex);
            _ = Interlocked.Exchange(ref state.AttemptCount, 0);
            state.SuppressedUntilUtc = null;
        }

        /// <summary>Records a connection failure and suppresses further attempts until the jittered delay elapses.</summary>
        /// <param name="hostIndex">Zero-based index into <see cref="RabbitMQOptions.Hosts"/>.</param>
        public void RecordFailure(int hostIndex)
        {
            HostState state = GetOrCreate(hostIndex);
            TimeSpan delay = GetReconnectDelay(hostIndex);
            state.SuppressedUntilUtc = DateTimeOffset.UtcNow.Add(delay);
        }

        /// <summary>Returns whether reconnect attempts to the host should be skipped temporarily.</summary>
        /// <param name="hostIndex">Zero-based index into <see cref="RabbitMQOptions.Hosts"/>.</param>
        /// <returns><see langword="true"/> when the host is inside its suppression window.</returns>
        public bool IsSuppressed(int hostIndex)
        {
            HostState state = GetOrCreate(hostIndex);
            return state.SuppressedUntilUtc is { } until && until > DateTimeOffset.UtcNow;
        }

        /// <summary>Gets or creates mutable state for <paramref name="hostIndex"/>.</summary>
        /// <param name="hostIndex">Zero-based host index.</param>
        /// <returns>Per-host tracking object.</returns>
        private HostState GetOrCreate(int hostIndex)
        {
            lock (_hosts)
            {
                if (!_hosts.TryGetValue(hostIndex, out HostState? state))
                {
                    state = new HostState();
                    _hosts[hostIndex] = state;
                }
                return state;
            }
        }

        /// <summary>Mutable reconnect state for one entry in <see cref="RabbitMQOptions.Hosts"/>.</summary>
        private sealed class HostState
        {
            /// <summary>Consecutive failure count used to widen backoff cap.</summary>
            internal int AttemptCount;

            /// <summary>UTC instant after which the host may be tried again; <see langword="null"/> when not suppressed.</summary>
            internal DateTimeOffset? SuppressedUntilUtc;
        }
    }
}
