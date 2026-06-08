// <copyright file="CachingMySqlUserRecordStore.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: decorates the inner record store with post-success authentication cache reads.

using Microsoft.Extensions.Logging;
using Vector.NNTP.Auth.MySql.Telemetry;

namespace Vector.NNTP.Auth.MySql.Records
{
    /// <summary>
    /// Read-through decorator over <see cref="MySqlUserRecordStore"/> that serves username-only burst-cache hits before
    /// opening MySQL connections.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Role:</b> Production implementation of <see cref="INntpUserRecordStore"/> registered by
    /// <see cref="DependencyInjection.ServiceCollectionExtensions.AddNntpMySqlAuth"/>. Wraps the inner
    /// <see cref="MySqlUserRecordStore"/> singleton and shares the same <see cref="MySqlUserRecordCache"/> instance that
    /// <see cref="Credentials.MySqlNntpCredentialValidator"/> populates after successful authentication.
    /// </para>
    /// <para><b>Consumers:</b></para>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <see cref="Credentials.MySqlScramCredentialStore"/> and <see cref="Credentials.MySqlCramMd5CredentialStore"/> —
    /// synchronous <see cref="INntpUserRecordStore.TryGetUser"/> for SASL secret retrieval.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <see cref="Credentials.MySqlNntpCredentialValidator"/> — <see cref="INntpUserRecordStore.TryGetUserAsync"/> when
    /// <see cref="MySqlUserRecordSaslCache"/> has no staged record during SASL account completion.
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// <b>Population:</b> Cache entries are written only by the validator after successful password or SASL validation.
    /// Failed lookups, disabled accounts, and policy denials never populate the burst cache.
    /// </para>
    /// <para>
    /// <b>Read-through scope:</b> Only entries keyed with <see cref="MySqlUserRecordCache.UsernameOnlyFingerprint"/>
    /// (post-SASL success) are consulted in <c>TryGetCached</c>. Password-fingerprint hits for AUTHINFO PASS are evaluated
    /// inside <see cref="Credentials.MySqlNntpCredentialValidator"/> against the same underlying
    /// <see cref="MySqlUserRecordCache"/> without passing through this decorator's lookup methods.
    /// </para>
    /// <para>
    /// <b>Observability:</b> Cache hits log EventId <c>420</c> and record <c>cache_hit</c> metrics from
    /// <c>CachingMySqlUserRecordStore.Logging.cs</c>; misses log EventId <c>421</c> before the inner store may emit
    /// EventIds <c>400</c>–<c>403</c>.
    /// </para>
    /// <para><b>Thread safety:</b> Singleton safe for concurrent NNTP session handlers; delegates to thread-safe inner store
    /// and <see cref="MySqlUserRecordCache"/>.</para>
    /// </remarks>
    internal sealed partial class CachingMySqlUserRecordStore : INntpUserRecordStore
    {
        /// <summary>
        /// Inner store that executes parameterised <c>nntpusers</c> queries on cache miss.
        /// </summary>
        /// <remarks>
        /// Typed as <see cref="INntpUserRecordStore"/> but constructed with the concrete
        /// <see cref="MySqlUserRecordStore"/> singleton from DI. Never null after construction.
        /// </remarks>
        private readonly INntpUserRecordStore _inner;

        /// <summary>
        /// Auth MySQL metrics used to record <c>cache_hit</c> outcomes from <c>TryGetCached</c>.
        /// </summary>
        /// <remarks>Shared singleton from DI; miss paths do not record metrics at this layer.</remarks>
        private readonly AuthMySqlMetrics _metrics;

        /// <summary>
        /// Category logger for burst-cache hit and miss diagnostics (EventIds <c>420</c>–<c>421</c>).
        /// </summary>
        /// <remarks>Passed to source-generated helpers in the logging partial.</remarks>
        private readonly ILogger<CachingMySqlUserRecordStore> _logger;

        /// <summary>
        /// Creates a read-through decorator around the inner MySQL record store and burst cache.
        /// </summary>
        /// <param name="inner">
        /// Concrete <see cref="MySqlUserRecordStore"/> that performs database I/O on cache miss. Must not be
        /// <see langword="null"/>.
        /// </param>
        /// <param name="cache">
        /// Shared successful-authentication cache also injected into <see cref="Credentials.MySqlNntpCredentialValidator"/>.
        /// Must not be <see langword="null"/>.
        /// </param>
        /// <param name="metrics">
        /// Metrics recorder for <c>cache_hit</c> lookup outcomes. Must not be <see langword="null"/>.
        /// </param>
        /// <param name="logger">
        /// Logger for <see cref="CachingMySqlUserRecordStore"/> cache diagnostics. Must not be <see langword="null"/>.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="inner"/>, <paramref name="cache"/>, <paramref name="metrics"/>, or
        /// <paramref name="logger"/> is <see langword="null"/>.
        /// </exception>
        /// <remarks>
        /// Registered as the <see cref="INntpUserRecordStore"/> singleton in
        /// <see cref="DependencyInjection.ServiceCollectionExtensions.AddNntpMySqlAuth"/>.
        /// </remarks>
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
        /// Gets the shared burst cache consulted by <c>TryGetCached</c> and populated by the credential validator.
        /// </summary>
        /// <value>
        /// The same <see cref="MySqlUserRecordCache"/> singleton registered in DI for post-success authentication
        /// deduplication.
        /// </value>
        /// <remarks>
        /// Exposed for assembly-internal tests and composition clarity. Production writes occur through
        /// <see cref="Credentials.MySqlNntpCredentialValidator"/> rather than through this decorator.
        /// </remarks>
        internal MySqlUserRecordCache Cache { get; }

