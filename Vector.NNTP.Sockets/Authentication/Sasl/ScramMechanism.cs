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
        private readonly HashAlgorithmName _hashAlgorithm;
        private readonly ScramStoredCredential _credential;
        private readonly string _gs2Header;
        private readonly string _clientFirstBare;
        private readonly string _serverFirst;
        private readonly string _combinedNonce;

        private ScramMechanism(
            HashAlgorithmName hashAlgorithm,
            ScramStoredCredential credential,
            string gs2Header,
            string clientFirstBare,
            string serverFirst,
            string combinedNonce)
        {
            _hashAlgorithm = hashAlgorithm;
            _credential = credential;
            _gs2Header = gs2Header;
            _clientFirstBare = clientFirstBare;
            _serverFirst = serverFirst;
            _combinedNonce = combinedNonce;
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

            if (!TrySplitClientFirst(clientFirst, out string? gs2Header, out string? clientFirstBare) ||
                !TryParseAttribute(clientFirstBare, 'r', out string? clientNonce) ||
                string.IsNullOrEmpty(clientNonce))
            {
                throw new ArgumentException("Invalid SCRAM client-first message.", nameof(clientFirst));
            }

            string serverNonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));
            string combinedNonce = clientNonce + serverNonce;
            string saltB64 = Convert.ToBase64String(credential.Salt.Span);
            string serverFirst = $"r={combinedNonce},s={saltB64},i={credential.IterationCount}";

            ScramMechanism state = new(hash, credential, gs2Header, clientFirstBare, serverFirst, combinedNonce);
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
                !TryParseAttribute(clientFinal, 'r', out string? combinedNonce) ||
                !TryParseAttribute(clientFinal, 'c', out string? channelBinding))
            {
                return null;
            }

            if (combinedNonce is null || !string.Equals(combinedNonce, _combinedNonce, StringComparison.Ordinal))
            {
                return null;
            }

            if (channelBinding is null ||
                !string.Equals(channelBinding, Convert.ToBase64String(Encoding.ASCII.GetBytes(_gs2Header)), StringComparison.Ordinal))
            {
                return null;
            }

            byte[] clientProof;
            try
            {
                clientProof = Convert.FromBase64String(proofB64);
            }
            catch (FormatException)
            {
                return null;
            }

            string clientFinalWithoutProof = clientFinal.Replace($",p={proofB64}", string.Empty, StringComparison.Ordinal);
            string authMessage = $"{_clientFirstBare},{_serverFirst},{clientFinalWithoutProof}";

            byte[] storedKey = _credential.StoredKey.Span.ToArray();
            byte[] clientSignature = Hmac(storedKey, Encoding.UTF8.GetBytes(authMessage));
            byte[] clientKey = Xor(clientProof, clientSignature);
            byte[] computedStoredKey = Hash(clientKey);
            if (!CryptographicOperations.FixedTimeEquals(computedStoredKey, storedKey))
            {
                return null;
            }

            byte[] serverKey = _credential.ServerKey.Span.ToArray();
            byte[] serverSignature = Hmac(serverKey, Encoding.UTF8.GetBytes(authMessage));
            return $"v={Convert.ToBase64String(serverSignature)}";
        }

        private byte[] Hmac(byte[] key, byte[] data)
        {
            using IncrementalHash hmac = IncrementalHash.CreateHMAC(_hashAlgorithm, key);
            hmac.AppendData(data);
            return hmac.GetHashAndReset();
        }

        private byte[] Hash(byte[] data)
        {
            using IncrementalHash hash = IncrementalHash.CreateHash(_hashAlgorithm);
            hash.AppendData(data);
            return hash.GetHashAndReset();
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

        private static bool TrySplitClientFirst(
            string clientFirst,
            [NotNullWhen(true)] out string? gs2Header,
            [NotNullWhen(true)] out string? clientFirstBare)
        {
            int idx = clientFirst.IndexOf(",,", StringComparison.Ordinal);
            if (idx < 0)
            {
                gs2Header = null;
                clientFirstBare = null;
                return false;
            }

            gs2Header = clientFirst[..(idx + 2)];
            clientFirstBare = clientFirst[(idx + 2)..];
            return !string.IsNullOrEmpty(clientFirstBare);
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
