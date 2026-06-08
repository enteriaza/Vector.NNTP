// <copyright file="SpoolDirectoryUtilities.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: spool directory path normalization and digest fan-out path construction.

using Vector.NNTP.HistoryDB.Encoding;
using Vector.NNTP.Sockets.Configuration;

namespace Vector.NNTP.Articles.Diagnostics
{
    /// <summary>
    /// Resolves transit spool root and article file paths from server options and HistoryDB digest keys.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Role:</b> Centralizes spool path layout for the transit Articles layer. <see cref="Storage.NntpSpoolWriterPump"/>
    /// resolves the spool root once at construction via <see cref="ResolveSpoolDirectory"/> and builds per-article paths
    /// with <see cref="GetArticleFilePath"/> using the precomputed
    /// <see cref="Storage.NntpSpoolWriteItem.MessageIdDigestHex"/> from enqueue. Startup configuration logging uses the
    /// same helpers so operators see canonical absolute paths.
    /// </para>
    /// <para>
    /// <b>On-disk layout:</b> Payload files live under
    /// <c>{spoolRoot}/Incoming/{L1}/{L2}/{digest}</c>, where <c>L1</c> and <c>L2</c> are the
    /// first and second pairs of lowercase hex digits from the 64-character digest (two fan-out levels of
    /// <see cref="FanoutLevelLength"/> characters each). The leaf file name is the full digest string. This bounds
    /// directory fan-out under sustained transit load.
    /// </para>
    /// <para>
    /// <b>Path normalization:</b> <see cref="ResolveSpoolDirectory"/> always returns a canonical absolute path via
    /// <see cref="Path.GetFullPath(string)"/>, collapsing <c>.</c>, <c>..</c>, and redundant separators for both
    /// rooted and relative configured <see cref="NntpServerOptions.SpoolDir"/> values.
    /// </para>
    /// <para><b>Thread safety:</b> Static and stateless; safe for concurrent writer pumps after options are resolved.</para>
    /// </remarks>
    internal static class SpoolDirectoryUtilities
    {
        /// <summary>
        /// Default spool subdirectory name under <see cref="AppContext.BaseDirectory"/> when
        /// <see cref="NntpServerOptions.SpoolDir"/> is unset or whitespace.
        /// </summary>
        /// <value>Literal <c>Spool</c>.</value>
        /// <remarks>
        /// Combined with <see cref="AppContext.BaseDirectory"/> by <see cref="ResolveSpoolDirectory"/> when no explicit
        /// spool directory is configured.
        /// </remarks>
        public const string DefaultSpoolSubdirectory = "Spool";

        /// <summary>
        /// Incoming spool subdirectory containing transit article payload files pending downstream ingestion.
        /// </summary>
        /// <value>Literal <c>Incoming</c>.</value>
        /// <remarks>
        /// Appended to the resolved spool root by <see cref="GetIncomingDirectory"/> and used as the first path segment in
        /// <see cref="GetArticleFilePath"/>.
        /// </remarks>
        public const string IncomingSubdirectory = "Incoming";

        /// <summary>
        /// Number of lowercase hexadecimal characters in each digest fan-out directory name.
        /// </summary>
        /// <value><c>2</c> characters per level (256-way fan-out per directory).</value>
        /// <remarks>
        /// <see cref="GetArticleFilePath"/> slices non-overlapping spans of this length from the start of the digest for
        /// each fan-out directory level before the leaf file name.
        /// </remarks>
        public const int FanoutLevelLength = 2;

        /// <summary>
        /// Declared number of digest-derived fan-out directory levels for prefix-length accounting.
        /// </summary>
        /// <value><c>1</c> in the current constants table.</value>
        /// <remarks>
        /// <para>
        /// Used with <see cref="FanoutLevelLength"/> to compute <see cref="FanoutPrefixHexLength"/>. The path builder in
        /// <see cref="GetArticleFilePath"/> presently hardcodes <b>two</b> fan-out directory levels of
        /// <see cref="FanoutLevelLength"/> characters (layout <c>Incoming/{aa}/{bb}/{digest}</c>), which is what
        /// production code and unit tests expect. This constant does not yet drive a loop in the path builder.
        /// </para>
        /// </remarks>
        public const int FanoutLevelCount = 1;

