// <copyright file="CachingMySqlUserRecordStore.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: decorates the inner record store with post-success authentication cache reads.

using Microsoft.Extensions.Logging;
using Vector.NNTP.Auth.MySql.Telemetry;

namespace Vector.NNTP.Auth.MySql.Records
{
    /// <summary>
    /// Decorates <see cref="MySqlUserRecordStore"/> with read-through cache hits for previously successful authentications.
    /// </summary>
    /// <remarks>
    /// <para><b>Population:</b> Cache entries are written by <see cref="Credentials.MySqlNntpCredentialValidator"/> after
    /// successful password or SASL validation — not on failed lookups.</para>
    /// <para><b>Read-through scope:</b> Only username-only entries (post-SASL success) are consulted here to warm SASL
    /// credential-store lookups. Password-fingerprint cache hits are evaluated inside
    /// <see cref="Credentials.MySqlNntpCredentialValidator"/> directly.</para>
    /// </remarks>
    internal sealed partial class CachingMySqlUserRecordStore : INntpUserRecordStore
    {
        /// <summary>
        /// Inner store that performs MySQL I/O on cache miss.
        /// </summary>
        private readonly INntpUserRecordStore _inner;

        /// <summary>
        /// Metrics for cache hit tracking.
        /// </summary>
        private readonly AuthMySqlMetrics _metrics;

        /// <summary>
        /// Logger for cache diagnostics.
        /// </summary>
        private readonly ILogger<CachingMySqlUserRecordStore> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="CachingMySqlUserRecordStore"/> class.
        /// </summary>
        /// <param name="inner">Inner MySQL record store.</param>
        /// <param name="cache">Successful-authentication cache.</param>
        /// <param name="metrics">Metrics instance.</param>
        /// <param name="logger">Logger for authentication cache diagnostics.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="inner"/>, <paramref name="cache"/>,
        /// <paramref name="metrics"/>, or <paramref name="logger"/> is null.</exception>
        internal CachingMySqlUserRecordStore(
            MySqlUserRecordStore inner,
            MySqlUserRecordCache cache,
            AuthMySqlMetrics metrics,
            ILogger<CachingMySqlUserRecordStore> logger)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            Cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Gets the successful-authentication cache for population after validation.
        /// </summary>
        internal MySqlUserRecordCache Cache { get; }

        /// <summary>
        /// Tries to get a user record by account name, consulting the username-only authentication cache first.
        /// </summary>
        /// <param name="accountName">Account name to lookup.</param>
        /// <returns>User record or <see langword="null"/> when not found in cache or MySQL.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="accountName"/> is null or empty.</exception>
        /// <remarks>
        /// Only <see cref="MySqlUserRecordCache.UsernameOnlyFingerprint"/> entries are read here; password-fingerprint cache
        /// hits are handled inside <see cref="Credentials.MySqlNntpCredentialValidator"/>.
        /// </remarks>
        MySqlUserRecord? INntpUserRecordStore.TryGetUser(string accountName)
        {
            return TryGetCached(accountName, out MySqlUserRecord? cached) ? cached : _inner.TryGetUser(accountName);
        }

        /// <summary>
        /// Tries to get a user record by account name asynchronously, consulting the username-only cache first.
        /// </summary>
        /// <param name="accountName">Account name to lookup.</param>
        /// <param name="cancellationToken">Cancellation token for the inner store on cache miss.</param>
        /// <returns>User record or <see langword="null"/> when not found in cache or MySQL.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="accountName"/> is null or empty.</exception>
        /// <remarks>
        /// Backend I/O exceptions from the inner store propagate after logging at the implementation boundary.
        /// </remarks>
        async Task<MySqlUserRecord?> INntpUserRecordStore.TryGetUserAsync(string accountName, CancellationToken cancellationToken)
        {
            return TryGetCached(accountName, out MySqlUserRecord? cached)
                ? cached
                : await _inner.TryGetUserAsync(accountName, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Attempts a username-only cache hit for SASL credential-store warm paths.
        /// </summary>
        /// <param name="accountName">Account name.</param>
        /// <param name="record">Cached record when found.</param>
        /// <returns><see langword="true"/> on cache hit.</returns>
        private bool TryGetCached(string accountName, out MySqlUserRecord? record)
        {
            if (Cache.TryGet(accountName, MySqlUserRecordCache.UsernameOnlyFingerprint, out record))
            {
                AuthCacheHit(_logger, accountName);
                _metrics.RecordLookup("cache_hit");
                return true;
            }

            AuthCacheMiss(_logger, accountName);
            return false;
        }
    }
}
