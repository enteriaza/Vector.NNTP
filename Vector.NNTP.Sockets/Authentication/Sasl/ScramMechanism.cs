// <copyright file="ScramMechanism.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: RFC 5802 SCRAM-SHA-256 and SCRAM-SHA-1 server-side exchange.

namespace Vector.NNTP.Sockets.Authentication.Sasl
{
    /// <summary>
    /// Server-side SCRAM exchange state and verification (SCRAM-SHA-256 / SCRAM-SHA-1).
    /// </summary>
    internal sealed class ScramMechanism
    {
        private readonly string _hashName;
        private readonly HashAlgorithmName _hashAlgorithm;
        private readonly ScramStoredCredential _credential;
        private readonly string _clientFirstBare;
        private readonly string _serverNonce;
        private string? _clientProof;

        private ScramMechanism(string hashName, HashAlgorithmName hashAlgorithm, ScramStoredCredential credential, string clientFirstBare, string serverNonce)
        {
            _hashName = hashName;
            _hashAlgorithm = hashAlgorithm;
            _credential = credential;
            _clientFirstBare = clientFirstBare;
            _serverNonce = serverNonce;
        }

        /// <summary>
        /// Begins SCRAM with client-first-message.
        /// </summary>
        /// <param name="mechanism">SCRAM-SHA-256 or SCRAM-SHA-1.</param>
        /// <param name="clientFirst">Client-first message (after GS2 header strip).</param>
        /// <param name="credential">Stored SCRAM credential.</param>
        /// <returns>Server-first message for 383 continuation.</returns>
        internal static (ScramMechanism State, string ServerFirst) Begin(string mechanism, string clientFirst, ScramStoredCredential credential)
        {
            HashAlgorithmName hash = mechanism.Contains("SHA-256", StringComparison.OrdinalIgnoreCase)
                ? HashAlgorithmName.SHA256
                : HashAlgorithmName.SHA1;
            string hashName = hash == HashAlgorithmName.SHA256 ? "SHA-256" : "SHA-1";
            string serverNonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));
            string clientFirstBare = StripGs2Header(clientFirst);
            string serverFirst = $"r={serverNonce},s={Convert.ToBase64String(credential.Salt.Span)},i={credential.IterationCount}";
            ScramMechanism state = new(hashName, hash, credential, clientFirstBare, serverNonce);
            return (state, serverFirst);
        }

        /// <summary>
        /// Processes client-final-message and returns server-final on success.
        /// </summary>
        /// <param name="clientFinal">Client-final message.</param>
        /// <returns>Server-final message or null when proof fails.</returns>
        internal string? TryFinish(string clientFinal)
        {
            if (!TryParseAttribute(clientFinal, 'p', out string? proofB64) ||
                !TryParseAttribute(clientFinal, 'r', out string? combinedNonce))
            {
                return null;
            }

            _clientProof = proofB64;
            if (combinedNonce is null || !combinedNonce.EndsWith(_serverNonce, StringComparison.Ordinal))
            {
                return null;
            }

            if (!TryParseAttribute(_clientFirstBare, 'n', out string? username))
            {
                return null;
            }

            _ = username;
            byte[] clientProof = Convert.FromBase64String(proofB64!);
            byte[] serverSignature = ComputeServerSignature(combinedNonce!);
            byte[] expectedProof = Xor(clientProof, serverSignature);
            byte[] storedKey = _credential.StoredKey.Span.ToArray();
            if (!CryptographicOperations.FixedTimeEquals(expectedProof, storedKey))
            {
                return null;
            }

            byte[] serverKey = _credential.ServerKey.Span.ToArray();
            byte[] serverFinalProof = Hmac(serverKey, Encoding.UTF8.GetBytes($"c=biws,r={combinedNonce}"));
            return $"v={Convert.ToBase64String(serverFinalProof)}";
        }

        private byte[] ComputeServerSignature(string combinedNonce)
        {
            string authMessage = $"{_clientFirstBare},{combinedNonce}";
            byte[] clientKey = Xor(Convert.FromBase64String(_clientProof!), _credential.StoredKey.Span.ToArray());
            return Hmac(clientKey, Encoding.UTF8.GetBytes(authMessage));
        }

        private byte[] Hmac(byte[] key, byte[] data)
        {
            using IncrementalHash hmac = IncrementalHash.CreateHMAC(_hashAlgorithm, key);
            hmac.AppendData(data);
            return hmac.GetHashAndReset();
        }

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

        private static string StripGs2Header(string clientFirst)
        {
            int comma = clientFirst.IndexOf(',', StringComparison.Ordinal);
            return comma >= 0 ? clientFirst[(comma + 1)..] : clientFirst;
        }

        private static bool TryParseAttribute(string message, char key, [NotNullWhen(true)] out string? value)
        {
            foreach (string part in message.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (part.Length >= 2 && part[0] == key && part[1] == '=')
                {
                    value = part[2..];
                    return true;
                }
            }

            value = null;
            return false;
        }
    }
}