        /// <summary>
        /// Total lowercase hexadecimal prefix length implied by <see cref="FanoutLevelLength"/> and
        /// <see cref="FanoutLevelCount"/>.
        /// </summary>
        /// <value><c>2</c> (<see cref="FanoutLevelLength"/> × <see cref="FanoutLevelCount"/>).</value>
        /// <remarks>
        /// <para>
        /// Accounting constant for fan-out prefix size. <see cref="GetArticleFilePath"/> consumes four digest characters
        /// across two directory levels before the leaf; a 64-character
        /// <see cref="HistoryKeyEncoder.DigestHexLength"/> digest always supplies sufficient material.
        /// </para>
        /// </remarks>
        public const int FanoutPrefixHexLength = FanoutLevelLength * FanoutLevelCount;

        /// <summary>
        /// Resolves the canonical absolute spool root path from <see cref="NntpServerOptions.SpoolDir"/>.
        /// </summary>
        /// <param name="options">
        /// Server options containing spool configuration. Typically bound from host JSON
        /// (<c>NntpServerOptions.SpoolDir</c>).
        /// </param>
        /// <returns>
        /// A normalized absolute spool root directory path. Unset or whitespace <see cref="NntpServerOptions.SpoolDir"/>
        /// resolves to <see cref="DefaultSpoolSubdirectory"/> under <see cref="AppContext.BaseDirectory"/>.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="options"/> is <see langword="null"/>.
        /// </exception>
        /// <remarks>
        /// <para><b>Relative configured paths</b> are trimmed, combined with <see cref="AppContext.BaseDirectory"/>, then
        /// normalized, so values such as <c>.\Spool</c>, <c>..\Spool</c>, and <c>Spool\..\Incoming</c> collapse to
        /// canonical absolute paths.</para>
        /// <para><b>Rooted configured paths</b> are trimmed and passed through <see cref="Path.GetFullPath(string)"/> so
        /// segments such as <c>..\</c> resolve against the supplied root (for example <c>D:\Spool\..\Data</c> →
        /// <c>D:\Data</c> on Windows).</para>
        /// <para>Does not create directories on disk; callers ensure the root exists before writing.</para>
        /// </remarks>
        public static string ResolveSpoolDirectory(NntpServerOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            if (string.IsNullOrWhiteSpace(options.SpoolDir))
            {
                return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, DefaultSpoolSubdirectory));
            }

