// <copyright file="MySqlUserRecordCacheProtection.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: encrypts and decrypts cached user records so secrets are not stored in cleartext in memory.

using System.Security.Cryptography;
using System.Text;

namespace Vector.NNTP.Auth.MySql.Records
{
    /// <summary>
    /// Serializes <see cref="MySqlUserRecord"/> snapshots and protects them with AES-256-GCM for in-memory cache storage.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Role:</b> Owned exclusively by <see cref="MySqlUserRecordCache"/> (one protector instance per cache). Successful
    /// authentication paths call <see cref="Protect"/> before entries are stored;
    /// <see cref="MySqlUserRecordCache.TryGet"/> calls
    /// <see cref="Unprotect"/> on read. Not used by short-lived <see cref="MySqlUserRecordSaslCache"/> staging.
    /// </para>
    /// <para>
    /// <b>Key material:</b> A fresh 256-bit AES key is generated with
    /// <see cref="System.Security.Cryptography.RandomNumberGenerator"/> in the
    /// constructor and retained in <see cref="_key"/> for the protector lifetime. The key never leaves the process and is
    /// not persisted; process restart or a new <see cref="MySqlUserRecordCache"/> instance invalidates prior payloads.
    /// </para>
    /// <para>
    /// <b>On-wire layout (protected blob):</b> <c>[12-byte nonce][16-byte GCM tag][ciphertext]</c>, where ciphertext
    /// encrypts the versioned binary record from <see cref="Serialize"/>. Each <see cref="Protect"/> call draws a new random
    /// nonce.
    /// </para>
    /// <para>
    /// <b>Plaintext hygiene:</b> Serialized bytes are zeroed in a <c>finally</c> block after encryption and after
    /// decryption/deserialization. This reduces cleartext password and SCRAM material lifetime in managed buffers but does
    /// not guarantee immediate erasure across all runtime copies.
    /// </para>
    /// <para>
    /// <b>Failure handling:</b> <see cref="Unprotect"/> returns <see langword="null"/> on authentication failure,
    /// truncation, unsupported format version, or malformed inner payload — never throws to callers. Tampered cache entries
    /// are removed by <see cref="MySqlUserRecordCache.TryGet"/>.
    /// </para>
    /// <para>
    /// <b>Scope:</b> Defense-in-depth for a short TTL burst cache only; not a substitute for TLS on the wire or encryption
    /// at rest in MySQL.
    /// </para>
    /// <para>
    /// <b>Thread safety:</b> Instance methods may run concurrently from <see cref="MySqlUserRecordCache"/> dictionary
    /// operations; <see cref="_key"/> is immutable after construction and each encrypt/decrypt uses a fresh
    /// <see cref="AesGcm"/> instance.
    /// </para>
    /// </remarks>
    internal sealed class MySqlUserRecordCacheProtection
    {
        /// <summary>
        /// Leading byte of the inner serialized payload identifying the field layout version.
        /// </summary>
        /// <value><c>1</c>.</value>
        /// <remarks>
        /// <see cref="Deserialize"/> rejects any other version with <see cref="InvalidDataException"/>, which
        /// <see cref="Unprotect"/> converts to <see langword="null"/>.
        /// </remarks>
        private const byte FormatVersion = 1;

        /// <summary>
        /// AES-256 key size in bytes for <see cref="_key"/>.
        /// </summary>
        /// <value><c>32</c>.</value>
        private const int KeySizeBytes = 32;

        /// <summary>
        /// GCM nonce length in bytes passed to <see cref="AesGcm"/> encrypt and decrypt operations.
        /// </summary>
        /// <value><c>12</c> (96-bit nonce).</value>
        private const int NonceSizeBytes = 12;

        /// <summary>
        /// GCM authentication tag length in bytes.
        /// </summary>
        /// <value><c>16</c> (128-bit tag).</value>
        /// <remarks>Constructor passes this value as <c>tagSizeInBytes</c> to <see cref="AesGcm"/>.</remarks>
        private const int TagSizeBytes = 16;

        /// <summary>
        /// Per-cache-instance AES-256 encryption key generated at construction.
        /// </summary>
        /// <remarks>
        /// Never exported or logged. Payloads encrypted by a different protector instance (different key) fail
        /// authentication in <see cref="TryDecrypt"/>.
        /// </remarks>
        private readonly byte[] _key;

