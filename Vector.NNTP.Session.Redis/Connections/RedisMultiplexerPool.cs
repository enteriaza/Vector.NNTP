// <copyright file="RedisMultiplexerPool.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
using StackExchange.Redis;
using Vector.NNTP.Session.Redis.Exceptions;

namespace Vector.NNTP.Session.Redis.Connections
{
    /// <summary>
    /// Manages long-lived <see cref="IConnectionMultiplexer"/> instances for session coordination.
    /// </summary>
    public sealed partial class RedisMultiplexerPool : IAsyncDisposable
    {
        private readonly RedisMultiplexerFactory _factory;
        private readonly IOptions<NntpSessionCoordinationOptions> _options;
        private readonly RedisHostHealthTracker _hostHealth;
        private readonly ILogger<RedisMultiplexerPool> _logger;
        private readonly object _snapshotLock = new();
        private readonly Channel<bool> _scaleUpSignal = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        private PooledMultiplexer[] _snapshot = [];
        private int _roundRobinIndex;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="RedisMultiplexerPool"/> class.
        /// </summary>
        /// <param name="factory">Multiplexer factory.</param>
        /// <param name="options">Coordination options.</param>
        /// <param name="hostHealth">Per-host backoff tracker.</param>
        /// <param name="logger">Logger.</param>
        public RedisMultiplexerPool(
            RedisMultiplexerFactory factory,
            IOptions<NntpSessionCoordinationOptions> options,
            RedisHostHealthTracker hostHealth,
            ILogger<RedisMultiplexerPool> logger)
        {
            ArgumentNullException.ThrowIfNull(factory);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(hostHealth);
            ArgumentNullException.ThrowIfNull(logger);
            _factory = factory;
            _options = options;
            _hostHealth = hostHealth;
            _logger = logger;
        }

        /// <summary>Gets current copy-on-write snapshot of pooled multiplexers.</summary>
        public IReadOnlyList<PooledMultiplexer> Snapshot => Volatile.Read(ref _snapshot);

        /// <summary>Gets reader for coalesced scale-up signals.</summary>
        internal ChannelReader<bool> ScaleUpReader => _scaleUpSignal.Reader;

        /// <summary>
        /// Opens <see cref="NntpSessionCoordinationOptions.MinConnections"/> multiplexers.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task representing the asynchronous start operation.</returns>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            NntpSessionCoordinationOptions options = _options.Value;
            for (int i = 0; i < options.MinConnections; i++)
            {
                _ = await AddMultiplexerAsync(cancellationToken).ConfigureAwait(false);
            }

            if (Snapshot.Count < options.MinConnections)
            {
                throw new RedisUnavailableException(
                    $"Redis pool failed to establish minimum connections ({options.MinConnections}).");
            }
        }

        /// <summary>
        /// Returns the next connected multiplexer using round-robin selection.
        /// </summary>
        /// <returns>Live multiplexer.</returns>
        /// <exception cref="RedisUnavailableException">Thrown when no connected multiplexers exist.</exception>
        public IConnectionMultiplexer GetMultiplexer()
        {
            PooledMultiplexer[] current = Volatile.Read(ref _snapshot);
            if (current.Length == 0)
            {
                throw new RedisUnavailableException();
            }

            int start = Interlocked.Increment(ref _roundRobinIndex);
            for (int offset = 0; offset < current.Length; offset++)
            {
                PooledMultiplexer entry = current[(start + offset) % current.Length];
                if (entry.State == PooledMultiplexerState.Connected && entry.Multiplexer.IsConnected)
                {
                    return entry.Multiplexer;
                }
            }

            throw new RedisUnavailableException("No connected Redis multiplexers are available.");
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            PooledMultiplexer[] current = Volatile.Read(ref _snapshot);
            Volatile.Write(ref _snapshot, []);
            for (int i = 0; i < current.Length; i++)
            {
                await current[i].DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>Coalesces a scale-up request for <see cref="RedisMultiplexerBackgroundScaler"/>.</summary>
        internal void SignalScaleUp()
        {
            _ = _scaleUpSignal.Writer.TryWrite(true);
        }

        /// <summary>
        /// Creates and appends a new pooled multiplexer.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The new pool entry.</returns>
        internal async Task<PooledMultiplexer> AddMultiplexerAsync(CancellationToken cancellationToken)
        {
            NntpSessionCoordinationOptions options = _options.Value;
            Exception? lastException = null;
            for (int attempt = 0; attempt < options.Hosts.Length; attempt++)
            {
                int hostIndex = Random.Shared.Next(options.Hosts.Length);
                if (_hostHealth.IsSuppressed(hostIndex))
                {
                    continue;
                }

                try
                {
                    IConnectionMultiplexer multiplexer = await _factory
                        .ConnectAsync(options, cancellationToken)
                        .ConfigureAwait(false);
                    _hostHealth.RecordSuccess(hostIndex);
                    PooledMultiplexer pooled = new(Guid.NewGuid(), multiplexer);
                    AppendToSnapshot(pooled);
                    LogMultiplexerAdded(_logger, pooled.ConnectionId, Snapshot.Count);
                    return pooled;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _hostHealth.RecordFailure(hostIndex);
                    lastException = ex;
                    LogMultiplexerConnectFailed(_logger, hostIndex, ex);
                }
            }

            throw new RedisConnectionException(
                ConnectionFailureType.UnableToConnect,
                "Unable to connect a Redis multiplexer to any configured host.",
                lastException);
        }

        /// <summary>
        /// Marks a multiplexer faulted and removes it from the active snapshot.
        /// </summary>
        /// <param name="connectionId">Pool entry identifier.</param>
        internal void MarkFaulted(Guid connectionId)
        {
            PooledMultiplexer[] current = Volatile.Read(ref _snapshot);
            for (int i = 0; i < current.Length; i++)
            {
                if (current[i].ConnectionId != connectionId)
                {
                    continue;
                }

                current[i].Transition(PooledMultiplexerState.Faulted);
                RemoveFromSnapshot(connectionId);
                return;
            }
        }

        /// <summary>Appends a connected multiplexer to the copy-on-write snapshot.</summary>
        /// <param name="pooled">Entry to add.</param>
        private void AppendToSnapshot(PooledMultiplexer pooled)
        {
            lock (_snapshotLock)
            {
                PooledMultiplexer[] current = _snapshot;
                PooledMultiplexer[] next = new PooledMultiplexer[current.Length + 1];
                current.CopyTo(next, 0);
                next[current.Length] = pooled;
                Volatile.Write(ref _snapshot, next);
            }
        }

        /// <summary>Removes a faulted multiplexer from the snapshot.</summary>
        /// <param name="connectionId">Pool entry identifier.</param>
        private void RemoveFromSnapshot(Guid connectionId)
        {
            lock (_snapshotLock)
            {
                PooledMultiplexer[] current = _snapshot;
                int index = -1;
                for (int i = 0; i < current.Length; i++)
                {
                    if (current[i].ConnectionId == connectionId)
                    {
                        index = i;
                        break;
                    }
                }

                if (index < 0)
                {
                    return;
                }

                PooledMultiplexer[] next = new PooledMultiplexer[current.Length - 1];
                if (index > 0)
                {
                    Array.Copy(current, 0, next, 0, index);
                }

                if (index < current.Length - 1)
                {
                    Array.Copy(current, index + 1, next, index, current.Length - index - 1);
                }

                Volatile.Write(ref _snapshot, next);
            }
        }
    }
}
