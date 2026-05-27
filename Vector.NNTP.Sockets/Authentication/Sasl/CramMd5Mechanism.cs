// <copyright file="CramMd5Mechanism.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: CRAM-MD5 challenge/response verification (RFC 2195).

namespace Vector.NNTP.Sockets.Authentication.Sasl
{
    /// <summary>
    /// CRAM-MD5 server challenge and response verification.
    /// </summary>
    internal static class CramMd5Mechanism
    {
        /// <summary>
        /// Creates a random challenge string for 383 continuation.
        /// </summary>
        /// <returns>Base64-encoded challenge bytes.</returns>
        internal static string CreateChallenge()
        {
            return Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        }

        /// <summary>
        /// Verifies a CRAM-MD5 response against the shared secret.
        /// </summary>
        /// <param name="username">Expected username from earlier SASL steps or response.</param>
        /// <param name="response">Client response (username + space + hex HMAC).</param>
        /// <param name="challenge">Challenge sent to the client.</param>
        /// <param name="secret">Shared secret bytes.</param>
        /// <returns><see langword="true"/> when the response matches.</returns>
        internal static bool Verify(string username, string response, string challenge, ReadOnlySpan<byte> secret)
        {
            int space = response.IndexOf(' ');
            if (space <= 0)
            {
                return false;
            }

            string respUser = response[..space];
            if (!string.Equals(respUser, username, StringComparison.Ordinal))
            {
                return false;
            }

            string hex = response[(space + 1)..];
            byte[] challengeBytes;
            try
            {
                challengeBytes = Convert.FromBase64String(challenge);
            }
            catch (FormatException)
            {
                return false;
            }

            byte[] expected = HmacMd5(secret, challengeBytes);
            string expectedHex = Convert.ToHexString(expected).ToLowerInvariant();
            return string.Equals(hex, expectedHex, StringComparison.OrdinalIgnoreCase);
        }

        private static byte[] HmacMd5(ReadOnlySpan<byte> key, ReadOnlySpan<byte> data)
        {
#pragma warning disable CA5351 // CRAM-MD5 requires HMAC-MD5 per RFC 2195
            using HMACMD5 hmac = new(key.ToArray());
#pragma warning restore CA5351
            return hmac.ComputeHash(data.ToArray());
        }
    }
}
