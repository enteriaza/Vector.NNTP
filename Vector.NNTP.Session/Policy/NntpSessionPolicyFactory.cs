// <copyright file="NntpSessionPolicyFactory.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Session.Policy
{
    /// <summary>
    /// Builds <see cref="NntpSessionPolicy"/> instances from persistence DTOs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Account type mapping:</b> MySQL <c>'R'</c> → <see cref="NntpAccountType.RateLimited"/> with
    /// <see cref="NntpAccountLimits.RateLimitMbps"/> converted via decimal SI Mbps;
    /// <c>'B'</c> → <see cref="NntpAccountType.ByteLimited"/> with <see cref="NntpAccountLimits.ByteLimit"/>.
    /// </para>
    /// </remarks>
    public static class NntpSessionPolicyFactory
    {
        /// <summary>
        /// Creates a session policy from account limits and posting permission.
        /// </summary>
        /// <param name="limits">Mapped limit columns from persistence.</param>
        /// <param name="allowPosting">Whether POST is permitted.</param>
        /// <param name="accountKeyNormalizer">Account key normalizer (BLAKE3 hex).</param>
        /// <returns>Policy for authentication success and enforcement.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public static NntpSessionPolicy Create(
            NntpAccountLimits limits,
            bool allowPosting,
            IAccountKeyNormalizer accountKeyNormalizer)
        {
            ArgumentNullException.ThrowIfNull(limits);
            ArgumentNullException.ThrowIfNull(accountKeyNormalizer);

            NntpAccountType accountType = MapAccountType(limits.AccountTypeChar);
            long rateBytesPerSecond = accountType == NntpAccountType.RateLimited
                ? NntpRateLimitConverter.MegabitsPerSecondToBytesPerSecond(limits.RateLimitMbps)
                : 0;
            long byteLimit = accountType == NntpAccountType.ByteLimited ? limits.ByteLimit : 0;
            string accountKey = accountKeyNormalizer.ComputeAccountKey(limits.Username);

            return new NntpSessionPolicy(
                limits.Username,
                allowPosting,
                accountType,
                limits.CustomerId,
                rateBytesPerSecond,
                byteLimit,
                limits.SessionLimit,
                limits.SrcIpLimit,
                accountKey);
        }

        /// <summary>
        /// Maps the MySQL account type character to <see cref="NntpAccountType"/>.
        /// </summary>
        /// <param name="accountTypeChar">Raw <c>account_type</c> column value.</param>
        /// <returns>Mapped enforcement model; unknown values default to byte-limited.</returns>
        public static NntpAccountType MapAccountType(char accountTypeChar)
        {
            return accountTypeChar is 'R' or 'r'
                ? NntpAccountType.RateLimited
                : NntpAccountType.ByteLimited;
        }
    }
}
