// <copyright file="NntpSessionPolicy.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: post-authentication policy handle from credential validation.

namespace Vector.NNTP.Sockets.Authentication
{
    /// <summary>
    /// Policy granted to an authenticated NNTP session (posting and account-level limits).
    /// </summary>
    public sealed class NntpSessionPolicy
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NntpSessionPolicy"/> class.
        /// </summary>
        /// <param name="username">Authenticated username.</param>
        /// <param name="allowPosting">Whether POST is permitted for this identity.</param>
        /// <param name="accountType">Account type flag (for example <c>'B'</c> for both or <c>'R'</c> for reader).</param>
        /// <param name="customerId">Customer identifier associated with the account (UUID string).</param>
        /// <param name="rateLimit">Configured per-session rate limit; <c>0</c> disables enforcement.</param>
        /// <param name="byteLimit">Configured per-session byte limit; <c>0</c> disables enforcement.</param>
        /// <param name="sessionLimit">Maximum concurrent sessions for the account; <c>0</c> disables enforcement.</param>
        /// <param name="srcIpLimit">Maximum concurrent sessions from a single source IP; <c>0</c> disables enforcement.</param>
        public NntpSessionPolicy(
            string username,
            bool allowPosting,
            char accountType,
            string customerId,
            int rateLimit,
            long byteLimit,
            int sessionLimit,
            int srcIpLimit)
        {
            ArgumentException.ThrowIfNullOrEmpty(username);
            this.Username = username;
            this.AllowPosting = allowPosting;
            this.AccountType = accountType;
            this.CustomerId = customerId ?? string.Empty;
            this.RateLimit = rateLimit;
            this.ByteLimit = byteLimit;
            this.SessionLimit = sessionLimit;
            this.SrcIpLimit = srcIpLimit;
        }

        /// <summary>
        /// Gets the authenticated username.
        /// </summary>
        public string Username { get; }

        /// <summary>
        /// Gets a value indicating whether the client may issue POST.
        /// </summary>
        public bool AllowPosting { get; }

        /// <summary>
        /// Gets the account type flag (for example <c>'B'</c> for both or <c>'R'</c> for reader).
        /// </summary>
        public char AccountType { get; }

        /// <summary>
        /// Gets the customer identifier associated with the account.
        /// </summary>
        public string CustomerId { get; }

        /// <summary>
        /// Gets the configured per-session rate limit value.
        /// </summary>
        public int RateLimit { get; }

        /// <summary>
        /// Gets the configured per-session byte limit value.
        /// </summary>
        public long ByteLimit { get; }

        /// <summary>
        /// Gets the maximum concurrent sessions for the account; <c>0</c> disables enforcement.
        /// </summary>
        public int SessionLimit { get; }

        /// <summary>
        /// Gets the maximum concurrent sessions from a single source IP; <c>0</c> disables enforcement.
        /// </summary>
        public int SrcIpLimit { get; }
    }
}