        /// <summary>
        /// Initializes a new instance with a cryptographically random AES-256 key.
        /// </summary>
        /// <remarks>
        /// Called once per <see cref="MySqlUserRecordCache"/> instance. Key material is not rotated during cache TTL
        /// operation.
        /// </remarks>
        internal MySqlUserRecordCacheProtection()
        {
            _key = new byte[KeySizeBytes];
            RandomNumberGenerator.Fill(_key);
        }

        /// <summary>
        /// Serializes and encrypts a validated user record for storage in <see cref="MySqlUserRecordCache"/>.
        /// </summary>
        /// <param name="record">
        /// Post-authentication <see cref="MySqlUserRecord"/> snapshot. Must not be <see langword="null"/>.
        /// </param>
        /// <returns>
        /// Protected byte array laid out as nonce, authentication tag, then ciphertext (see class remarks).
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="record"/> is <see langword="null"/>.
        /// </exception>
        /// <remarks>
        /// <para>
        /// Invoked from <see cref="MySqlUserRecordCache.Put"/> after successful credential validation. Inner plaintext is
        /// zeroed after <see cref="Encrypt"/> even when encryption succeeds.
        /// </para>
        /// <para>Never throws for valid records beyond null argument validation.</para>
        /// </remarks>
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
        /// Authenticates, decrypts, and deserializes a protected cache payload.
        /// </summary>
        /// <param name="protectedPayload">
        /// Bytes previously returned from <see cref="Protect"/> for the same protector instance.
        /// </param>
        /// <returns>
        /// A materialised <see cref="MySqlUserRecord"/> when decryption and deserialization succeed; otherwise
        /// <see langword="null"/> (tampered, truncated, wrong key, unsupported version, or corrupt inner encoding).
        /// </returns>
        /// <remarks>
        /// <para>
        /// <see cref="TryDecrypt"/> returns <see langword="null"/> on GCM authentication failure. Deserialization faults
        /// (<see cref="ArgumentException"/> from <see cref="MySqlUserRecord"/> validation,
        /// <see cref="EndOfStreamException"/>, <see cref="InvalidDataException"/>) are also converted to
        /// <see langword="null"/> without propagating.
        /// </para>
        /// <para>Decrypted plaintext is always zeroed in a <c>finally</c> block.</para>
        /// </remarks>
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
        /// Writes a versioned binary snapshot of all <see cref="MySqlUserRecord"/> fields to a UTF-8 <see cref="BinaryWriter"/>
        /// stream.
        /// </summary>
        /// <param name="record">Record to serialize. Must not be <see langword="null"/>.</param>
        /// <returns>Inner plaintext bytes encrypted by <see cref="Encrypt"/>.</returns>
        /// <remarks>
        /// <para><b>Field order after <see cref="FormatVersion"/>:</b></para>
        /// <list type="number">
        /// <item><description><see cref="MySqlUserRecord.AccountName"/> (length-prefixed UTF-8 string).</description></item>
        /// <item><description><see cref="MySqlUserRecord.AccountPassword"/> (length-prefixed UTF-8 string).</description></item>
        /// <item><description><see cref="MySqlUserRecord.AllowAuthPlain"/> (<see cref="bool"/>).</description></item>
        /// <item><description><see cref="MySqlUserRecord.AllowAuthScram256"/> (<see cref="bool"/>).</description></item>
        /// <item><description>SCRAM salt, iterations, stored key, server key (length-prefixed byte arrays + <see cref="int"/>).</description></item>
        /// <item><description>Account type (<see cref="char"/>), rate/byte/session/src-IP limits, <see cref="MySqlUserRecord.IsEnabled"/>.</description></item>
        /// <item><description><see cref="MySqlUserRecord.CustomerId"/> (length-prefixed UTF-8 string).</description></item>
        /// </list>
        /// </remarks>
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
        /// Reconstructs a <see cref="MySqlUserRecord"/> from a versioned inner plaintext payload.
        /// </summary>
        /// <param name="payload">Decrypted bytes from <see cref="TryDecrypt"/>.</param>
        /// <returns>Materialised user record passing <see cref="MySqlUserRecord"/> constructor validation.</returns>
        /// <exception cref="InvalidDataException">
        /// Thrown when the format version byte is not <see cref="FormatVersion"/> or length prefixes are negative.
        /// </exception>
        /// <exception cref="EndOfStreamException">Thrown when the payload ends before a declared field is fully read.</exception>
        /// <exception cref="ArgumentException">
        /// Thrown when reconstructed values violate <see cref="MySqlUserRecord"/> invariants (for example empty account name).
        /// </exception>
        /// <remarks>
        /// Mirrors field order documented on <see cref="Serialize"/>. Allocates a copy of <paramref name="payload"/> for
        /// <see cref="BinaryReader"/> consumption.
        /// </remarks>
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
        /// Encrypts inner plaintext with AES-256-GCM using <see cref="_key"/> and a random nonce.
        /// </summary>
        /// <param name="plaintext">Serialized record bytes from <see cref="Serialize"/>.</param>
        /// <returns>
        /// Concatenated buffer: <c>nonce || tag || ciphertext</c> with lengths
        /// <see cref="NonceSizeBytes"/>, <see cref="TagSizeBytes"/>, and <paramref name="plaintext"/>.Length respectively.
        /// </returns>
        /// <remarks>
        /// A new <see cref="AesGcm"/> instance is created per call. Ciphertext length equals plaintext length (no padding
        /// beyond GCM semantics).
        /// </remarks>
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
        /// Authenticates and decrypts a protected payload with AES-256-GCM.
        /// </summary>
        /// <param name="protectedPayload">Outer blob laid out as nonce, tag, then ciphertext.</param>
        /// <returns>
        /// Inner plaintext bytes on successful authentication; <see langword="null"/> when the payload is too short or GCM
        /// verification fails.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Returns <see langword="null"/> without throwing when <paramref name="protectedPayload"/>.Length is less than
        /// <see cref="NonceSizeBytes"/> + <see cref="TagSizeBytes"/>. <see cref="CryptographicException"/> from GCM
        /// decrypt is caught and converted to <see langword="null"/> after zeroing the plaintext
        /// buffer.
        /// </para>
        /// </remarks>
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
        /// Writes a length-prefixed UTF-8 string to the serialization stream.
        /// </summary>
        /// <param name="writer">Active binary writer. Must not be <see langword="null"/>.</param>
        /// <param name="value">String to encode; <see langword="null"/> is treated as empty.</param>
        /// <remarks>
        /// Prefix is a 32-bit signed length followed by raw UTF-8 bytes (not NUL-terminated).
        /// </remarks>
        private static void WriteString(BinaryWriter writer, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        /// <summary>
        /// Reads a length-prefixed UTF-8 string from the serialization stream.
        /// </summary>
        /// <param name="reader">Active binary reader positioned at the length prefix.</param>
        /// <returns>Decoded string (may be empty).</returns>
        /// <exception cref="InvalidDataException">Thrown when the declared length is negative.</exception>
        /// <exception cref="EndOfStreamException">Thrown when fewer than the declared bytes remain in the stream.</exception>
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
        /// Writes a length-prefixed byte array to the serialization stream.
        /// </summary>
        /// <param name="writer">Active binary writer.</param>
        /// <param name="value">Byte payload; empty spans write a zero length prefix only.</param>
        /// <remarks>Prefix is a 32-bit signed length followed by raw bytes when length is positive.</remarks>
        private static void WriteBytes(BinaryWriter writer, ReadOnlyMemory<byte> value)
        {
            writer.Write(value.Length);
            if (!value.IsEmpty)
            {
                writer.Write(value.Span);
            }
        }

        /// <summary>
        /// Reads a length-prefixed byte array from the serialization stream.
        /// </summary>
        /// <param name="reader">Active binary reader positioned at the length prefix.</param>
        /// <returns><see cref="ReadOnlyMemory{T}.Empty"/> when length is zero; otherwise a new byte array copy.</returns>
        /// <exception cref="InvalidDataException">Thrown when the declared length is negative.</exception>
        /// <exception cref="EndOfStreamException">Thrown when fewer than the declared bytes remain in the stream.</exception>
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
