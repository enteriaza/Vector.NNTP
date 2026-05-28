// <copyright file="MySqlNntpCredentialValidatorTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: verifies password comparison and policy mapping logic without a real MySQL instance.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Vector.NNTP.Auth.MySql;
using Vector.NNTP.Sockets.Authentication;

namespace Vector.NNTP.Tests.Auth.MySql
{
    /// <summary>
    /// Tests for <see cref="MySqlNntpCredentialValidator"/> password comparison and policy mapping.
    /// </summary>
    [TestFixture]
    public sealed class MySqlNntpCredentialValidatorTests
    {
        /// <summary>
        /// Ensures that matching passwords yield a success result with a non-null policy.
        /// </summary>
        /// <returns>A task that completes when the assertion is evaluated.</returns>
        [Test]
        public async Task ValidatePasswordAsync_MatchingPassword_Succeeds()
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
            FakeUserRecordStore store = new FakeUserRecordStore(record);
            Blake3AccountKeyNormalizer normalizer = new Blake3AccountKeyNormalizer();
            MySqlNntpCredentialValidator validator = new MySqlNntpCredentialValidator(store, normalizer, NullLogger<MySqlNntpCredentialValidator>.Instance);

            NntpAuthResult result = await validator.ValidatePasswordAsync(
                NntpAuthMechanisms.AuthInfoUserPass,
                "user1",
                "secret",
                IPAddress.Loopback,
                true,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(NntpAuthStatus.Success));
            Assert.That(result.Policy, Is.Not.Null);
            Assert.That(result.Policy!.Username, Is.EqualTo("user1"));
            Assert.That(result.Policy!.AllowPosting, Is.True);
            Assert.That(result.Policy!.AccountType, Is.EqualTo(NntpAccountType.ByteLimited));
            Assert.That(result.Policy!.RateBytesPerSecond, Is.EqualTo(0));
            Assert.That(result.Policy!.ByteLimit, Is.EqualTo(1000L));
            Assert.That(result.Policy!.CustomerId, Is.EqualTo("00000000-0000-0000-0000-0000000042"));
            Assert.That(result.Policy!.SessionLimit, Is.EqualTo(2));
            Assert.That(result.Policy!.SrcIpLimit, Is.EqualTo(1));
        }

