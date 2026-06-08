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
    /// Thread-safe, time-bounded in-memory cache of <see cref="MySqlUserRecord"/> snapshots populated only after successful
    /// authentication.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Purpose:</b> Deduplicate MySQL lookups when many NNRPD sessions authenticate with identical credentials in a
    /// short window (for example concurrent clients repeating AUTHINFO PASS or SASL completion for the same account).
    /// </para>
    /// <para><b>Producers and consumers:</b></para>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <see cref="Credentials.MySqlNntpCredentialValidator"/> — <see cref="TryGet"/> before database lookup;
    /// <see cref="Put"/> after successful password or SASL finalization via <c>CacheSuccessfulAuth</c>.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <see cref="CachingMySqlUserRecordStore"/> — read-through <see cref="TryGet"/> with
    /// <see cref="UsernameOnlyFingerprint"/> only (SASL credential-store warm path).
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// <b>Security:</b> Only successful validations are cached; failed lookups and rejections are never stored. At-rest
    /// payloads are AES-256-GCM protected by a per-cache <see cref="MySqlUserRecordCacheProtection"/> instance (see that
    /// type for wire format). Distinct from per-exchange <see cref="MySqlUserRecordSaslCache"/> staging.
    /// </para>
    /// <para>
    /// <b>Keys:</b> Entries are keyed by SHA-256 over UTF-8 account name, a <c>|</c> separator, and the credential
    /// fingerprint bytes (<see cref="BuildCacheKey"/>). Password paths use <see cref="ComputePasswordFingerprint"/>;
    /// post-SASL success uses the fixed <see cref="UsernameOnlyFingerprint"/> sentinel so username-only warm reads do not
    /// require the password hash.
    /// </para>
    /// <para>
    /// <b>Expiry:</b> Entries expire by absolute UTC instant (<c>UtcNow + TTL</c> at <see cref="Put"/>). Default TTL is
    /// ten seconds via <see cref="Configuration.MySqlAuthOptions.AuthCacheTtl"/> when constructed from DI. There is no
    /// count-based eviction; stale entries are removed lazily on <see cref="TryGet"/> when expired or undecryptable.
    /// </para>
    /// <para>
    /// <b>Registration:</b> Singleton from <see cref="DependencyInjection.ServiceCollectionExtensions.AddNntpMySqlAuth"/>.
    /// </para>
    /// <para><b>Thread safety:</b> <see cref="ConcurrentDictionary{TKey, TValue}"/> backs storage; <see cref="TryGet"/> and
    /// <see cref="Put"/> are safe under concurrent session handlers.</para>
    /// </remarks>
    internal sealed class MySqlUserRecordCache
    {
        /// <summary>
        /// Sentinel fingerprint bytes for username-only cache entries after successful SASL completion.
        /// </summary>
        /// <value>UTF-8 encoding of the literal <c>username-only</c>.</value>
        /// <remarks>
        /// <para>
        /// Used as the credential fingerprint argument to <see cref="Put"/> and <see cref="TryGet"/> when the
        /// validator caches a record without binding to a specific password digest (SASL finalize path).
        /// </para>
        /// <para>
        /// <see cref="CachingMySqlUserRecordStore"/> consults only this fingerprint; password-fingerprint hits are evaluated
        /// inside <see cref="Credentials.MySqlNntpCredentialValidator"/> directly.
        /// </para>
        /// </remarks>
        internal static readonly byte[] UsernameOnlyFingerprint = "username-only"u8.ToArray();

        /// <summary>
        /// Immutable stored value pairing encrypted record bytes with an absolute expiry instant.
        /// </summary>
        /// <param name="ProtectedPayload">
        /// AES-256-GCM protected blob from <see cref="MySqlUserRecordCacheProtection.Protect"/>; never stored in cleartext.
        /// </param>
        /// <param name="ExpiresUtc">
        /// UTC instant after which <see cref="TryGet"/> must treat the entry as a miss and remove it.
        /// </param>
        /// <remarks>Held inside <see cref="_entries"/> keyed by <see cref="BuildCacheKey"/> output.</remarks>
        private readonly record struct CacheEntry(byte[] ProtectedPayload, DateTimeOffset ExpiresUtc);

        /// <summary>
        /// Concurrent map from hex cache keys to protected entries.
        /// </summary>
        /// <remarks>
        /// Grows with distinct account/fingerprint pairs until entries expire on read. No maximum entry count is enforced.
        /// </remarks>
        private readonly ConcurrentDictionary<string, CacheEntry> _entries = new();

        /// <summary>
        /// Duration added to <see cref="DateTimeOffset.UtcNow"/> when inserting entries in <see cref="Put"/>.
        /// </summary>
        /// <remarks>Captured at construction; not mutable after the cache is created.</remarks>
        private readonly TimeSpan _ttl;

        /// <summary>
        /// Encryptor/decryptor dedicated to this cache instance.
        /// </summary>
        /// <remarks>
        /// Constructed alongside the cache so all payloads in <see cref="_entries"/> share one in-process AES key.
        /// </remarks>
        private readonly MySqlUserRecordCacheProtection _protection;

        /// <summary>
        /// Initializes a new cache with the supplied entry time-to-live and a fresh protector key.
        /// </summary>
        /// <param name="ttl">
        /// Positive duration for each inserted entry. Typically <see cref="Configuration.MySqlAuthOptions.AuthCacheTtl"/>
        /// from DI.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="ttl"/> is zero or negative.
        /// </exception>
        /// <remarks>Creates a new <see cref="MySqlUserRecordCacheProtection"/> instance bound to this cache.</remarks>
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
        /// Attempts to retrieve a decrypted user record for an account and credential fingerprint.
        /// </summary>
        /// <param name="accountName">
        /// NNTP account name (same string used at <see cref="Put"/>). Callers must not pass <see langword="null"/>.
        /// </param>
        /// <param name="credentialFingerprint">
        /// Credential digest from <see cref="ComputePasswordFingerprint"/> or <see cref="UsernameOnlyFingerprint"/> for SASL
        /// username-only entries.
        /// </param>
        /// <param name="record">
        /// When this method returns <see langword="true"/>, the decrypted <see cref="MySqlUserRecord"/>. When this method
        /// returns <see langword="false"/>, <see langword="null"/>.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when a non-expired entry exists, decrypts successfully, and deserializes; otherwise
        /// <see langword="false"/>.
        /// </returns>
        /// <remarks>
        /// <para><b>Miss paths (returns <see langword="false"/>):</b></para>
        /// <list type="bullet">
        /// <item><description>Unknown cache key.</description></item>
        /// <item><description><see cref="CacheEntry.ExpiresUtc"/> is in the past (entry removed).</description></item>
        /// <item>
        /// <description>
        /// <see cref="MySqlUserRecordCacheProtection.Unprotect"/> returns <see langword="null"/> (tamper/wrong key/corrupt
        /// payload; entry removed).
        /// </description>
        /// </item>
        /// </list>
        /// <para>Never throws for normal lookup inputs; does not extend TTL on hit.</para>
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
        /// Encrypts and stores a user record after successful authentication.
        /// </summary>
        /// <param name="accountName">Authenticated account name. Callers must not pass <see langword="null"/>.</param>
        /// <param name="credentialFingerprint">
        /// Password SHA-256 from <see cref="ComputePasswordFingerprint"/> or <see cref="UsernameOnlyFingerprint"/> after
        /// SASL success.
        /// </param>
        /// <param name="record">Validated snapshot to cache. Must not be <see langword="null"/>.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="record"/> is <see langword="null"/>.
        /// </exception>
        /// <remarks>
        /// <para>
        /// Overwrites any prior entry for the same <see cref="BuildCacheKey"/> hash. Expiry is set to
        /// <c>DateTimeOffset.UtcNow + <see cref="_ttl"/></c> at insert time.
        /// </para>
        /// <para>Invoked only from successful validator paths; never called for invalid credentials.</para>
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
        /// Computes the SHA-256 credential fingerprint used for password-bound cache keys.
        /// </summary>
        /// <param name="password">Supplied password from the client. May be <see langword="null"/> (treated as empty).</param>
        /// <returns>
        /// 32-byte SHA-256 digest of the password encoded with <see cref="Encoding.ASCII"/> (not UTF-8).
        /// </returns>
        /// <remarks>
        /// <para>
        /// Used by <see cref="Credentials.MySqlNntpCredentialValidator"/> for AUTHINFO password paths so cache hits require
        /// the same password bytes (modulo ASCII encoding rules). Non-ASCII Unicode code points follow
        /// <see cref="Encoding.ASCII"/> replacement semantics.
        /// </para>
        /// <para>Returns a new array on each call.</para>
        /// </remarks>
        internal static byte[] ComputePasswordFingerprint(string password)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(password ?? string.Empty);
            return SHA256.HashData(bytes);
        }

        /// <summary>
        /// Derives a stable hex dictionary key from an account name and credential fingerprint.
        /// </summary>
        /// <param name="accountName">Account name encoded as UTF-8 in the hash input.</param>
        /// <param name="credentialFingerprint">Fingerprint bytes appended after a <c>|</c> separator.</param>
        /// <returns>
        /// Uppercase hexadecimal SHA-256 digest of <c>UTF8(accountName) | 0x7C | fingerprint</c> suitable for use as
        /// <see cref="ConcurrentDictionary{TKey, TValue}"/> keys.
        /// </returns>
        /// <remarks>
        /// <para>
        /// The separator byte prevents ambiguous concatenation between account names and fingerprint bytes. Identical inputs
        /// always produce identical keys within a process.
        /// </para>
        /// <para>Does not normalize account name casing; callers should use consistent username strings.</para>
        /// </remarks>
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
