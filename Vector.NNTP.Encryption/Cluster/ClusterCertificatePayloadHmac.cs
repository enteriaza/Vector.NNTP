// <copyright file="ClusterCertificatePayloadHmac.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Vector.NNTP.Encryption.Cluster
{
    /// <summary>
    /// HMAC-SHA256 over a canonical UTF-8 representation of <see cref="ClusterCertificatePayload"/> fields.
    /// </summary>
    internal static class ClusterCertificatePayloadHmac
    {
        /// <summary>
        /// Computes a hex-encoded HMAC-SHA256 of the payload fields used for cluster fanout integrity.
        /// </summary>
        /// <param name="payload">Payload to sign.</param>
        /// <param name="secretUtf8">UTF-8 signing secret bytes.</param>
        /// <returns>Uppercase hex HMAC digest.</returns>
        public static string ComputeSignature(ClusterCertificatePayload payload, ReadOnlySpan<byte> secretUtf8)
        {
            byte[] signable = BuildSignableUtf8(payload);
            Span<byte> mac = stackalloc byte[32];
            _ = HMACSHA256.HashData(secretUtf8, signable, mac);
            return Convert.ToHexString(mac);
        }

        /// <summary>
        /// Returns true when <paramref name="signatureHex"/> matches the HMAC for <paramref name="payload"/>.
        /// </summary>
        /// <param name="payload">Payload to verify.</param>
        /// <param name="secretUtf8">UTF-8 signing secret bytes.</param>
        /// <param name="signatureHex">Wire signature to compare.</param>
        /// <returns><see langword="true"/> when the signature is valid.</returns>
        public static bool IsSignatureValid(ClusterCertificatePayload payload, ReadOnlySpan<byte> secretUtf8, string? signatureHex)
        {
            if (string.IsNullOrEmpty(signatureHex) || signatureHex.Length != 64)
                return false;

            byte[] expected;
            try
            {
                expected = Convert.FromHexString(signatureHex);
            }
            catch (FormatException)
            {
                return false;
            }

            if (expected.Length != 32)
                return false;

            byte[] signable = BuildSignableUtf8(payload);
            Span<byte> mac = stackalloc byte[32];
            _ = HMACSHA256.HashData(secretUtf8, signable, mac);
            return CryptographicOperations.FixedTimeEquals(mac, expected);
        }

        /// <summary>
        /// Builds a UTF-8 encoded byte array representing the signable payload from the specified cluster certificate
        /// data.
        /// </summary>
        /// <param name="p">The cluster certificate payload containing the data to encode.</param>
        /// <returns>A byte array containing the UTF-8 encoded signable payload.</returns>
        private static byte[] BuildSignableUtf8(ClusterCertificatePayload p)
        {
            int estimated = 128 + (p.PfxBase64?.Length ?? 0) + ((p.Sha256Thumbprint?.Length ?? 0) * 2);
            foreach (string d in p.Domains ?? [])
                estimated += d.Length;

            using MemoryStream ms = new(Math.Max(256, estimated));
            WriteLine(ms, p.SignatureVersion.ToString(CultureInfo.InvariantCulture));
            WriteLine(ms, p.Epoch.ToString(CultureInfo.InvariantCulture));
            WriteLine(ms, p.Sha256Thumbprint ?? string.Empty);
            WriteLine(ms, p.NotAfterUtcTicks.ToString(CultureInfo.InvariantCulture));
            WriteLine(ms, p.IssuedAtUtcTicks.ToString(CultureInfo.InvariantCulture));

            string[] domains = p.Domains ?? [];
            string[] sorted = new string[domains.Length];
            Array.Copy(domains, sorted, domains.Length);
            Array.Sort(sorted, StringComparer.OrdinalIgnoreCase);
            WriteLine(ms, string.Join('\u001e', sorted));
            WriteLine(ms, p.PfxBase64 ?? string.Empty);
            return ms.ToArray();
        }

        private static void WriteLine(MemoryStream ms, string line)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(line);
            ms.Write(utf8);
            ms.WriteByte((byte)'\n');
        }
    }
}
