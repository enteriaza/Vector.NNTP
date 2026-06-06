// <copyright file="MySqlCredentialStoreTransientTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Logging.Abstractions;
using Vector.NNTP.Auth.MySql.Credentials;
using Vector.NNTP.Auth.MySql.Records;
using Vector.NNTP.Sockets.Authentication;

namespace Vector.NNTP.Tests.Auth.MySql
{
    /// <summary>
    /// Verifies SASL credential stores throw <see cref="NntpCredentialStoreTransientException"/> on backend failure.
    /// </summary>
    [TestFixture]
    public sealed class MySqlCredentialStoreTransientTests
    {
        /// <summary>
        /// Ensures SCRAM store throws transient exception when the record store fails.
        /// </summary>
        [Test]
        public void TryGetScramCredential_BackendFailure_ThrowsTransientException()
        {
            MySqlScramCredentialStore store = new MySqlScramCredentialStore(
                new ThrowingUserRecordStore(),
                NullLogger<MySqlScramCredentialStore>.Instance);

            Assert.Throws<NntpCredentialStoreTransientException>(() =>
            {
                _ = store.TryGetScramCredential("user1", out _);
            });
        }

        /// <summary>
        /// Ensures CRAM store throws transient exception when the record store fails.
        /// </summary>
        [Test]
        public void TryGetCramSecret_BackendFailure_ThrowsTransientException()
        {
            MySqlCramMd5CredentialStore store = new MySqlCramMd5CredentialStore(
                new ThrowingUserRecordStore(),
                NullLogger<MySqlCramMd5CredentialStore>.Instance);

            Assert.Throws<NntpCredentialStoreTransientException>(() =>
            {
                _ = store.TryGetCramSecret("user1", out _);
            });
        }

        /// <summary>
        /// Ensures not-found still returns false without throwing.
        /// </summary>
        [Test]
        public void TryGetScramCredential_UserNotFound_ReturnsFalse()
        {
            MySqlScramCredentialStore store = new MySqlScramCredentialStore(
                new EmptyUserRecordStore(),
                NullLogger<MySqlScramCredentialStore>.Instance);

            bool found = store.TryGetScramCredential("missing", out ScramStoredCredential? credential);

            Assert.That(found, Is.False);
            Assert.That(credential, Is.Null);
        }

        /// <summary>
        /// Record store that always throws.
        /// </summary>
        private sealed class ThrowingUserRecordStore : INntpUserRecordStore
        {
            /// <inheritdoc />
            public MySqlUserRecord? TryGetUser(string accountName)
            {
                _ = accountName;
                throw new InvalidOperationException("Simulated backend outage.");
            }

            /// <inheritdoc />
            public Task<MySqlUserRecord?> TryGetUserAsync(string accountName, CancellationToken cancellationToken)
            {
                _ = accountName;
                _ = cancellationToken;
                throw new InvalidOperationException("Simulated backend outage.");
            }
        }

        /// <summary>
        /// Record store that always returns null.
        /// </summary>
        private sealed class EmptyUserRecordStore : INntpUserRecordStore
        {
            /// <inheritdoc />
            public MySqlUserRecord? TryGetUser(string accountName)
            {
                _ = accountName;
                return null;
            }

            /// <inheritdoc />
            public Task<MySqlUserRecord?> TryGetUserAsync(string accountName, CancellationToken cancellationToken)
            {
                _ = accountName;
                _ = cancellationToken;
                return Task.FromResult<MySqlUserRecord?>(null);
            }
        }
    }
}
