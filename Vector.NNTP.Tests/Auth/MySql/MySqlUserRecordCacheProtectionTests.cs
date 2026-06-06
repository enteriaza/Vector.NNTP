// <copyright file="MySqlUserRecordCacheProtectionTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Text;
using Vector.NNTP.Auth.MySql.Configuration;
using Vector.NNTP.Auth.MySql.Records;

namespace Vector.NNTP.Tests.Auth.MySql
{
    /// <summary>
    /// Verifies encrypted at-rest storage for successful-authentication cache entries.
    /// </summary>
    [TestFixture]
    public sealed class MySqlUserRecordCacheProtectionTests
    {
        /// <summary>
        /// Ensures protected payloads round-trip back to usable records.
        /// </summary>
        [Test]
        public void ProtectAndUnprotect_RoundTrip_PreservesPasswordAndScramMaterial()
        {
            MySqlUserRecord record = CreateRecord("user1", "s3cret!");
            MySqlUserRecordCacheProtection protection = new MySqlUserRecordCacheProtection();
            byte[] protectedPayload = protection.Protect(record);

            MySqlUserRecord? cached = protection.Unprotect(protectedPayload);

            Assert.That(cached, Is.Not.Null);
            Assert.That(cached!.AccountPassword, Is.EqualTo("s3cret!"));
            Assert.That(cached.ScramStoredKey.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
        }

        /// <summary>
        /// Ensures protected cache bytes do not contain the cleartext password.
        /// </summary>
        [Test]
        public void Protect_DoesNotStoreCleartextPassword()
        {
            MySqlUserRecord record = CreateRecord("user1", "very-secret-password");
            MySqlUserRecordCacheProtection protection = new MySqlUserRecordCacheProtection();
            byte[] protectedPayload = protection.Protect(record);

            Assert.That(
                Encoding.UTF8.GetString(protectedPayload),
                Does.Not.Contain("very-secret-password"));
        }

        /// <summary>
        /// Ensures the cache decrypts entries on read for validator burst deduplication.
        /// </summary>
        [Test]
        public void CachePutAndTryGet_RoundTrip_PreservesPassword()
        {
            MySqlUserRecord record = CreateRecord("user1", "burst-secret");
            MySqlUserRecordCache cache = new MySqlUserRecordCache(TimeSpan.FromSeconds(10));
            byte[] fingerprint = MySqlUserRecordCache.ComputePasswordFingerprint("burst-secret");

            cache.Put("user1", fingerprint, record);

            Assert.That(cache.TryGet("user1", fingerprint, out MySqlUserRecord? cached), Is.True);
            Assert.That(cached!.AccountPassword, Is.EqualTo("burst-secret"));
        }

        /// <summary>
        /// Ensures production cache TTL defaults to ten seconds.
        /// </summary>
        [Test]
        public void MySqlAuthOptions_DefaultAuthCacheTtl_IsTenSeconds()
        {
            MySqlAuthOptions options = new MySqlAuthOptions(
                "Server=127.0.0.1;Database=nntp;User ID=test;Password=test");

            Assert.That(options.AuthCacheTtl, Is.EqualTo(TimeSpan.FromSeconds(10)));
        }

        /// <summary>
        /// Builds a test user record.
        /// </summary>
        /// <param name="username">Account name.</param>
        /// <param name="password">Cleartext password.</param>
        /// <returns>Configured record.</returns>
        private static MySqlUserRecord CreateRecord(string username, string password)
        {
            return new MySqlUserRecord(
                username,
                password,
                allowAuthPlain: true,
                allowAuthScram256: true,
                scramSalt: new byte[] { 9, 8, 7 },
                scramIterations: 4096,
                scramStoredKey: new byte[] { 1, 2, 3 },
                scramServerKey: new byte[] { 4, 5, 6 },
                'B',
                10,
                1000L,
                2,
                1,
                true,
                "00000000-0000-0000-0000-0000000042");
        }
    }
}
