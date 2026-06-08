// <copyright file="MySqlUserRecordSaslCache.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: per-exchange AsyncLocal staging between SASL credential lookup and account completion.

namespace Vector.NNTP.Auth.MySql.Records
{
    /// <summary>
    /// Stashes a <see cref="MySqlUserRecord"/> fetched during SASL credential lookup so account completion can avoid a
    /// second database round-trip on the same logical async flow.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Role:</b> Two-step SASL authentication loads credential material first
    /// (<see cref="Credentials.MySqlScramCredentialStore"/> or <see cref="Credentials.MySqlCramMd5CredentialStore"/>), then
    /// finalizes policy in <see cref="Credentials.MySqlNntpCredentialValidator"/> via
    /// <see cref="Sockets.Authentication.INntpSaslAccountAuthenticator.CompleteSaslAccountAsync"/>. Successful credential-store
    /// lookups call <see cref="Set"/>; completion calls <see cref="TryTake"/> before falling back to
    /// <see cref="INntpUserRecordStore.TryGetUserAsync"/>.
    /// </para>
    /// <para>
    /// <b>Not a burst cache:</b> Unlike TTL-backed <see cref="MySqlUserRecordCache"/>, this slot is scoped to one in-flight
    /// SASL exchange on the current <see cref="AsyncLocal{T}"/> execution context and holds a plaintext
    /// <see cref="MySqlUserRecord"/> only until <see cref="TryTake"/> or <see cref="Clear"/> runs.
    /// </para>
    /// <para>
    /// <b>Scope:</b> Values flow via <see cref="AsyncLocal{T}"/> on the logical async call chain for a single SASL dialog.
    /// Concurrent SASL attempts on different connections or thread-pool workers remain independent even for the same
    /// username.
    /// </para>
    /// <para><b>Lifecycle:</b></para>
    /// <list type="number">
    /// <item><description>Credential store succeeds → <see cref="Set"/>.</description></item>
    /// <item>
    /// <description>
    /// Completion runs → <see cref="TryTake"/> on matching username (consumes and nulls the slot) or cache miss → database
    /// lookup.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <see cref="Clear"/> in a <c>finally</c> block inside <c>FinalizeAuthenticationAsync</c> (SASL completion path) and
    /// from <see cref="Credentials.MySqlNntpCredentialValidator"/> when
    /// <see cref="Sockets.Authentication.INntpSaslAccountAuthenticator.AbandonSaslExchange"/> runs (client auth reset).
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// <b>Username matching:</b> <see cref="TryTake"/> compares <see cref="MySqlUserRecord.AccountName"/> to the completion
    /// username with ordinal case-sensitive equality. A mismatch returns <see langword="false"/> and leaves the staged record
    /// in place until <see cref="Clear"/>.
    /// </para>
    /// <para><b>Thread safety:</b> <see cref="AsyncLocal{T}"/> isolates slots per execution context; static methods do not
    /// share state across unrelated sessions.</para>
    /// </remarks>
    internal static class MySqlUserRecordSaslCache
    {
        /// <summary>
        /// Per-logical-call slot holding at most one staged <see cref="MySqlUserRecord"/> for the current SASL exchange.
        /// </summary>
        /// <remarks>
        /// Initialized once. <see langword="null"/> means no staged record. Overwritten by a later <see cref="Set"/> on the
        /// same execution context without explicit removal.
        /// </remarks>
        private static readonly AsyncLocal<MySqlUserRecord?> Current = new();

        /// <summary>
        /// Stores a user record for the current SASL exchange after a successful credential-store lookup.
        /// </summary>
        /// <param name="record">
        /// Record materialised from <see cref="INntpUserRecordStore"/> during SCRAM or CRAM secret retrieval. Must not be
        /// <see langword="null"/>.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="record"/> is <see langword="null"/>.
        /// </exception>
        /// <remarks>
        /// <para>
        /// Invoked from <see cref="Credentials.MySqlScramCredentialStore"/> and
        /// <see cref="Credentials.MySqlCramMd5CredentialStore"/> only after policy and material checks pass. Replaces any
        /// prior value in <see cref="Current"/> on the same async flow.
        /// </para>
        /// <para>Does not populate <see cref="MySqlUserRecordCache"/>; burst deduplication happens only after successful
        /// completion.</para>
        /// </remarks>
        internal static void Set(MySqlUserRecord record)
        {
            ArgumentNullException.ThrowIfNull(record);
            Current.Value = record;
        }

        /// <summary>
        /// Attempts to consume a staged record for <paramref name="username"/> without querying MySQL.
        /// </summary>
        /// <param name="username">
        /// Username supplied to SASL account completion. Compared ordinally to <see cref="MySqlUserRecord.AccountName"/>.
        /// Must not be null or empty.
        /// </param>
        /// <param name="record">
        /// When this method returns <see langword="true"/>, the consumed <see cref="MySqlUserRecord"/> and the slot is
        /// cleared. When this method returns <see langword="false"/>, <see langword="null"/> and any mismatched staged record
        /// remains until <see cref="Clear"/>.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when a non-null staged record exists and its account name equals <paramref name="username"/>;
        /// otherwise <see langword="false"/>.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="username"/> is <see langword="null"/> or empty.
        /// </exception>
        /// <remarks>
        /// <para>
        /// Called from <c>FinalizeAuthenticationAsync</c> before <see cref="INntpUserRecordStore.TryGetUserAsync"/> on cache
        /// miss. A hit avoids a second lookup when the credential store already materialised the same row during the SASL
        /// proof step.
        /// </para>
        /// <para>Never throws for non-matching or empty slots.</para>
        /// </remarks>
        internal static bool TryTake(string username, out MySqlUserRecord? record)
        {
            ArgumentException.ThrowIfNullOrEmpty(username);

            MySqlUserRecord? cached = Current.Value;
            if (cached is null || !string.Equals(cached.AccountName, username, StringComparison.Ordinal))
            {
                record = null;
                return false;
            }

            Current.Value = null;
            record = cached;
            return true;
        }

        /// <summary>
        /// Clears any staged record for the current logical async call without consuming it.
        /// </summary>
        /// <remarks>
        /// <para><b>Call sites:</b></para>
        /// <list type="bullet">
        /// <item>
        /// <description>
        /// <see cref="Sockets.Authentication.INntpSaslAccountAuthenticator.AbandonSaslExchange"/> implementation when the
        /// client resets authentication mid-exchange.
        /// </description>
        /// </item>
        /// <item>
        /// <description>
        /// <c>finally</c> block in <c>FinalizeAuthenticationAsync</c> after SASL completion so credential lookup material
        /// does not leak when <see cref="TryTake"/> is skipped, returns <see langword="false"/>, or already consumed the
        /// slot (including after username mismatch left a stale entry).
        /// </description>
        /// </item>
        /// </list>
        /// <para><b>Idempotence:</b> Safe when the slot is already <see langword="null"/> (including after successful
        /// <see cref="TryTake"/>).</para>
        /// </remarks>
        internal static void Clear()
        {
            Current.Value = null;
        }
    }
}
