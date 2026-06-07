// <copyright file="HistoryKeyEncoder.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Blake3;

namespace Vector.NNTP.HistoryDB.Encoding
{
    /// <summary>
    /// Derives 32-byte BLAKE3 digests from message-id strings for history keys and spool paths.
    /// </summary>
    public static class HistoryKeyEncoder
    {
        /// <summary>
        /// Size of a history digest key in bytes.
        /// </summary>
        public const int DigestLength = 32;

        /// <summary>
        /// Size of a lowercase hexadecimal digest string in characters.
        /// </summary>
        public const int DigestHexLength = 64;

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

        /// <summary>
        /// Encodes the BLAKE3 digest of <paramref name="messageId"/> as lowercase hexadecimal into <paramref name="destination"/>.
        /// </summary>
        /// <param name="messageId">Message-id text (UTF-8 encoded).</param>
        /// <param name="destination">Destination span (at least <see cref="DigestHexLength"/> characters).</param>
        /// <returns><see langword="true"/> when the hex string was written.</returns>
        public static bool TryEncodeHexLower(string messageId, Span<char> destination)
        {
            if (destination.Length < DigestHexLength)
            {
                return false;
            }

            Span<byte> digest = stackalloc byte[DigestLength];
            if (!TryComputeDigest(messageId, digest))
            {
                return false;
            }

            for (int i = 0; i < DigestLength; i++)
            {
                byte b = digest[i];
                destination[(i * 2)] = GetHexChar(b >> 4);
                destination[(i * 2) + 1] = GetHexChar(b & 0x0F);
            }

            return true;
        }

        /// <summary>
        /// Encodes the BLAKE3 digest of <paramref name="messageId"/> as lowercase hexadecimal.
        /// </summary>
        /// <param name="messageId">Message-id text (UTF-8 encoded).</param>
        /// <returns>64-character lowercase hex digest.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="messageId"/> cannot be encoded.</exception>
        public static string EncodeHexLower(string messageId)
        {
            Span<char> hex = stackalloc char[DigestHexLength];
            if (!TryEncodeHexLower(messageId, hex))
            {
                throw new ArgumentException("Message-id cannot be encoded to a history digest.", nameof(messageId));
            }

            return new string(hex);
        }

        /// <summary>
        /// Maps a 4-bit nibble to a lowercase hexadecimal character.
        /// </summary>
        /// <param name="nibble">Four-bit value.</param>
        /// <returns>Lowercase hex character.</returns>
        private static char GetHexChar(int nibble)
        {
            return (char)(nibble < 10 ? '0' + nibble : 'a' + (nibble - 10));
        }
    }
}
