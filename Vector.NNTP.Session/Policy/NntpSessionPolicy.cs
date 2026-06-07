// <copyright file="NntpSessionPolicy.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Session.Policy
{
    /// <summary>
    /// Policy granted to an authenticated NNTP session (posting and account-level limits).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="RateBytesPerSecond"/> uses decimal SI Mbps conversion via <see cref="NntpRateLimitConverter"/>.
    /// For <see cref="NntpAccountType.RateLimited"/> accounts the rate is an aggregate account ceiling shared across
    /// all concurrent authenticated sessions cluster-wide, not a per-connection entitlement.
    /// </para>
    /// </remarks>
    public sealed class NntpSessionPolicy
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NntpSessionPolicy"/> class with authenticated limits.
        /// </summary>
        /// <param name="username">Authenticated username.</param>
        /// <param name="allowPosting">Whether POST is permitted for this identity.</param>
        /// <param name="accountType">Primary billing/enforcement model.</param>
        /// <param name="customerId">Customer identifier associated with the account.</param>
        /// <param name="rateBytesPerSecond">Active rate limit in bytes/sec when <paramref name="accountType"/> is rate-limited.</param>
        /// <param name="byteLimit">Active byte quota when <paramref name="accountType"/> is byte-limited.</param>
        /// <param name="sessionLimit">Maximum concurrent sessions for the account; <c>0</c> disables admission enforcement.</param>
        /// <param name="srcIpLimit">Maximum distinct source IPs with concurrent sessions; <c>0</c> disables.</param>
        /// <param name="accountKey">Normalized BLAKE3 hex account key for Redis coordination.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="username"/> or <paramref name="accountKey"/> is null or empty.</exception>
        public NntpSessionPolicy(
            string username,
            bool allowPosting,
            NntpAccountType accountType,
            string customerId,
            long rateBytesPerSecond,
            long byteLimit,
            int sessionLimit,
            int srcIpLimit,
            string accountKey)
        {
            ArgumentException.ThrowIfNullOrEmpty(username);
            ArgumentException.ThrowIfNullOrEmpty(accountKey);
            Username = username;
            AllowPosting = allowPosting;
            AccountType = accountType;
            CustomerId = customerId ?? string.Empty;
            RateBytesPerSecond = rateBytesPerSecond;
            ByteLimit = byteLimit;
            SessionLimit = sessionLimit;
            SrcIpLimit = srcIpLimit;
            AccountKey = accountKey;
        }

        /// <summary>
        /// Gets the authenticated username from credential validation.
        /// </summary>
        public string Username { get; }

        /// <summary>
        /// Gets a value indicating whether the client may issue POST for this identity.
        /// </summary>
        public bool AllowPosting { get; }

        /// <summary>
        /// Gets the primary billing and enforcement model (rate-limited vs byte-limited).
        /// </summary>
        public NntpAccountType AccountType { get; }

        /// <summary>
        /// Gets the customer identifier associated with the account for billing correlation.
        /// </summary>
        public string CustomerId { get; }

        /// <summary>
        /// Gets the account aggregate send rate in bytes per second (decimal SI Mbps source); <c>0</c> disables rate enforcement.
        /// </summary>
        /// <remarks>Decimal SI Mbps; not Mibps. See <see cref="NntpRateLimitConverter"/>.</remarks>
        public long RateBytesPerSecond { get; }

        /// <summary>
        /// Gets the cluster-wide byte quota when byte-limited; <c>0</c> disables block enforcement.
        /// </summary>
        public long ByteLimit { get; }

        /// <summary>
        /// Gets the maximum concurrent sessions for the account; <c>0</c> disables distributed admission.
        /// </summary>
        public int SessionLimit { get; }

        /// <summary>
        /// Gets the maximum distinct client source IPs with concurrent sessions; <c>0</c> disables source-IP admission.
        /// </summary>
        public int SrcIpLimit { get; }

        /// <summary>
        /// Gets the normalized BLAKE3 hex digest used for Redis keying (not the raw username).
        /// </summary>
        public string AccountKey { get; }

        /// <summary>
        /// Returns whether distributed admission should run for this policy.
        /// </summary>
        /// <returns><see langword="true"/> when session or source-IP limits are positive.</returns>
        public bool RequiresDistributedAdmission()
        {
            return SessionLimit > 0 || SrcIpLimit > 0;
        }
    }
}
