// <copyright file="ScramMechanismTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: unit tests for SCRAM server verification.

using System.Security.Cryptography;
using System.Text;
using Vector.NNTP.Sockets.Authentication;
using Vector.NNTP.Sockets.Authentication.Sasl;

namespace Vector.NNTP.Tests.Sockets
{
    /// <summary>
    /// Unit tests for <see cref="ScramMechanism"/> using an end-to-end derived credential.
    /// </summary>
    [TestFixture]
    public sealed class ScramMechanismTests
    {
        /// <summary>
        /// Verifies that a client-final proof derived from the same stored credentials validates successfully.
        /// </summary>
        [Test]
        public void ScramSha256_WhenKeysMatch_ServerReturnsVerifier()
        {
            string username = "a";
            string password = "a";
            byte[] salt = Encoding.UTF8.GetBytes("salt123");
            int iterations = 4096;

            ScramStoredCredential cred = Derive(password, salt, iterations);

            string clientNonce = "foobar123";
            string clientFirst = $"n,,n={username},r={clientNonce}";
            (ScramMechanism state, string serverFirst) = ScramMechanism.Begin("SCRAM-SHA-256", clientFirst, cred);

            Assert.That(serverFirst, Does.Contain("s=" + Convert.ToBase64String(salt)));
            Assert.That(serverFirst, Does.Contain("i=" + iterations));

            string combinedNonce = GetAttribute(serverFirst, 'r');
            string clientFinalWithoutProof = $"c=biws,r={combinedNonce}";
            string authMessage = $"{GetClientFirstBare(clientFirst)},{serverFirst},{clientFinalWithoutProof}";

            byte[] clientSignature = HmacSha256(cred.StoredKey.Span.ToArray(), Encoding.UTF8.GetBytes(authMessage));
            byte[] clientKey = RecoverClientKey(password, salt, iterations);
            byte[] clientProof = Xor(clientKey, clientSignature);
            string clientFinal = $"{clientFinalWithoutProof},p={Convert.ToBase64String(clientProof)}";

            string? serverFinal = state.TryFinish(clientFinal);
            Assert.That(serverFinal, Is.Not.Null);
            Assert.That(serverFinal, Does.StartWith("v="));
        }

        /// <summary>
        /// Derives a stored SCRAM credential using PBKDF2-HMAC-SHA-256 and RFC5802 key labels.
        /// </summary>
        /// <param name="password">Cleartext password.</param>
        /// <param name="salt">Salt bytes.</param>
        /// <param name="iterations">PBKDF2 iteration count.</param>
        /// <returns>Stored credential containing salt, iteration count, stored key, and server key.</returns>
        private static ScramStoredCredential Derive(string password, byte[] salt, int iterations)
        {
            byte[] saltedPassword = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);

            byte[] clientKey;
            using (HMACSHA256 hmac = new(saltedPassword))
            {
                clientKey = hmac.ComputeHash(Encoding.ASCII.GetBytes("Client Key"));
            }

            byte[] storedKey = SHA256.HashData(clientKey);

            byte[] serverKey;
            using (HMACSHA256 hmac = new(saltedPassword))
            {
                serverKey = hmac.ComputeHash(Encoding.ASCII.GetBytes("Server Key"));
            }

            return new ScramStoredCredential(salt, iterations, storedKey, serverKey);
        }

        /// <summary>
        /// Recomputes the SCRAM client key from password, salt, and iterations.
        /// </summary>
        /// <param name="password">Cleartext password.</param>
        /// <param name="salt">Salt bytes.</param>
        /// <param name="iterations">PBKDF2 iteration count.</param>
        /// <returns>Client key bytes.</returns>
        private static byte[] RecoverClientKey(string password, byte[] salt, int iterations)
        {
            byte[] saltedPassword = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);
            using HMACSHA256 hmac = new(saltedPassword);
            return hmac.ComputeHash(Encoding.ASCII.GetBytes("Client Key"));
        }

        /// <summary>
        /// Computes HMAC-SHA-256 over the supplied data.
        /// </summary>
        /// <param name="key">HMAC key bytes.</param>
        /// <param name="data">Data bytes.</param>
        /// <returns>HMAC digest bytes.</returns>
        private static byte[] HmacSha256(byte[] key, byte[] data)
        {
            using HMACSHA256 hmac = new(key);
            return hmac.ComputeHash(data);
        }

        /// <summary>
        /// XORs two byte arrays up to the shorter length.
        /// </summary>
        /// <param name="a">First byte array.</param>
        /// <param name="b">Second byte array.</param>
        /// <returns>XOR result.</returns>
        private static byte[] Xor(byte[] a, byte[] b)
        {
            int len = Math.Min(a.Length, b.Length);
            byte[] result = new byte[len];
            for (int i = 0; i < len; i++)
            {
                result[i] = (byte)(a[i] ^ b[i]);
            }

            return result;
        }

        /// <summary>
        /// Extracts the client-first-bare portion after the GS2 header.
        /// </summary>
        /// <param name="clientFirst">Client-first SCRAM message including GS2 header.</param>
        /// <returns>Client-first-bare string.</returns>
        private static string GetClientFirstBare(string clientFirst)
        {
            int idx = clientFirst.IndexOf(",,", StringComparison.Ordinal);
            return idx < 0 ? clientFirst : clientFirst[(idx + 2)..];
        }

        /// <summary>
        /// Gets a SCRAM attribute value (<c>k=v</c>) from a comma-delimited message.
        /// </summary>
        /// <param name="message">SCRAM message.</param>
        /// <param name="key">Attribute key.</param>
        /// <returns>Attribute value.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the attribute is missing.</exception>
        private static string GetAttribute(string message, char key)
        {
            foreach (string part in message.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (part.Length >= 2 && part[0] == key && part[1] == '=')
                {
                    return part[2..];
                }
            }

            throw new InvalidOperationException("Attribute not found.");
        }
    }
}
