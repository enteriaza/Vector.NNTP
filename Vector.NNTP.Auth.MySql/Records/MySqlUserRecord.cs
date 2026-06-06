// <copyright file="MySqlUserRecord.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Auth.MySql.Records
{
    /// <summary>
    /// Materialised NNTP user record from the backing MySQL <c>nntpusers</c> table.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Scope:</b> Assembly-internal data-transfer object used by the MySQL credential validator, SCRAM/CRAM credential
    /// stores, and <see cref="INntpUserRecordStore"/>. Hosts integrate via
    /// <see cref="Sockets.Authentication.INntpCredentialValidator"/>,
    /// <see cref="Sockets.Authentication.IScramCredentialStore"/>, and
    /// <see cref="Sockets.Authentication.ICramMd5CredentialStore"/> instead.
    /// </para>
    /// <para>
    /// <b>Account type:</b> <see cref="AccountType"/> is stored as the database <c>char</c> flag (<c>'R'</c> reader,
    /// <c>'B'</c> both). A dedicated enum mapped at materialisation time would remove magic characters but is deferred.
    /// </para>
    /// </remarks>
    internal sealed class MySqlUserRecord
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MySqlUserRecord"/> class.
        /// </summary>
        /// <param name="accountName">User account name.</param>
        /// <param name="accountPassword">Cleartext account password after decryption.</param>
        /// <param name="allowAuthPlain">Whether password-based mechanisms are permitted for the account.</param>
        /// <param name="allowAuthScram256">Whether SCRAM-SHA-256 is permitted for the account.</param>
        /// <param name="scramSalt">SCRAM salt bytes.</param>
        /// <param name="scramIterations">SCRAM PBKDF2 iteration count.</param>
        /// <param name="scramStoredKey">SCRAM stored key material (StoredKey).</param>
        /// <param name="scramServerKey">SCRAM server key material (ServerKey).</param>
        /// <param name="accountType">Account type flag (typically <c>'B'</c> for both or <c>'R'</c> for reader).</param>
        /// <param name="rateLimit">Per-connection rate limit value from the database.</param>
        /// <param name="byteLimit">Per-connection byte limit value from the database.</param>
        /// <param name="sessionLimit">Maximum concurrent sessions for the account.</param>
        /// <param name="srcIpLimit">Maximum concurrent sessions from a single source IP.</param>
        /// <param name="isEnabled">Indicates whether the account is enabled for logon.</param>
        /// <param name="customerId">Customer identifier associated with the account.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="accountName"/> is null or empty.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="scramIterations"/> is negative.</exception>
        internal MySqlUserRecord(
            string accountName,
            string accountPassword,
            bool allowAuthPlain,
            bool allowAuthScram256,
            ReadOnlyMemory<byte> scramSalt,
            int scramIterations,
            ReadOnlyMemory<byte> scramStoredKey,
            ReadOnlyMemory<byte> scramServerKey,
            char accountType,
            int rateLimit,
            long byteLimit,
            int sessionLimit,
            int srcIpLimit,
            bool isEnabled,
            string customerId)
        {
            ArgumentException.ThrowIfNullOrEmpty(accountName);
            ArgumentOutOfRangeException.ThrowIfNegative(scramIterations);

            AccountName = accountName;
            AccountPassword = accountPassword ?? string.Empty;
            AllowAuthPlain = allowAuthPlain;
            AllowAuthScram256 = allowAuthScram256;
            ScramSalt = scramSalt;
            ScramIterations = scramIterations;
            ScramStoredKey = scramStoredKey;
            ScramServerKey = scramServerKey;
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
        internal string AccountName { get; }

        /// <summary>
        /// Gets the decrypted account password in cleartext.
        /// </summary>
        internal string AccountPassword { get; }

        /// <summary>
        /// Gets a value indicating whether password-based mechanisms (AUTHINFO PASS, SASL PLAIN, SASL LOGIN, and CRAM-MD5)
        /// are permitted for this account.
        /// </summary>
        internal bool AllowAuthPlain { get; }

        /// <summary>
        /// Gets a value indicating whether SCRAM-SHA-256 is permitted for this account.
        /// </summary>
        internal bool AllowAuthScram256 { get; }

        /// <summary>
        /// Gets the SCRAM salt bytes.
        /// </summary>
        internal ReadOnlyMemory<byte> ScramSalt { get; }

        /// <summary>
        /// Gets the SCRAM PBKDF2 iteration count.
        /// </summary>
        /// <remarks>
        /// Zero indicates SCRAM material is not provisioned for the account; positive values are required before
        /// <see cref="Credentials.MySqlScramCredentialStore"/> will return stored keys.
        /// </remarks>
        internal int ScramIterations { get; }

        /// <summary>
        /// Gets the SCRAM StoredKey (H(ClientKey)).
        /// </summary>
        internal ReadOnlyMemory<byte> ScramStoredKey { get; }

        /// <summary>
        /// Gets the SCRAM ServerKey.
        /// </summary>
        internal ReadOnlyMemory<byte> ScramServerKey { get; }

        /// <summary>
        /// Gets the account type flag (for example <c>'B'</c> for both or <c>'R'</c> for reader).
        /// </summary>
        internal char AccountType { get; }

        /// <summary>
        /// Gets the configured rate limit value for the account.
        /// </summary>
        internal int RateLimit { get; }

        /// <summary>
        /// Gets the configured byte limit value for the account.
        /// </summary>
        internal long ByteLimit { get; }

        /// <summary>
        /// Gets the maximum concurrent sessions permitted for the account.
        /// </summary>
        internal int SessionLimit { get; }

        /// <summary>
        /// Gets the maximum concurrent sessions from a single source IP address.
        /// </summary>
        internal int SrcIpLimit { get; }

        /// <summary>
        /// Gets a value indicating whether the account is enabled for authentication.
        /// </summary>
        internal bool IsEnabled { get; }

        /// <summary>
        /// Gets the customer identifier associated with the account.
        /// </summary>
        internal string CustomerId { get; }
    }
}