        /// <summary>
        /// Ensures rate-limited accounts map Mbps to bytes/sec without performing admission.
        /// </summary>
        /// <returns>A task that completes when the assertion is evaluated.</returns>
        [Test]
        public async Task ValidatePasswordAsync_RateLimitedAccount_MapsMbpsToBytesPerSecond()
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
                'R',
                10,
                0L,
                0,
                0,
                true,
                string.Empty);
            FakeUserRecordStore store = new FakeUserRecordStore(record);
            Blake3AccountKeyNormalizer normalizer = new Blake3AccountKeyNormalizer();
            MySqlNntpCredentialValidator validator = new MySqlNntpCredentialValidator(store, normalizer, NullLogger<MySqlNntpCredentialValidator>.Instance);

            NntpAuthResult result = await validator.ValidatePasswordAsync(
                NntpAuthMechanisms.AuthInfoUserPass,
                "user1",
                "secret",
                IPAddress.Loopback,
                true,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(NntpAuthStatus.Success));
            Assert.That(result.Policy!.AccountType, Is.EqualTo(NntpAccountType.RateLimited));
            Assert.That(result.Policy!.RateBytesPerSecond, Is.EqualTo(NntpRateLimitConverter.MegabitsPerSecondToBytesPerSecond(10)));
        }

        /// <summary>
        /// Ensures that non-matching passwords yield an invalid-credentials result.
        /// </summary>
        /// <returns>A task that completes when the assertion is evaluated.</returns>
        [Test]
        public async Task ValidatePasswordAsync_MismatchedPassword_Fails()
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
                'R',
                0,
                0L,
                0,
                0,
                true,
                string.Empty);
            FakeUserRecordStore store = new FakeUserRecordStore(record);
            Blake3AccountKeyNormalizer normalizer = new Blake3AccountKeyNormalizer();
            MySqlNntpCredentialValidator validator = new MySqlNntpCredentialValidator(store, normalizer, NullLogger<MySqlNntpCredentialValidator>.Instance);

            NntpAuthResult result = await validator.ValidatePasswordAsync(
                NntpAuthMechanisms.SaslPlain,
                "user1",
                "wrong",
                IPAddress.Loopback,
                true,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(NntpAuthStatus.InvalidCredentials));
        }

        /// <summary>
        /// Ensures that SCRAM completion after proof verification yields a success result with policy from the record.
        /// </summary>
        /// <returns>A task that completes when the assertion is evaluated.</returns>
        [Test]
        public async Task CompleteSaslAccountAsync_Scram_ValidAccount_Succeeds()
        {
            MySqlUserRecord record = new MySqlUserRecord(
                "user1",
                "secret",
                allowAuthPlain: false,
                allowAuthScram256: true,
                scramSalt: new byte[] { 1, 2, 3 },
                scramIterations: 4096,
                scramStoredKey: new byte[32],
                scramServerKey: new byte[32],
                'B',
                10,
                1000L,
                2,
                1,
                true,
                "00000000-0000-0000-0000-0000000042");
            FakeUserRecordStore store = new FakeUserRecordStore(record);
            Blake3AccountKeyNormalizer normalizer = new Blake3AccountKeyNormalizer();
            MySqlNntpCredentialValidator validator = new MySqlNntpCredentialValidator(store, normalizer, NullLogger<MySqlNntpCredentialValidator>.Instance);

            NntpAuthResult result = await validator.CompleteSaslAccountAsync(
                NntpAuthMechanisms.SaslScramSha256,
                "user1",
                IPAddress.Loopback,
                true,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(NntpAuthStatus.Success));
            Assert.That(result.Policy, Is.Not.Null);
            Assert.That(result.Policy!.Username, Is.EqualTo("user1"));
            Assert.That(result.Policy!.AllowPosting, Is.True);
        }

        /// <summary>
        /// Ensures that CRAM-MD5 completion after proof verification yields a success result with policy from the record.
        /// </summary>
        /// <returns>A task that completes when the assertion is evaluated.</returns>
        [Test]
        public async Task CompleteSaslAccountAsync_Cram_ValidAccount_Succeeds()
        {
            MySqlUserRecord record = new MySqlUserRecord(
                "user1",
                "secret",
                allowAuthPlain: true,
                allowAuthScram256: false,
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
            FakeUserRecordStore store = new FakeUserRecordStore(record);
            Blake3AccountKeyNormalizer normalizer = new Blake3AccountKeyNormalizer();
            MySqlNntpCredentialValidator validator = new MySqlNntpCredentialValidator(store, normalizer, NullLogger<MySqlNntpCredentialValidator>.Instance);

            NntpAuthResult result = await validator.CompleteSaslAccountAsync(
                NntpAuthMechanisms.SaslCramMd5,
                "user1",
                IPAddress.Loopback,
                true,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(NntpAuthStatus.Success));
            Assert.That(result.Policy, Is.Not.Null);
            Assert.That(result.Policy!.Username, Is.EqualTo("user1"));
        }

        /// <summary>
        /// Ensures that backend exceptions are treated as transient authentication failure.
        /// </summary>
        /// <returns>A task that completes when the assertion is evaluated.</returns>
        [Test]
        public async Task ValidatePasswordAsync_RecordStoreThrows_TransientFailure()
        {
            ThrowingUserRecordStore store = new ThrowingUserRecordStore();
            Blake3AccountKeyNormalizer normalizer = new Blake3AccountKeyNormalizer();
            MySqlNntpCredentialValidator validator = new MySqlNntpCredentialValidator(store, normalizer, NullLogger<MySqlNntpCredentialValidator>.Instance);

            NntpAuthResult result = await validator.ValidatePasswordAsync(
                NntpAuthMechanisms.AuthInfoUserPass,
                "user1",
                "secret",
                IPAddress.Loopback,
                true,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(NntpAuthStatus.TransientFailure));
        }

        /// <summary>
        /// Fake user record store for validator unit tests.
        /// </summary>
        private sealed class FakeUserRecordStore : INntpUserRecordStore
        {
            /// <summary>
            /// Backing record returned when the account name matches.
            /// </summary>
            private readonly MySqlUserRecord? _record;

            /// <summary>
            /// Initializes a new instance of the <see cref="FakeUserRecordStore"/> class.
            /// </summary>
            /// <param name="record">Optional user record to return.</param>
            public FakeUserRecordStore(MySqlUserRecord? record)
            {
                this._record = record;
            }

            /// <inheritdoc />
            public Task<MySqlUserRecord?> TryGetUserAsync(string accountName, CancellationToken cancellationToken)
            {
                _ = cancellationToken;
                if (this._record is null || this._record.AccountName != accountName)
                {
                    return Task.FromResult<MySqlUserRecord?>(null);
                }

                return Task.FromResult<MySqlUserRecord?>(this._record);
            }
        }

        /// <summary>
        /// Fake user record store that always throws, simulating a backend outage.
        /// </summary>
        private sealed class ThrowingUserRecordStore : INntpUserRecordStore
        {
            /// <inheritdoc />
            public Task<MySqlUserRecord?> TryGetUserAsync(string accountName, CancellationToken cancellationToken)
            {
                _ = accountName;
                _ = cancellationToken;
                throw new InvalidOperationException("Simulated backend outage.");
            }
        }
    }
}
