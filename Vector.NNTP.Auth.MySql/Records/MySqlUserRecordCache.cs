// <copyright file="MySqlUserRecordCache.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: bounded in-memory cache of successfully authenticated user records.

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Vector.NNTP.Auth.MySql.Records
{
    /// <summary>
    /// Thread-safe time-bounded cache of user records populated only after successful authentication.
    /// </summary>
    /// <remarks>
    /// <para><b>Purpose:</b> Deduplicate MySQL lookups when many NNRPD sessions authenticate with identical credentials
    /// in a short window.</para>
    /// <para><b>Security:</b> Stores materialised <see cref="MySqlUserRecord"/> snapshots only after successful validation.
    /// Never caches failures. Passwords and SCRAM key material are AES-256-GCM encrypted at rest inside the cache.</para>
    /// <para><b>Expiry:</b> Entries expire solely by elapsed time (default ten seconds). There is no count-based eviction.</para>
    /// </remarks>
    internal sealed class MySqlUserRecordCache
    {
        /// <summary>
        /// Sentinel fingerprint for username-only cache entries after successful SASL completion.
        /// </summary>
        internal static readonly byte[] UsernameOnlyFingerprint = "username-only"u8.ToArray();

        /// <summary>
        /// AES-256-GCM protected user-record payload with an absolute UTC expiry instant.
        /// </summary>
        /// <param name="ProtectedPayload">Encrypted cache bytes produced by <see cref="MySqlUserRecordCacheProtection"/>.</param>
        /// <param name="ExpiresUtc">UTC instant after which the entry must not be returned.</param>
        private readonly record struct CacheEntry(byte[] ProtectedPayload, DateTimeOffset ExpiresUtc);

        /// <summary>
        /// Backing concurrent dictionary keyed by cache hash.
        /// </summary>
        private readonly ConcurrentDictionary<string, CacheEntry> _entries = new();

        /// <summary>
        /// Entry time-to-live after which cached credentials are no longer returned.
        /// </summary>
        private readonly TimeSpan _ttl;

        /// <summary>
        /// Protects cached record payloads at rest in memory.
        /// </summary>
        private readonly MySqlUserRecordCacheProtection _protection;

        /// <summary>
        /// Initializes a new instance of the <see cref="MySqlUserRecordCache"/> class.
        /// </summary>
        /// <param name="ttl">Time-to-live for cache entries.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="ttl"/> is not positive.</exception>
        internal MySqlUserRecordCache(TimeSpan ttl)
        {
            if (ttl <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(ttl), ttl, "TTL must be positive.");
            }

            _ttl = ttl;
            _protection = new MySqlUserRecordCacheProtection();
        }

        /// <summary>
        /// Attempts to retrieve a cached record for the given account and credential fingerprint.
        /// </summary>
        /// <param name="accountName">Normalized account name.</param>
        /// <param name="credentialFingerprint">Fingerprint of supplied credentials, or <see cref="UsernameOnlyFingerprint"/>.</param>
        /// <param name="record">Cached record when found and not expired.</param>
        /// <returns><see langword="true"/> when a valid cache entry was found.</returns>
        /// <remarks>
        /// Expired, undecryptable, or tampered entries are removed eagerly and reported as misses.
        /// </remarks>
        internal bool TryGet(string accountName, ReadOnlySpan<byte> credentialFingerprint, out MySqlUserRecord? record)
        {
            string key = BuildCacheKey(accountName, credentialFingerprint);
            if (!_entries.TryGetValue(key, out CacheEntry entry))
            {
                record = null;
                return false;
            }

            if (entry.ExpiresUtc <= DateTimeOffset.UtcNow)
            {
                _ = _entries.TryRemove(key, out _);
                record = null;
                return false;
            }

            MySqlUserRecord? decrypted = _protection.Unprotect(entry.ProtectedPayload);
            if (decrypted is null)
            {
                _ = _entries.TryRemove(key, out _);
                record = null;
                return false;
            }

            record = decrypted;
            return true;
        }

        /// <summary>
        /// Stores a successful authentication record in the cache.
        /// </summary>
        /// <param name="accountName">Normalized account name.</param>
        /// <param name="credentialFingerprint">Fingerprint of supplied credentials, or <see cref="UsernameOnlyFingerprint"/>.</param>
        /// <param name="record">Validated user record snapshot.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="record"/> is null.</exception>
        /// <remarks>
        /// Overwrites any prior entry for the same account and fingerprint hash. Payloads are encrypted before insertion.
        /// </remarks>
        internal void Put(string accountName, ReadOnlySpan<byte> credentialFingerprint, MySqlUserRecord record)
        {
            ArgumentNullException.ThrowIfNull(record);
            string key = BuildCacheKey(accountName, credentialFingerprint);
            DateTimeOffset expires = DateTimeOffset.UtcNow.Add(_ttl);
            byte[] protectedPayload = _protection.Protect(record);
            _entries[key] = new CacheEntry(protectedPayload, expires);
        }

        /// <summary>
        /// Computes a SHA-256 fingerprint for a supplied password string.
        /// </summary>
        /// <param name="password">Supplied password (US-ASCII).</param>
        /// <returns>SHA-256 digest of the ASCII password bytes.</returns>
        /// <remarks>
        /// Null passwords hash as empty strings. Non-ASCII code points follow <see cref="Encoding.ASCII"/> replacement rules.
        /// </remarks>
        internal static byte[] ComputePasswordFingerprint(string password)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(password ?? string.Empty);
            return SHA256.HashData(bytes);
        }

        /// <summary>
        /// Builds a stable cache key from account name and credential fingerprint.
        /// </summary>
        /// <param name="accountName">Account name.</param>
        /// <param name="credentialFingerprint">Credential fingerprint bytes.</param>
        /// <returns>Hex-encoded cache key.</returns>
        private static string BuildCacheKey(string accountName, ReadOnlySpan<byte> credentialFingerprint)
        {
            byte[] accountBytes = Encoding.UTF8.GetBytes(accountName);
            byte[] combined = new byte[accountBytes.Length + 1 + credentialFingerprint.Length];
            accountBytes.CopyTo(combined.AsSpan());
            combined[accountBytes.Length] = (byte)'|';
            credentialFingerprint.CopyTo(combined.AsSpan(accountBytes.Length + 1));
            return Convert.ToHexString(SHA256.HashData(combined));
        }

    }
}
