// <copyright file="CachingMySqlUserRecordStore.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// EventIds 420-421 (post-success authentication cache read-through).

using Microsoft.Extensions.Logging;

namespace Vector.NNTP.Auth.MySql.Records
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> helpers for <see cref="CachingMySqlUserRecordStore"/> burst-cache
    /// read-through diagnostics.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Role:</b> Logging partial for <see cref="CachingMySqlUserRecordStore"/>. Emits Debug-level hit/miss lines from
    /// <c>TryGetCached</c> when <see cref="INntpUserRecordStore"/> lookups consult
    /// <see cref="MySqlUserRecordCache"/> using the <see cref="MySqlUserRecordCache.UsernameOnlyFingerprint"/> sentinel.
    /// Database lookup lifecycle events remain on <see cref="MySqlUserRecordStore"/> (EventIds <c>400</c>–<c>403</c>).
    /// </para>
    /// <para>
    /// <b>Logger category:</b> Callers pass <see cref="ILogger{TCategoryName}"/> for
    /// <see cref="CachingMySqlUserRecordStore"/> from the decorator instance. Methods are
    /// <see langword="static"/> <see langword="partial"/> with an explicit <see cref="ILogger"/> parameter so
    /// <see cref="LoggerMessageAttribute"/> source generation remains valid on the nested partial class.
    /// </para>
    /// <para><b>Event identifiers:</b></para>
    /// <list type="bullet">
    /// <item><description>EventId <c>420</c> — username-only cache hit (<see cref="AuthCacheHit"/>).</description></item>
    /// <item><description>EventId <c>421</c> — username-only cache miss (<see cref="AuthCacheMiss"/>).</description></item>
    /// </list>
    /// <para>
    /// <b>Observability pairing:</b> <see cref="AuthCacheHit"/> is followed by
    /// <see cref="Telemetry.AuthMySqlMetrics.RecordLookup"/> with outcome <c>cache_hit</c>. A miss logs only EventId
    /// <c>421</c>; the inner <see cref="MySqlUserRecordStore"/> may still emit lookup lifecycle logs and metrics when the
    /// decorator delegates to MySQL. Password-fingerprint burst hits are logged inside
    /// <see cref="Credentials.MySqlNntpCredentialValidator"/>, not by this partial.
    /// </para>
    /// <para>
    /// <b>Semantics:</b> A miss means no valid username-only cache entry was present (expired, never populated, or wrong
    /// fingerprint). It does not by itself mean the account is absent from <c>nntpusers</c>.
    /// </para>
    /// <para>
    /// <b>Privacy:</b> Logs include the plaintext account name for correlation; decrypted passwords, SCRAM keys, and cache
    /// ciphertext are never written by these helpers.
    /// </para>
    /// <para><b>Threading:</b> Static helpers; safe to call from concurrent session handlers on the singleton decorator.</para>
    /// </remarks>
    internal sealed partial class CachingMySqlUserRecordStore
    {
        /// <summary>
        /// Logs that a username-only burst-cache entry satisfied a record-store lookup without MySQL I/O.
        /// </summary>
        /// <param name="logger">
        /// Decorator category logger (typically <see cref="ILogger{TCategoryName}"/> where the category is
        /// <see cref="CachingMySqlUserRecordStore"/>). Must not be <see langword="null"/>.
        /// </param>
        /// <param name="accountName">
        /// NNTP account name used for the cache key. Rendered as <c>'{AccountName}'</c> in the message template.
        /// </param>
        /// <remarks>
        /// <para>
        /// Source-generated at EventId <c>420</c>, <see cref="LogLevel.Debug"/>. Message template:
        /// <c>Auth.MySql auth cache hit for '{AccountName}'</c>.
        /// </para>
        /// <para>
        /// Invoked from <c>TryGetCached</c> immediately after <see cref="MySqlUserRecordCache.TryGet"/> succeeds with
        /// <see cref="MySqlUserRecordCache.UsernameOnlyFingerprint"/>. Pair with
        /// <see cref="Telemetry.AuthMySqlMetrics.RecordLookup"/> outcome <c>cache_hit</c>. No
        /// <see cref="MySqlUserRecordStore"/> lookup logs are emitted for this path.
        /// </para>
        /// </remarks>
        [LoggerMessage(
            EventId = 420,
            Level = LogLevel.Debug,
            Message = "Auth.MySql auth cache hit for '{AccountName}'")]
        private static partial void AuthCacheHit(ILogger logger, string accountName);

        /// <summary>
        /// Logs that no username-only burst-cache entry was available before delegating to the inner store.
        /// </summary>
        /// <param name="logger">
        /// Decorator category logger (typically <see cref="ILogger{TCategoryName}"/> where the category is
        /// <see cref="CachingMySqlUserRecordStore"/>). Must not be <see langword="null"/>.
        /// </param>
        /// <param name="accountName">
        /// NNTP account name used for the cache key. Rendered as <c>'{AccountName}'</c> in the message template.
        /// </param>
        /// <remarks>
        /// <para>
        /// Source-generated at EventId <c>421</c>, <see cref="LogLevel.Debug"/>. Message template:
        /// <c>Auth.MySql auth cache miss for '{AccountName}'</c>.
        /// </para>
        /// <para>
        /// Invoked from <c>TryGetCached</c> when <see cref="MySqlUserRecordCache.TryGet"/> returns
        /// <see langword="false"/> (unknown key, expired entry, or undecryptable payload). The decorator then calls the inner
        /// <see cref="MySqlUserRecordStore"/>, which may log EventIds <c>400</c>–<c>403</c> and record
        /// <c>found</c>, <c>not_found</c>, or <c>transient_failure</c> metrics. This helper does not record a metrics
        /// outcome by itself.
        /// </para>
        /// </remarks>
        [LoggerMessage(
            EventId = 421,
            Level = LogLevel.Debug,
            Message = "Auth.MySql auth cache miss for '{AccountName}'")]
        private static partial void AuthCacheMiss(ILogger logger, string accountName);
    }
}
