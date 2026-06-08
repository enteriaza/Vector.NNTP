// <copyright file="MySqlUserRecord.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Auth.MySql.Records
{
    /// <summary>
    /// Immutable in-process snapshot of one NNTP account row from the backing MySQL <c>nntpusers</c> table.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Role:</b> Assembly-internal data-transfer object produced by <see cref="MySqlUserRecordStore"/> (via
    /// <c>MapUserRecord</c>) and consumed by <see cref="Credentials.MySqlNntpCredentialValidator"/>,
    /// <see cref="Credentials.MySqlScramCredentialStore"/>, <see cref="Credentials.MySqlCramMd5CredentialStore"/>,
    /// <see cref="MySqlUserRecordSaslCache"/>, and <see cref="MySqlUserRecordCache"/> (after AES-GCM protection). Hosts
    /// integrate through <see cref="Sockets.Authentication.INntpCredentialValidator"/>,
    /// <see cref="Sockets.Authentication.IScramCredentialStore"/>, and
    /// <see cref="Sockets.Authentication.ICramMd5CredentialStore"/> instead of this type.
    /// </para>
    /// <para>
    /// <b>Sensitivity:</b> <see cref="AccountPassword"/> holds cleartext after SQL <c>AES_DECRYPT</c>. Records exist only
    /// in the auth assembly process; burst-cache copies are encrypted at rest by
    /// <see cref="MySqlUserRecordCacheProtection"/>.
    /// </para>
    /// <para>
    /// <b>Materialisation:</b> Flag columns (<see cref="AllowAuthPlain"/>, <see cref="AllowAuthScram256"/>,
    /// <see cref="IsEnabled"/>) are <see langword="true"/> only when the database value is <c>Y</c> (case-insensitive).
    /// Null numerics default to <c>0</c>; null <see cref="AccountType"/> defaults to <c>'R'</c> at mapping time. Null
    /// password and <see cref="CustomerId"/> become <see cref="string.Empty"/>.
    /// </para>
    /// <para>
    /// <b>Immutability:</b> All properties are set once in the constructor and are not mutated afterward. SCRAM byte
    /// properties expose read-only memory views; callers must treat returned buffers as read-only.
    /// </para>
    /// </remarks>
    internal sealed class MySqlUserRecord
    {
        /// <summary>
        /// Creates a validated user-record snapshot from mapped database columns or cache deserialization.
        /// </summary>
        /// <param name="accountName">
        /// Plaintext NNTP account name supplied by the lookup caller (not the MD5 hash stored in
        /// <c>nntpusers.account_name</c>).
        /// </param>
        /// <param name="accountPassword">
        /// Cleartext password after <c>AES_DECRYPT</c> in <see cref="MySqlUserRecordStore"/>. <see langword="null"/> is
        /// coerced to <see cref="string.Empty"/>.
        /// </param>
        /// <param name="allowAuthPlain">
        /// Whether password-oriented mechanisms (AUTHINFO PASS, SASL PLAIN/LOGIN, CRAM-MD5) are permitted for the account.
        /// </param>
        /// <param name="allowAuthScram256">Whether SCRAM-SHA-256 is permitted for the account.</param>
        /// <param name="scramSalt">SCRAM salt bytes from <c>scram_salt</c>; empty when SQL <c>NULL</c> or zero-length.</param>
        /// <param name="scramIterations">
        /// SCRAM PBKDF2 iteration count from <c>scram_iterations</c>. Must be non-negative; <c>0</c> means SCRAM is not
        /// provisioned.
        /// </param>
        /// <param name="scramStoredKey">
        /// SCRAM StoredKey (H of ClientKey) from <c>scram_stored_key</c>; empty when not provisioned.
        /// </param>
        /// <param name="scramServerKey">
        /// SCRAM ServerKey from <c>scram_server_key</c>; empty when not provisioned.
        /// </param>
        /// <param name="accountType">
        /// MySQL <c>account_type</c> flag: <c>'R'</c> rate-limited reader or <c>'B'</c> byte-limited account (see
        /// <see cref="Session.Policy.NntpSessionPolicyFactory"/>).
        /// </param>
        /// <param name="rateLimit">
        /// Decimal SI Mbps from <c>account_rate_limit</c>; enforced when <paramref name="accountType"/> is <c>'R'</c>.
        /// </param>
        /// <param name="byteLimit">
        /// Byte quota from <c>account_byte_limit</c>; enforced when <paramref name="accountType"/> is <c>'B'</c>.
        /// </param>
        /// <param name="sessionLimit">
        /// Cluster-wide concurrent session cap from <c>account_session_limit</c>; <c>0</c> disables session limiting.
        /// </param>
        /// <param name="srcIpLimit">
        /// Per-source-IP concurrent session cap from <c>account_srcip_limit</c>; <c>0</c> disables IP limiting.
        /// </param>
        /// <param name="isEnabled">
        /// Whether the account may authenticate; disabled accounts are rejected before password or SASL verification.
        /// </param>
        /// <param name="customerId">
        /// Customer or tenant identifier from <c>customer_id</c>; <see langword="null"/> is coerced to
        /// <see cref="string.Empty"/>.
        /// </param>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="accountName"/> is <see langword="null"/> or empty.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="scramIterations"/> is negative.
        /// </exception>
        /// <remarks>
        /// Invoked from <see cref="MySqlUserRecordStore"/> row mapping and
        /// <see cref="MySqlUserRecordCacheProtection"/> deserialization. Does not perform authentication checks beyond
        /// structural validation.
        /// </remarks>
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
        /// Gets the plaintext NNTP account name used for lookups, logging, and session policy identity.
        /// </summary>
        /// <value>Non-empty string identical to the username presented on the wire (not the MD5 hash column).</value>
        /// <remarks>
        /// Compared ordinally in <see cref="MySqlUserRecordSaslCache.TryTake"/> and used when deriving burst-cache keys in
        /// <see cref="MySqlUserRecordCache"/>.
        /// </remarks>
        internal string AccountName { get; }

        /// <summary>
        /// Gets the decrypted account password in cleartext.
        /// </summary>
        /// <value>
        /// Password bytes decoded as a string after SQL <c>AES_DECRYPT</c>; never <see langword="null"/> (empty when the
        /// column decrypts to null).
        /// </value>
        /// <remarks>
        /// <para>
        /// Compared in <see cref="Credentials.MySqlNntpCredentialValidator"/> for AUTHINFO PASS. CRAM-MD5 requires ASCII
        /// encoding (<see cref="Credentials.MySqlCramMd5CredentialStore"/> rejects non-ASCII passwords).
        /// </para>
        /// <para>Serialized into burst-cache payloads; treat as highly sensitive in memory.</para>
        /// </remarks>
        internal string AccountPassword { get; }

        /// <summary>
        /// Gets a value indicating whether password-based authentication mechanisms are allowed for this account.
        /// </summary>
        /// <value>
        /// <see langword="true"/> when <c>allow_auth_plain</c> is <c>Y</c> in the database; otherwise
        /// <see langword="false"/>.
        /// </value>
        /// <remarks>
        /// Gates AUTHINFO PASS, SASL PLAIN, SASL LOGIN, and CRAM-MD5 in the validator and credential stores. Independent
        /// of <see cref="AllowAuthScram256"/>.
        /// </remarks>
        internal bool AllowAuthPlain { get; }

        /// <summary>
        /// Gets a value indicating whether SCRAM-SHA-256 is allowed for this account.
        /// </summary>
        /// <value>
        /// <see langword="true"/> when <c>allow_auth_scram256</c> is <c>Y</c> in the database; otherwise
        /// <see langword="false"/>.
        /// </value>
        /// <remarks>
        /// Checked by <see cref="Credentials.MySqlScramCredentialStore"/> before returning SCRAM material. SASL SCRAM
        /// completion in <see cref="Credentials.MySqlNntpCredentialValidator"/> uses the same flag.
        /// </remarks>
        internal bool AllowAuthScram256 { get; }

        /// <summary>
        /// Gets the SCRAM salt bytes provisioned for the account.
        /// </summary>
        /// <value>Binary salt from <c>scram_salt</c>, or empty when not provisioned.</value>
        /// <remarks>
        /// Together with <see cref="ScramIterations"/>, <see cref="ScramStoredKey"/>, and <see cref="ScramServerKey"/>,
        /// must all be non-empty with positive iterations before SCRAM lookup succeeds.
        /// </remarks>
        internal ReadOnlyMemory<byte> ScramSalt { get; }

        /// <summary>
        /// Gets the SCRAM PBKDF2 iteration count for the account.
        /// </summary>
        /// <value>
        /// Non-negative iteration count from <c>scram_iterations</c>; <c>0</c> when the column is null or SCRAM is not
        /// configured.
        /// </value>
        /// <remarks>
        /// <see cref="Credentials.MySqlScramCredentialStore"/> treats <c>0</c> as missing SCRAM provisioning and returns no
        /// credential.
        /// </remarks>
        internal int ScramIterations { get; }

        /// <summary>
        /// Gets the SCRAM StoredKey (H of ClientKey) for server-side verification.
        /// </summary>
        /// <value>Binary key material from <c>scram_stored_key</c>, or empty when not provisioned.</value>
        /// <remarks>Passed to <see cref="Sockets.Authentication.ScramStoredCredential"/> on successful SCRAM lookup.</remarks>
        internal ReadOnlyMemory<byte> ScramStoredKey { get; }

        /// <summary>
        /// Gets the SCRAM ServerKey used to sign the server proof.
        /// </summary>
        /// <value>Binary key material from <c>scram_server_key</c>, or empty when not provisioned.</value>
        /// <remarks>Passed to <see cref="Sockets.Authentication.ScramStoredCredential"/> on successful SCRAM lookup.</remarks>
        internal ReadOnlyMemory<byte> ScramServerKey { get; }

        /// <summary>
        /// Gets the account enforcement model flag from the database.
        /// </summary>
        /// <value>
        /// <c>'R'</c> for rate-limited accounts or <c>'B'</c> for byte-limited accounts; defaults to <c>'R'</c> when the
        /// column is null at mapping time.
        /// </value>
        /// <remarks>
        /// Fed into <see cref="Session.Policy.NntpAccountLimits"/> and mapped by
        /// <see cref="Session.Policy.NntpSessionPolicyFactory"/> to <see cref="Session.Policy.NntpAccountType"/>.
        /// </remarks>
        internal char AccountType { get; }

        /// <summary>
        /// Gets the per-connection rate limit in decimal SI megabits per second.
        /// </summary>
        /// <value>Copied from <c>account_rate_limit</c>; <c>0</c> when the column is null.</value>
        /// <remarks>
        /// Active for <see cref="AccountType"/> <c>'R'</c> accounts. Converted to bytes per second in
        /// <see cref="Credentials.MySqlNntpCredentialValidator"/> when building <see cref="Session.Policy.NntpSessionPolicy"/>.
        /// </remarks>
        internal int RateLimit { get; }

        /// <summary>
        /// Gets the per-connection byte quota for byte-limited accounts.
        /// </summary>
        /// <value>Copied from <c>account_byte_limit</c>; <c>0</c> when the column is null.</value>
        /// <remarks>
        /// Active for <see cref="AccountType"/> <c>'B'</c> accounts. Passed through to
        /// <see cref="Session.Policy.NntpSessionPolicy"/> via <see cref="Session.Policy.NntpAccountLimits"/>.
        /// </remarks>
        internal long ByteLimit { get; }

        /// <summary>
        /// Gets the maximum number of concurrent authenticated sessions allowed for the account cluster-wide.
        /// </summary>
        /// <value>Copied from <c>account_session_limit</c>; <c>0</c> disables the limit.</value>
        /// <remarks>Enforced by session coordination after successful authentication, not at lookup time.</remarks>
        internal int SessionLimit { get; }

        /// <summary>
        /// Gets the maximum number of concurrent authenticated sessions allowed from a single client IP.
        /// </summary>
        /// <value>Copied from <c>account_srcip_limit</c>; <c>0</c> disables the limit.</value>
        /// <remarks>Enforced by session coordination after successful authentication, not at lookup time.</remarks>
        internal int SrcIpLimit { get; }

        /// <summary>
        /// Gets a value indicating whether the account is enabled for logon.
        /// </summary>
        /// <value>
        /// <see langword="true"/> when <c>is_enabled</c> is <c>Y</c> in the database; otherwise <see langword="false"/>.
        /// </value>
        /// <remarks>
        /// Checked before credential comparison in <see cref="Credentials.MySqlNntpCredentialValidator"/> and before
        /// returning SASL/CRAM material from the credential stores. Disabled accounts never populate the auth burst cache.
        /// </remarks>
        internal bool IsEnabled { get; }

        /// <summary>
        /// Gets the customer or tenant identifier associated with the account.
        /// </summary>
        /// <value>
        /// String form of <c>customer_id</c> (GUID values use <c>D</c> format); never <see langword="null"/> (empty when the
        /// column is null).
        /// </value>
        /// <remarks>
        /// Carried into <see cref="Session.Policy.NntpSessionPolicy"/> for billing and multi-tenant session tracking.
        /// </remarks>
        internal string CustomerId { get; }
    }
}
