// <copyright file="CramMd5MechanismTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: unit tests for CRAM-MD5 challenge verification.

using System.Security.Cryptography;
using System.Text;
using Vector.NNTP.Sockets.Authentication.Sasl;

namespace Vector.NNTP.Tests.Sockets
{
    /// <summary>
    /// Unit tests for <see cref="CramMd5Mechanism"/> challenge/response verification.
    /// </summary>
    [TestFixture]
    public sealed class CramMd5MechanismTests
    {
        /// <summary>
        /// Verifies that the server validates a correct CRAM-MD5 response against the base64-decoded challenge bytes.
        /// </summary>
        [Test]
        public void Verify_WhenResponseMatchesDecodedChallenge_ReturnsTrue()
        {
            string username = "a";
            byte[] secret = Encoding.UTF8.GetBytes("a");
            byte[] challengeBytes = Encoding.ASCII.GetBytes("test-challenge");
            string challenge = Convert.ToBase64String(challengeBytes);

#pragma warning disable CA5351 // CRAM-MD5 requires HMAC-MD5 per RFC 2195
            using HMACMD5 hmac = new(secret);
#pragma warning restore CA5351
            byte[] digest = hmac.ComputeHash(challengeBytes);
            string hex = Convert.ToHexString(digest).ToLowerInvariant();
            string response = $"{username} {hex}";

            Assert.That(CramMd5Mechanism.Verify(username, response, challenge, secret), Is.True);
        }

        /// <summary>
        /// Verifies that invalid base64 challenges do not validate.
        /// </summary>
        [Test]
        public void Verify_WhenChallengeNotBase64_ReturnsFalse()
        {
            byte[] secret = Encoding.UTF8.GetBytes("a");
            Assert.That(CramMd5Mechanism.Verify("a", "a deadbeef", "not-base64", secret), Is.False);
        }
    }
}
