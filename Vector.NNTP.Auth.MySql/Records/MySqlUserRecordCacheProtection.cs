// <copyright file="MySqlUserRecordCacheProtection.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: encrypts and decrypts cached user records so secrets are not stored in cleartext in memory.

using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Vector.NNTP.Auth.MySql.Records
{
    /// <summary>
    /// Serializes <see cref="MySqlUserRecord"/> snapshots and protects them with AES-256-GCM for in-memory cache storage.
    /// </summary>
    /// <remarks>
    /// <para><b>Key material:</b> A per-cache-instance 256-bit key is generated at construction and never leaves the process.</para>
    /// <para><b>Scope:</b> Short-lived successful-authentication cache only; not a substitute for TLS or database encryption.</para>
    /// </remarks>
    internal sealed class MySqlUserRecordCacheProtection
    {
        /// <summary>
        /// Serialized payload format version.
        /// </summary>
        private const byte FormatVersion = 1;

        /// <summary>
        /// AES-256 key size in bytes.
        /// </summary>
        private const int KeySizeBytes = 32;

        /// <summary>
        /// GCM nonce size in bytes.
        /// </summary>
        private const int NonceSizeBytes = 12;

        /// <summary>
        /// GCM authentication tag size in bytes.
        /// </summary>
        private const int TagSizeBytes = 16;

        /// <summary>
        /// Per-cache AES-256 key.
        /// </summary>
        private readonly byte[] _key;

        /// <summary>
        /// Initializes a new instance of the <see cref="MySqlUserRecordCacheProtection"/> class with a random AES key.
        /// </summary>
        internal MySqlUserRecordCacheProtection()
        {
            _key = new byte[KeySizeBytes];
            RandomNumberGenerator.Fill(_key);
        }

        /// <summary>
        /// Serializes and encrypts a user record for cache storage.
        /// </summary>
        /// <param name="record">Validated user record snapshot.</param>
        /// <returns>Nonce, authentication tag, and ciphertext suitable for cache storage.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="record"/> is null.</exception>
        internal byte[] Protect(MySqlUserRecord record)
        {
            ArgumentNullException.ThrowIfNull(record);
            byte[] plaintext = Serialize(record);
            try
            {
                return Encrypt(plaintext);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }

        /// <summary>
        /// Decrypts and deserializes a protected cache payload.
        /// </summary>
        /// <param name="protectedPayload">Encrypted cache bytes.</param>
        /// <returns>Materialised user record, or <see langword="null"/> when decryption or deserialization fails.</returns>
        internal MySqlUserRecord? Unprotect(ReadOnlySpan<byte> protectedPayload)
        {
            byte[]? plaintext = TryDecrypt(protectedPayload);
            if (plaintext is null)
            {
                return null;
            }

            try
            {
                return Deserialize(plaintext);
            }
            catch (Exception ex) when (ex is ArgumentException or EndOfStreamException or InvalidDataException)
            {
                return null;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }

        /// <summary>
        /// Serializes a user record to a versioned binary payload.
        /// </summary>
        /// <param name="record">Record to serialize.</param>
        /// <returns>Serialized bytes.</returns>
        private static byte[] Serialize(MySqlUserRecord record)
        {
            using MemoryStream stream = new();
            using BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true);
            writer.Write(FormatVersion);
            WriteString(writer, record.AccountName);
            WriteString(writer, record.AccountPassword);
            writer.Write(record.AllowAuthPlain);
            writer.Write(record.AllowAuthScram256);
            WriteBytes(writer, record.ScramSalt);
            writer.Write(record.ScramIterations);
            WriteBytes(writer, record.ScramStoredKey);
            WriteBytes(writer, record.ScramServerKey);
            writer.Write(record.AccountType);
            writer.Write(record.RateLimit);
            writer.Write(record.ByteLimit);
            writer.Write(record.SessionLimit);
            writer.Write(record.SrcIpLimit);
            writer.Write(record.IsEnabled);
            WriteString(writer, record.CustomerId);
            return stream.ToArray();
        }

        /// <summary>
        /// Deserializes a user record from a versioned binary payload.
        /// </summary>
        /// <param name="payload">Serialized bytes.</param>
        /// <returns>Materialised user record.</returns>
        /// <exception cref="InvalidDataException">Thrown when the payload version is unsupported.</exception>
        private static MySqlUserRecord Deserialize(ReadOnlySpan<byte> payload)
        {
            using MemoryStream stream = new(payload.ToArray());
            using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: true);
            byte version = reader.ReadByte();
            if (version != FormatVersion)
            {
                throw new InvalidDataException($"Unsupported cache payload version {version}.");
            }

            string accountName = ReadString(reader);
            string accountPassword = ReadString(reader);
            bool allowAuthPlain = reader.ReadBoolean();
            bool allowAuthScram256 = reader.ReadBoolean();
            ReadOnlyMemory<byte> scramSalt = ReadBytes(reader);
            int scramIterations = reader.ReadInt32();
            ReadOnlyMemory<byte> scramStoredKey = ReadBytes(reader);
            ReadOnlyMemory<byte> scramServerKey = ReadBytes(reader);
            char accountType = reader.ReadChar();
            int rateLimit = reader.ReadInt32();
            long byteLimit = reader.ReadInt64();
            int sessionLimit = reader.ReadInt32();
            int srcIpLimit = reader.ReadInt32();
            bool isEnabled = reader.ReadBoolean();
            string customerId = ReadString(reader);

            return new MySqlUserRecord(
                accountName,
                accountPassword,
                allowAuthPlain,
                allowAuthScram256,
                scramSalt,
                scramIterations,
                scramStoredKey,
                scramServerKey,
                accountType,
                rateLimit,
                byteLimit,
                sessionLimit,
                srcIpLimit,
                isEnabled,
                customerId);
        }

        /// <summary>
        /// Encrypts plaintext with AES-256-GCM.
        /// </summary>
        /// <param name="plaintext">Serialized record bytes.</param>
        /// <returns>Concatenated nonce, tag, and ciphertext.</returns>
        private byte[] Encrypt(ReadOnlySpan<byte> plaintext)
        {
            byte[] nonce = new byte[NonceSizeBytes];
            RandomNumberGenerator.Fill(nonce);
            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[TagSizeBytes];
            using AesGcm aes = new(_key, TagSizeBytes);
            aes.Encrypt(nonce, plaintext, ciphertext, tag);

            byte[] protectedPayload = new byte[NonceSizeBytes + TagSizeBytes + ciphertext.Length];
            nonce.CopyTo(protectedPayload.AsSpan(0, NonceSizeBytes));
            tag.CopyTo(protectedPayload.AsSpan(NonceSizeBytes, TagSizeBytes));
            ciphertext.CopyTo(protectedPayload.AsSpan(NonceSizeBytes + TagSizeBytes));
            return protectedPayload;
        }

        /// <summary>
        /// Decrypts a protected payload with AES-256-GCM.
        /// </summary>
        /// <param name="protectedPayload">Concatenated nonce, tag, and ciphertext.</param>
        /// <returns>Plaintext bytes, or <see langword="null"/> when authentication fails.</returns>
        private byte[]? TryDecrypt(ReadOnlySpan<byte> protectedPayload)
        {
            int minimumLength = NonceSizeBytes + TagSizeBytes;
            if (protectedPayload.Length < minimumLength)
            {
                return null;
            }

            ReadOnlySpan<byte> nonce = protectedPayload[..NonceSizeBytes];
            ReadOnlySpan<byte> tag = protectedPayload.Slice(NonceSizeBytes, TagSizeBytes);
            ReadOnlySpan<byte> ciphertext = protectedPayload[(NonceSizeBytes + TagSizeBytes)..];
            byte[] plaintext = new byte[ciphertext.Length];
            try
            {
                using AesGcm aes = new(_key, TagSizeBytes);
                aes.Decrypt(nonce, ciphertext, tag, plaintext);
                return plaintext;
            }
            catch (CryptographicException)
            {
                CryptographicOperations.ZeroMemory(plaintext);
                return null;
            }
        }

        /// <summary>
        /// Writes a length-prefixed UTF-8 string.
        /// </summary>
        /// <param name="writer">Binary writer.</param>
        /// <param name="value">String value.</param>
        private static void WriteString(BinaryWriter writer, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        /// <summary>
        /// Reads a length-prefixed UTF-8 string.
        /// </summary>
        /// <param name="reader">Binary reader.</param>
        /// <returns>Decoded string.</returns>
        private static string ReadString(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            if (length < 0)
            {
                throw new InvalidDataException("Negative string length in cache payload.");
            }

            byte[] bytes = reader.ReadBytes(length);
            return bytes.Length != length
                ? throw new EndOfStreamException("Unexpected end of cache payload while reading string.")
                : Encoding.UTF8.GetString(bytes);
        }

        /// <summary>
        /// Writes a length-prefixed byte array.
        /// </summary>
        /// <param name="writer">Binary writer.</param>
        /// <param name="value">Byte payload.</param>
        private static void WriteBytes(BinaryWriter writer, ReadOnlyMemory<byte> value)
        {
            writer.Write(value.Length);
            if (!value.IsEmpty)
            {
                writer.Write(value.Span);
            }
        }

        /// <summary>
        /// Reads a length-prefixed byte array.
        /// </summary>
        /// <param name="reader">Binary reader.</param>
        /// <returns>Byte payload.</returns>
        private static ReadOnlyMemory<byte> ReadBytes(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            if (length < 0)
            {
                throw new InvalidDataException("Negative byte length in cache payload.");
            }

            if (length == 0)
            {
                return ReadOnlyMemory<byte>.Empty;
            }

            byte[] bytes = reader.ReadBytes(length);
            return bytes.Length != length ? throw new EndOfStreamException("Unexpected end of cache payload while reading bytes.") : (ReadOnlyMemory<byte>)bytes;
        }
    }
}
