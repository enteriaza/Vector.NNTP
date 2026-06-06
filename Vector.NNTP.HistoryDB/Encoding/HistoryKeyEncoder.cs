// <copyright file="HistoryKeyEncoder.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Blake3;

namespace Vector.NNTP.HistoryDB.Encoding
{
    /// <summary>
    /// Derives 32-byte BLAKE3 digests from message-id strings for history keys.
    /// </summary>
    internal static class HistoryKeyEncoder
    {
        /// <summary>
        /// Size of a history digest key in bytes.
        /// </summary>
        public const int DigestLength = 32;

        /// <summary>
        /// Computes the BLAKE3 digest for a message-id into <paramref name="destination"/>.
        /// </summary>
        /// <param name="messageId">Message-id text (UTF-8 encoded).</param>
        /// <param name="destination">Destination span (at least <see cref="DigestLength"/> bytes).</param>
        /// <returns><see langword="true"/> when the digest was written.</returns>
        public static bool TryComputeDigest(string messageId, Span<byte> destination)
        {
            ArgumentException.ThrowIfNullOrEmpty(messageId);
            if (destination.Length < DigestLength)
            {
                return false;
            }

            int byteCount = System.Text.Encoding.UTF8.GetByteCount(messageId);
            if (byteCount > 2048)
            {
                return false;
            }

            Span<byte> utf8 = stackalloc byte[byteCount];
            int written = System.Text.Encoding.UTF8.GetBytes(messageId, utf8);
            Hash hash = Hasher.Hash(utf8[..written]);
            hash.AsSpan().CopyTo(destination);
            return true;
        }
    }
}
