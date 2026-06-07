// <copyright file="PostFilterAccessTracker.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: diagnostics/formatting/startup validation; readability over micro-optimization; allocations acceptable.
// PostFilterAccessTracker.cs -- In-process sliding-window post counters per rate-limit key.

namespace Vector.NNTP.Filters.PostFilter
{
    /// <summary>
    /// In-process sliding-window post counters per rate-limit key (simplified Perl <c>access.conf</c>).
    /// </summary>
    /// <remarks>
    /// <para><b>Thread safety:</b> Synchronizes access internally. This is designed for low to moderate POST rates.</para>
    /// <para>
    /// <b>Memory:</b> Retains timestamp lists per identity key until keys go idle; there is no global eviction of unused keys.
    /// Each key prunes entries older than the active window on every post.
    /// </para>
    /// </remarks>
    public sealed class PostFilterAccessTracker
    {
        /// <summary>Lock protecting mutations to <see cref="_postsByKey"/>.</summary>
        private readonly object _sync = new();

        /// <summary>
        /// Sliding-window post timestamps keyed by identity (IP string or username), compared ordinal for stable dictionary behavior.
        /// </summary>
        private readonly Dictionary<string, List<long>> _postsByKey = new(StringComparer.Ordinal);

        /// <summary>
        /// Records a post for <paramref name="key"/> and returns <see langword="false"/> when the sliding window is exceeded.
        /// </summary>
        /// <param name="key">Identity key (IP string or username).</param>
        /// <param name="windowSeconds">Window length in seconds.</param>
        /// <param name="maxPosts">Maximum posts in the window (0 = unlimited).</param>
        /// <param name="utcNowSeconds">Current UTC unix seconds.</param>
        /// <returns><see langword="true"/> when under the limit after recording.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is <see langword="null"/>.</exception>
        public bool TryRecordPost(string key, int windowSeconds, int maxPosts, long utcNowSeconds)
        {
            ArgumentNullException.ThrowIfNull(key);

            if (maxPosts <= 0 || windowSeconds <= 0)
            {
                return true;
            }

            lock (_sync)
            {
                long cutoff = utcNowSeconds - windowSeconds;
                if (!_postsByKey.TryGetValue(key, out List<long>? stamps))
                {
                    stamps = new List<long>(capacity: 8);
                    _postsByKey[key] = stamps;
                }
                else
                {
                    _ = stamps.RemoveAll(t => t < cutoff);
                }

                if (stamps.Count >= maxPosts)
                {
                    return false;
                }

                stamps.Add(utcNowSeconds);
                return true;
            }
        }
    }
}

