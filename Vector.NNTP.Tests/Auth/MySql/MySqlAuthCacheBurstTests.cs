// <copyright file="MySqlAuthCacheBurstTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Logging.Abstractions;
using Vector.NNTP.Auth.MySql.Credentials;
using Vector.NNTP.Auth.MySql.Records;
using Vector.NNTP.Auth.MySql.Telemetry;
using Vector.NNTP.Sockets.Authentication;

namespace Vector.NNTP.Tests.Auth.MySql
{
    /// <summary>
    /// Verifies successful-authentication caching deduplicates burst AUTHINFO lookups.
    /// </summary>
    [TestFixture]
    public sealed class MySqlAuthCacheBurstTests
    {
        /// <summary>
        /// Ensures repeated successful AUTHINFO validations hit the backing store only once per TTL window.
        /// </summary>
        /// <returns>A task that completes when the assertion is evaluated.</returns>
        [Test]
        public async Task ValidatePasswordAsync_BurstIdenticalCredentials_SingleStoreLookup()
        {
            MySqlUserRecord record = new MySqlUserRecord(
                "user1",
                "secret",
                allowAuthPlain: true,
                allowAuthScram256: true,
                scramSalt: ReadOnlyMemory<byte>.Empty,
                scramIterations: 0,
                scramStoredKey: ReadOnlyMemory<byte>.Empty,
                scramServerKey: ReadOnlyMemory<byte>.Empty,
                'B',
                10,
                1000L,
                2,
                1,
                true,
                "00000000-0000-0000-0000-0000000042");
            CountingUserRecordStore store = new CountingUserRecordStore(record);
            MySqlUserRecordCache cache = new MySqlUserRecordCache(TimeSpan.FromMinutes(1));
            AuthMySqlMetrics metrics = new AuthMySqlMetrics();
            Blake3AccountKeyNormalizer normalizer = new Blake3AccountKeyNormalizer();
            INntpCredentialValidator validator = new MySqlNntpCredentialValidator(
                store,
                normalizer,
                cache,
                metrics,
                NullLogger<MySqlNntpCredentialValidator>.Instance);

            for (int i = 0; i < 50; i++)
            {
                NntpAuthResult result = await validator.ValidatePasswordAsync(
                    NntpAuthMechanisms.AuthInfoUserPass,
                    "user1",
                    "secret",
                    IPAddress.Loopback,
                    true,
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(result.Status, Is.EqualTo(NntpAuthStatus.Success));
            }

            Assert.That(store.LookupCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Counting fake record store.
        /// </summary>
        private sealed class CountingUserRecordStore : INntpUserRecordStore
        {
            /// <summary>
            /// Backing record.
            /// </summary>
            private readonly MySqlUserRecord _record;

            /// <summary>
            /// Initializes a new instance of the <see cref="CountingUserRecordStore"/> class.
            /// </summary>
            /// <param name="record">Record to return.</param>
            public CountingUserRecordStore(MySqlUserRecord record)
            {
                this._record = record;
            }

            /// <summary>
            /// Gets lookup invocation count.
            /// </summary>
            public int LookupCount { get; private set; }

            /// <inheritdoc />
            public MySqlUserRecord? TryGetUser(string accountName)
            {
                this.LookupCount++;
                return this._record.AccountName == accountName ? this._record : null;
            }

            /// <inheritdoc />
            public Task<MySqlUserRecord?> TryGetUserAsync(string accountName, CancellationToken cancellationToken)
            {
                _ = cancellationToken;
                this.LookupCount++;
                return Task.FromResult<MySqlUserRecord?>(
                    this._record.AccountName == accountName ? this._record : null);
            }
        }
    }
}