        /// <summary>
        /// Synchronous <see cref="INntpUserRecordStore"/> lookup with username-only burst-cache read-through.
        /// </summary>
        /// <param name="accountName">
        /// Plaintext NNTP account name. Validated by the inner store on cache miss; must not be <see langword="null"/> or
        /// empty per the interface contract.
        /// </param>
        /// <returns>
        /// A <see cref="MySqlUserRecord"/> from the burst cache or inner store when found; otherwise
        /// <see langword="null"/>.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="accountName"/> is <see langword="null"/> or empty and the inner store is invoked.
        /// </exception>
        /// <remarks>
        /// <para>
        /// Calls <c>TryGetCached</c> first. On hit, returns the decrypted cache payload without MySQL I/O. On miss, delegates
        /// to the inner <see cref="MySqlUserRecordStore"/> synchronous lookup path.
        /// </para>
        /// <para>
        /// Only <see cref="MySqlUserRecordCache.UsernameOnlyFingerprint"/> entries participate. Password-fingerprint cache
        /// hits are not visible through this method.
        /// </para>
        /// </remarks>
        MySqlUserRecord? INntpUserRecordStore.TryGetUser(string accountName)
        {
            return TryGetCached(accountName, out MySqlUserRecord? cached) ? cached : _inner.TryGetUser(accountName);
        }

        /// <summary>
        /// Asynchronous <see cref="INntpUserRecordStore"/> lookup with username-only burst-cache read-through.
        /// </summary>
        /// <param name="accountName">
        /// Plaintext NNTP account name. Validated by the inner store on cache miss; must not be <see langword="null"/> or
        /// empty per the interface contract.
        /// </param>
        /// <param name="cancellationToken">
        /// Cancellation token forwarded to the inner store on cache miss. Ignored when a cache hit returns synchronously.
        /// </param>
        /// <returns>
        /// A task producing a <see cref="MySqlUserRecord"/> from the burst cache or inner store when found; otherwise
        /// <see langword="null"/>.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="accountName"/> is <see langword="null"/> or empty and the inner store is invoked.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        /// Thrown when <paramref name="cancellationToken"/> is signalled during inner-store async I/O on cache miss.
        /// </exception>
        /// <remarks>
        /// <para>
        /// Calls <c>TryGetCached</c> first. On hit, returns a completed result without awaiting MySQL. On miss, awaits
        /// the inner <see cref="MySqlUserRecordStore"/> asynchronous lookup path with <c>ConfigureAwait(false)</c>.
        /// </para>
        /// <para>Backend faults from the inner store propagate after that store's logging and metrics.</para>
        /// </remarks>
        async Task<MySqlUserRecord?> INntpUserRecordStore.TryGetUserAsync(string accountName, CancellationToken cancellationToken)
        {
            return TryGetCached(accountName, out MySqlUserRecord? cached)
                ? cached
                : await _inner.TryGetUserAsync(accountName, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Attempts to satisfy a lookup from a username-only burst-cache entry.
        /// </summary>
        /// <param name="accountName">Account name passed to <see cref="MySqlUserRecordCache.TryGet"/>.</param>
        /// <param name="record">
        /// When this method returns <see langword="true"/>, the decrypted <see cref="MySqlUserRecord"/> from the cache.
        /// When this method returns <see langword="false"/>, <see langword="null"/>.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when <see cref="MySqlUserRecordCache.TryGet"/> succeeds for
        /// <see cref="MySqlUserRecordCache.UsernameOnlyFingerprint"/>; otherwise <see langword="false"/>.
        /// </returns>
        /// <remarks>
        /// <para>
        /// On hit, logs <see cref="AuthCacheHit"/> (EventId <c>420</c>) and records
        /// <see cref="AuthMySqlMetrics.RecordLookup"/> with outcome <c>cache_hit</c>.
        /// </para>
        /// <para>
        /// On miss, logs <see cref="AuthCacheMiss"/> (EventId <c>421</c>) only. Does not write to the cache, mutate TTL,
        /// or record metrics. A miss does not imply the account is absent from MySQL.
        /// </para>
        /// </remarks>
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
