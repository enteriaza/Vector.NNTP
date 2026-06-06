// <copyright file="AuthenticationTimeoutTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Vector.NNTP.Auth.MySql.Credentials;
using Vector.NNTP.Auth.MySql.Records;
using Vector.NNTP.Auth.MySql.Telemetry;
using Vector.NNTP.Sockets.Authentication;

namespace Vector.NNTP.Tests.Auth.MySql
{
    /// <summary>
    /// Verifies slow or timing-out authentication backends complete with transient failure semantics.
    /// </summary>
    [TestFixture]
    public sealed class AuthenticationTimeoutTests
    {
        /// <summary>
        /// Ensures delayed async lookup returns transient failure for AUTHINFO.
        /// </summary>
        /// <returns>A task that completes when the assertion is evaluated.</returns>
        [Test]
        public async Task ValidatePasswordAsync_DelayedLookup_ReturnsTransientFailure()
        {
            DelayedUserRecordStore store = new DelayedUserRecordStore(TimeSpan.FromMilliseconds(50));
            MySqlNntpCredentialValidator validator = CreateValidator(store);

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
        /// Ensures delayed async lookup completes within bounded wall-clock time.
        /// </summary>
        /// <returns>A task that completes when the assertion is evaluated.</returns>
        [Test]
        public async Task ValidatePasswordAsync_DelayedLookup_CompletesWithinBudget()
        {
            DelayedUserRecordStore store = new DelayedUserRecordStore(TimeSpan.FromMilliseconds(200));
            MySqlNntpCredentialValidator validator = CreateValidator(store);
            Stopwatch stopwatch = Stopwatch.StartNew();

            _ = await validator.ValidatePasswordAsync(
                NntpAuthMechanisms.AuthInfoUserPass,
                "user1",
                "secret",
                IPAddress.Loopback,
                true,
                CancellationToken.None).ConfigureAwait(false);

            stopwatch.Stop();
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(3)));
        }

        /// <summary>
        /// Ensures SCRAM store throws transient exception on timeout-shaped failures.
        /// </summary>
        [Test]
        public void TryGetScramCredential_QueryTimeout_ThrowsTransientException()
        {
            MySqlScramCredentialStore store = new MySqlScramCredentialStore(
                new TimeoutUserRecordStore(),
                NullLogger<MySqlScramCredentialStore>.Instance);

            Assert.Throws<NntpCredentialStoreTransientException>(() =>
            {
                _ = store.TryGetScramCredential("user1", out _);
            });
        }

        /// <summary>
        /// Ensures pool-pressure simulation on AUTHINFO returns transient failure.
        /// </summary>
        /// <returns>A task that completes when the assertion is evaluated.</returns>
        [Test]
        public async Task ValidatePasswordAsync_PoolPressure_ReturnsTransientFailure()
        {
            PoolPressureUserRecordStore store = new PoolPressureUserRecordStore();
            MySqlNntpCredentialValidator validator = CreateValidator(store);

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
        /// Ensures pool-pressure simulation on SCRAM lookup throws transient exception.
        /// </summary>
        [Test]
        public void TryGetScramCredential_PoolPressure_ThrowsTransientException()
        {
            MySqlScramCredentialStore store = new MySqlScramCredentialStore(
                new PoolPressureUserRecordStore(),
                NullLogger<MySqlScramCredentialStore>.Instance);

            Assert.Throws<NntpCredentialStoreTransientException>(() =>
            {
                _ = store.TryGetScramCredential("user1", out _);
            });
        }

        /// <summary>
        /// Ensures CRAM store throws transient exception on timeout-shaped failures.
        /// </summary>
        [Test]
        public void TryGetCramSecret_QueryTimeout_ThrowsTransientException()
        {
            MySqlCramMd5CredentialStore store = new MySqlCramMd5CredentialStore(
                new TimeoutUserRecordStore(),
                NullLogger<MySqlCramMd5CredentialStore>.Instance);

            Assert.Throws<NntpCredentialStoreTransientException>(() =>
            {
                _ = store.TryGetCramSecret("user1", out _);
            });
        }

        /// <summary>
        /// Builds a validator for timeout tests.
        /// </summary>
        /// <param name="store">Backing record store.</param>
        /// <returns>Configured validator.</returns>
        private static MySqlNntpCredentialValidator CreateValidator(INntpUserRecordStore store)
        {
            Blake3AccountKeyNormalizer normalizer = new Blake3AccountKeyNormalizer();
            MySqlUserRecordCache cache = new MySqlUserRecordCache(TimeSpan.FromMinutes(1));
            AuthMySqlMetrics metrics = new AuthMySqlMetrics();
            return new MySqlNntpCredentialValidator(
                store,
                normalizer,
                cache,
                metrics,
                NullLogger<MySqlNntpCredentialValidator>.Instance);
        }

        /// <summary>
        /// Store that delays then throws, simulating a slow query timeout.
        /// </summary>
        private sealed class DelayedUserRecordStore : INntpUserRecordStore
        {
            /// <summary>
            /// Simulated query delay.
            /// </summary>
            private readonly TimeSpan _delay;

            /// <summary>
            /// Initializes a new instance of the <see cref="DelayedUserRecordStore"/> class.
            /// </summary>
            /// <param name="delay">Delay before throwing.</param>
            public DelayedUserRecordStore(TimeSpan delay)
            {
                this._delay = delay;
            }

            /// <inheritdoc />
            public MySqlUserRecord? TryGetUser(string accountName)
            {
                _ = accountName;
                Thread.Sleep(this._delay);
                throw new TimeoutException("Simulated query timeout.");
            }

            /// <inheritdoc />
            public async Task<MySqlUserRecord?> TryGetUserAsync(string accountName, CancellationToken cancellationToken)
            {
                _ = accountName;
                await Task.Delay(this._delay, cancellationToken).ConfigureAwait(false);
                throw new TimeoutException("Simulated query timeout.");
            }
        }

        /// <summary>
        /// Store that simulates connection-pool exhaustion by blocking until a wait timeout.
        /// </summary>
        private sealed class PoolPressureUserRecordStore : INntpUserRecordStore
        {
            /// <summary>
            /// Gate held to simulate an exhausted pool (never released).
            /// </summary>
            private readonly SemaphoreSlim _gate = new(0, 1);

            /// <inheritdoc />
            public MySqlUserRecord? TryGetUser(string accountName)
            {
                _ = accountName;
                if (!this._gate.Wait(TimeSpan.FromMilliseconds(100)))
                {
                    throw new TimeoutException("Timed out waiting for a pooled connection.");
                }

                return null;
            }

            /// <inheritdoc />
            public async Task<MySqlUserRecord?> TryGetUserAsync(string accountName, CancellationToken cancellationToken)
            {
                _ = accountName;
                if (!await this._gate.WaitAsync(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false))
                {
                    throw new TimeoutException("Timed out waiting for a pooled connection.");
                }

                return null;
            }
        }

        /// <summary>
        /// Store that throws a MySQL timeout-shaped exception immediately.
        /// </summary>
        private sealed class TimeoutUserRecordStore : INntpUserRecordStore
        {
            /// <inheritdoc />
            public MySqlUserRecord? TryGetUser(string accountName)
            {
                _ = accountName;
                throw new TimeoutException("Query execution was interrupted (timeout).");
            }

            /// <inheritdoc />
            public Task<MySqlUserRecord?> TryGetUserAsync(string accountName, CancellationToken cancellationToken)
            {
                _ = accountName;
                _ = cancellationToken;
                throw new TimeoutException("Query execution was interrupted (timeout).");
            }
        }
    }
}
