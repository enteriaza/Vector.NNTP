// <copyright file="RedisHostHealthTracker.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Session.Redis.Connections
{
    /// <summary>
    /// Per-host reconnect backoff after multiplexer connect failures.
    /// </summary>
    public sealed class RedisHostHealthTracker
    {
        private readonly IOptions<NntpSessionCoordinationOptions> _options;

        private readonly Dictionary<int, int> _attemptCounts = [];

        private readonly Dictionary<int, DateTimeOffset?> _suppressedUntil = [];

        /// <summary>
        /// Initializes a new instance of the <see cref="RedisHostHealthTracker"/> class.
        /// </summary>
        /// <param name="options">Coordination options providing reconnect delay bounds.</param>
        public RedisHostHealthTracker(IOptions<NntpSessionCoordinationOptions> options)
        {
            ArgumentNullException.ThrowIfNull(options);
            _options = options;
        }

        /// <summary>Computes the next reconnect delay for a host using exponential cap and full jitter.</summary>
        /// <param name="hostIndex">Zero-based index into <see cref="NntpSessionCoordinationOptions.Hosts"/>.</param>
        /// <returns>Delay before the next reconnect attempt.</returns>
        public TimeSpan GetReconnectDelay(int hostIndex)
        {
            NntpSessionCoordinationOptions options = _options.Value;
            int attempt = IncrementAttempt(hostIndex);
            int cap = Math.Min(
                options.PoolReconnectMaxDelayMs,
                options.PoolReconnectBaseDelayMs * (1 << Math.Min(attempt, 10)));
            int jitter = Random.Shared.Next(options.PoolReconnectBaseDelayMs, cap + 1);
            return TimeSpan.FromMilliseconds(jitter);
        }

        /// <summary>Resets failure state after a successful connection.</summary>
        /// <param name="hostIndex">Zero-based host index.</param>
        public void RecordSuccess(int hostIndex)
        {
            lock (_attemptCounts)
            {
                _attemptCounts[hostIndex] = 0;
                _suppressedUntil[hostIndex] = null;
            }
        }

        /// <summary>Records a failure and suppresses further attempts until the jittered delay elapses.</summary>
        /// <param name="hostIndex">Zero-based host index.</param>
        public void RecordFailure(int hostIndex)
        {
            TimeSpan delay = GetReconnectDelay(hostIndex);
            lock (_attemptCounts)
            {
                _suppressedUntil[hostIndex] = DateTimeOffset.UtcNow.Add(delay);
            }
        }

        /// <summary>Returns whether reconnect attempts to the host should be skipped temporarily.</summary>
        /// <param name="hostIndex">Zero-based host index.</param>
        /// <returns><see langword="true"/> when the host is inside its suppression window.</returns>
        public bool IsSuppressed(int hostIndex)
        {
            lock (_attemptCounts)
            {
                return _suppressedUntil.TryGetValue(hostIndex, out DateTimeOffset? until)
                    && until is { } deadline
                    && deadline > DateTimeOffset.UtcNow;
            }
        }

        /// <summary>Increments and returns the attempt counter for a host index.</summary>
        /// <param name="hostIndex">Zero-based host index.</param>
        /// <returns>Updated attempt count.</returns>
        private int IncrementAttempt(int hostIndex)
        {
            lock (_attemptCounts)
            {
                if (!_attemptCounts.TryGetValue(hostIndex, out int attempt))
                {
                    attempt = 0;
                }

                attempt++;
                _attemptCounts[hostIndex] = attempt;
                return attempt;
            }
        }
    }
}
