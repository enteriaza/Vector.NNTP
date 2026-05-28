// <copyright file="BoundedCache.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Session.Utilities
{
    /// <summary>
    /// Single-value TTL cache with a hard maximum TTL of 100 ms for anti-thrash reads.
    /// </summary>
    /// <typeparam name="T">Cached value type.</typeparam>
    public sealed class BoundedCache<T>
    {
        private static readonly long MaxTtlTicks = TimeSpan.FromMilliseconds(100).Ticks;

        private readonly Func<CancellationToken, Task<T>> _factory;
        private readonly long _ttlTicks;
        private long _expiresAtTicks;
        private T? _cachedValue;
        private int _hasValue;

        /// <summary>
        /// Initializes a new instance of the <see cref="BoundedCache{T}"/> class.
        /// </summary>
        /// <param name="ttl">Requested TTL; coerced to 0..100 ms.</param>
        /// <param name="factory">Factory on cache miss.</param>
        public BoundedCache(TimeSpan ttl, Func<CancellationToken, Task<T>> factory)
        {
            ArgumentNullException.ThrowIfNull(factory);
            _factory = factory;
            long ticks = ttl.Ticks;
            _ttlTicks = ticks <= 0 ? 0 : Math.Min(ticks, MaxTtlTicks);
        }

        /// <summary>
        /// Gets a fresh or cached value.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Cached or newly produced value.</returns>
        public async Task<T> GetAsync(CancellationToken cancellationToken)
        {
            if (_ttlTicks > 0 && Volatile.Read(ref _hasValue) == 1)
            {
                long now = DateTime.UtcNow.Ticks;
                if (now < Interlocked.Read(ref _expiresAtTicks))
                {
                    return _cachedValue!;
                }
            }

            T value = await _factory(cancellationToken).ConfigureAwait(false);
            if (_ttlTicks > 0)
            {
                _cachedValue = value;
                _ = Interlocked.Exchange(ref _expiresAtTicks, DateTime.UtcNow.Ticks + _ttlTicks);
                Volatile.Write(ref _hasValue, 1);
            }

            return value;
        }
    }
}
