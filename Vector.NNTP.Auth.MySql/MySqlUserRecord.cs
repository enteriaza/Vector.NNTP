// <copyright file="MySqlUserRecord.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: DTO for a single row from the nntpusers table.

namespace Vector.NNTP.Auth.MySql
{
    /// <summary>
    /// Materialised NNTP user record from the backing MySQL <c>nntpusers</c> table.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Scope:</b> This type is an internal data-transfer object used by the MySQL credential validator and CRAM-MD5
    /// secret store. It is not intended for public API consumption.
    /// </para>
    /// </remarks>
    public sealed class MySqlUserRecord
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MySqlUserRecord"/> class.
        /// </summary>
        /// <param name="accountName">User account name.</param>
        /// <param name="accountPassword">Cleartext account password after decryption.</param>
        /// <param name="accountType">Account type flag (typically <c>'B'</c> for both or <c>'R'</c> for reader).</param>
        /// <param name="rateLimit">Per-connection rate limit value from the database.</param>
        /// <param name="byteLimit">Per-connection byte limit value from the database.</param>
        /// <param name="sessionLimit">Maximum concurrent sessions for the account.</param>
        /// <param name="srcIpLimit">Maximum concurrent sessions from a single source IP.</param>
        /// <param name="isEnabled">Indicates whether the account is enabled for logon.</param>
        /// <param name="customerId">Customer identifier associated with the account.</param>
        public MySqlUserRecord(
            string accountName,
            string accountPassword,
            char accountType,
            int rateLimit,
            long byteLimit,
            int sessionLimit,
            int srcIpLimit,
            bool isEnabled,
            string customerId)
        {
            ArgumentException.ThrowIfNullOrEmpty(accountName);
            AccountName = accountName;
            AccountPassword = accountPassword ?? string.Empty;
            AccountType = accountType;
            RateLimit = rateLimit;
            ByteLimit = byteLimit;
            SessionLimit = sessionLimit;
            SrcIpLimit = srcIpLimit;
            IsEnabled = isEnabled;
            CustomerId = customerId ?? string.Empty;
        }

        /// <summary>
        /// Gets the user account name.
        /// </summary>
        public string AccountName { get; }

        /// <summary>
        /// Gets the decrypted account password in cleartext.
        /// </summary>
        public string AccountPassword { get; }

        /// <summary>
        /// Gets the account type flag (for example <c>'B'</c> for both or <c>'R'</c> for reader).
        /// </summary>
        public char AccountType { get; }

        /// <summary>
        /// Gets the configured rate limit value for the account.
        /// </summary>
        public int RateLimit { get; }

        /// <summary>
        /// Gets the configured byte limit value for the account.
        /// </summary>
        public long ByteLimit { get; }

        /// <summary>
        /// Gets the maximum concurrent sessions permitted for the account.
        /// </summary>
        public int SessionLimit { get; }

        /// <summary>
        /// Gets the maximum concurrent sessions from a single source IP address.
        /// </summary>
        public int SrcIpLimit { get; }

        /// <summary>
        /// Gets a value indicating whether the account is enabled for authentication.
        /// </summary>
        public bool IsEnabled { get; }

        /// <summary>
        /// Gets the customer identifier associated with the account.
        /// </summary>
        public string CustomerId { get; }
    }
}
