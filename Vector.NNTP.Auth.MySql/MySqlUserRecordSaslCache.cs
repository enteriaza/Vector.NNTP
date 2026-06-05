// <copyright file="MySqlUserRecordSaslCache.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Auth.MySql
{
    /// <summary>
    /// Stashes a user record fetched during SASL credential lookup so completion can avoid a second database round-trip.
    /// </summary>
    /// <remarks>
    /// <para><b>Scope:</b> Values flow via <see cref="AsyncLocal{T}"/> on the logical async call chain for one SASL exchange.
    /// Concurrent SASL attempts for the same username on different connections remain independent.</para>
    /// <para><b>Lifecycle:</b> <see cref="TryTake"/> clears the slot after a successful username match so records are not
    /// reused across unrelated authentications. <see cref="Clear"/> is invoked from
    /// <see cref="MySqlNntpCredentialValidator.AbandonSaslExchange"/> (session auth reset) and from a <c>finally</c> block
    /// after <see cref="MySqlNntpCredentialValidator.CompleteSaslAccountAsync"/> so a prior <see cref="Set"/> does not
    /// linger when the exchange aborts before <see cref="TryTake"/> runs.</para>
    /// </remarks>
    internal static class MySqlUserRecordSaslCache
    {
        /// <summary>
        /// Per-logical-call SASL record slot.
        /// </summary>
        private static readonly AsyncLocal<MySqlUserRecord?> Current = new();

        /// <summary>
        /// Stores a user record for the current SASL exchange after a successful credential-store lookup.
        /// </summary>
        /// <param name="record">Record materialised from the backing store.</param>
        internal static void Set(MySqlUserRecord record)
        {
            ArgumentNullException.ThrowIfNull(record);
            Current.Value = record;
        }

        /// <summary>
        /// Attempts to take a cached record for <paramref name="username"/> without hitting the database.
        /// </summary>
        /// <param name="username">Authenticated username for the SASL completion step.</param>
        /// <param name="record">Cached record when the username matches; otherwise <see langword="null"/>.</param>
        /// <returns><see langword="true"/> when a matching cached record was consumed.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="username"/> is null or empty.</exception>
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
        /// Clears any cached record for the current logical async call without consuming it.
        /// </summary>
        /// <remarks>
        /// <para><b>Call site:</b> Invoke from a <c>finally</c> block after SASL account completion so credential lookup
        /// material does not remain attached to the <see cref="AsyncLocal{T}"/> slot when <see cref="TryTake"/> is skipped
        /// or returns <see langword="false"/> without clearing a mismatched entry.</para>
        /// <para><b>Idempotence:</b> Safe after a successful <see cref="TryTake"/>, which already nulls the slot.</para>
        /// </remarks>
        internal static void Clear()
        {
            Current.Value = null;
        }
    }
}
