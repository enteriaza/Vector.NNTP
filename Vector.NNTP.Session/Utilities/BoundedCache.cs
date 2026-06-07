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
        /// <summary>
        /// Hard anti-thrash ceiling of 100 ms expressed in <see cref="DateTime"/> ticks.
        /// </summary>
        private static readonly long MaxTtlTicks = TimeSpan.FromMilliseconds(100).Ticks;

        /// <summary>
        /// Async factory invoked when the cache is empty or expired.
        /// </summary>
        private readonly Func<CancellationToken, Task<T>> _factory;

        /// <summary>
        /// Effective TTL in ticks after coercing the requested value to 0–100 ms.
        /// </summary>
        private readonly long _ttlTicks;

        /// <summary>
        /// Absolute expiry instant in UTC ticks for the cached entry.
        /// </summary>
        private long _expiresAtTicks;

        /// <summary>
        /// Non-zero when <see cref="_cachedValue"/> is valid until <see cref="_expiresAtTicks"/>.
        /// </summary>
        private int _hasValue;

        /// <summary>
        /// Last produced value retained until expiry.
        /// </summary>
        private T? _cachedValue;

        /// <summary>
        /// Initializes a new instance of the <see cref="BoundedCache{T}"/> class with TTL coerced to the 0–100 ms anti-thrash window.
        /// </summary>
        /// <param name="ttl">Requested TTL; coerced to 0..100 ms.</param>
        /// <param name="factory">Factory invoked on cache miss.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="factory"/> is null.</exception>
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