            string configured = options.SpoolDir.Trim();
            return Path.IsPathRooted(configured)
                ? Path.GetFullPath(configured)
                : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configured));
        }

        /// <summary>
        /// Gets the incoming article spool directory under the supplied spool root.
        /// </summary>
        /// <param name="spoolDirectory">
        /// Absolute spool root directory, typically from <see cref="ResolveSpoolDirectory"/>. Need not exist yet.
        /// </param>
        /// <returns>
        /// The path <c>{spoolDirectory}/{<see cref="IncomingSubdirectory"/>}</c> using platform directory separators.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="spoolDirectory"/> is <see langword="null"/> or empty.
        /// </exception>
        /// <remarks>
        /// Does not normalize <paramref name="spoolDirectory"/> or verify it is absolute. Does not create the directory.
        /// </remarks>
        public static string GetIncomingDirectory(string spoolDirectory)
        {
            ArgumentException.ThrowIfNullOrEmpty(spoolDirectory);
            return Path.Combine(spoolDirectory, IncomingSubdirectory);
        }

        /// <summary>
        /// Gets the canonical transit article payload file path for a HistoryDB digest key.
        /// </summary>
        /// <param name="spoolDirectory">Spool root directory (absolute path from <see cref="ResolveSpoolDirectory"/>).</param>
        /// <param name="digestHex">
        /// Lowercase hexadecimal digest key with length exactly <see cref="HistoryKeyEncoder.DigestHexLength"/> (64
        /// characters). Must match <see cref="Storage.NntpSpoolWriteItem.MessageIdDigestHex"/> for the article.
        /// </param>
        /// <returns>
        /// A path of the form
        /// <c>{spoolDirectory}/Incoming/{digest[0..2]}/{digest[2..4]}/{digest}</c> using platform separators, where each
        /// fan-out segment is <see cref="FanoutLevelLength"/> lowercase hex characters and the leaf is the full digest.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="spoolDirectory"/> is null or empty, or when <paramref name="digestHex"/> fails
        /// validation (wrong length or non-lowercase hexadecimal characters).
        /// </exception>
        /// <remarks>
        /// <para>
        /// Fan-out segments are sliced from <paramref name="digestHex"/> without allocating intermediate substrings and
        /// joined with a multi-span <see cref="Path.Join(ReadOnlySpan{char}, ReadOnlySpan{char}, ReadOnlySpan{char}, ReadOnlySpan{char})"/>
        /// overload to avoid per-level string allocations on the writer pump hot path.
        /// </para>
        /// <para>
        /// Invoked from <see cref="Storage.NntpSpoolWriterPump"/> immediately before
        /// <see cref="Utilities.IO.FileIOUtilities.AtomicWriteAsync"/>. Parent fan-out directories are created by the pump
        /// when missing.
        /// </para>
        /// </remarks>
        /// <example>
        /// For digest <c>aabb…</c> (64 lowercase hex chars), the path begins
        /// <c>…/Incoming/aa/bb/aabb…</c>.
        /// </example>
        public static string GetArticleFilePath(string spoolDirectory, string digestHex)
        {
            ArgumentException.ThrowIfNullOrEmpty(spoolDirectory);
            ValidateDigestHex(digestHex);

            string incoming = GetIncomingDirectory(spoolDirectory);
            ReadOnlySpan<char> digest = digestHex.AsSpan();
            return Path.Join(
                incoming.AsSpan(),
                digest[..FanoutLevelLength],
                digest.Slice(FanoutLevelLength, FanoutLevelLength),
                digest);
        }

        /// <summary>
        /// Validates a digest string before spool fan-out path generation.
        /// </summary>
        /// <param name="digestHex">Digest candidate from HistoryDB encoding or enqueue metadata.</param>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="digestHex"/> is <see langword="null"/> or empty, length is not exactly
        /// <see cref="HistoryKeyEncoder.DigestHexLength"/>, or any character is outside lowercase hexadecimal
        /// (<c>0-9</c>, <c>a-f</c>). Uppercase <c>A-F</c> is rejected.
        /// </exception>
        /// <remarks>
        /// <para>
        /// Called from <see cref="GetArticleFilePath"/> before slicing fan-out segments. A valid 64-character digest
        /// always provides enough characters for the two-level fan-out layout used by the path builder.
        /// </para>
        /// <para>Never returns; throws on failure.</para>
        /// </remarks>
        private static void ValidateDigestHex(string digestHex)
        {
            ArgumentException.ThrowIfNullOrEmpty(digestHex);
            if (digestHex.Length != HistoryKeyEncoder.DigestHexLength)
            {
                throw new ArgumentException(
                    $"Digest must be exactly {HistoryKeyEncoder.DigestHexLength} hexadecimal characters.",
                    nameof(digestHex));
            }

            foreach (char ch in digestHex)
            {
                bool isHexLower = ch is (>= '0' and <= '9') or (>= 'a' and <= 'f');
                if (!isHexLower)
                {
                    throw new ArgumentException("Digest must contain lowercase hexadecimal characters only.", nameof(digestHex));
                }
            }
        }
    }
}
