// <copyright file="SpoolDirectoryUtilities.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: spool directory path normalization and digest fan-out path construction.

using Vector.NNTP.HistoryDB.Encoding;
using Vector.NNTP.Sockets.Configuration;

namespace Vector.NNTP.Articles.Diagnostics
{
    /// <summary>
    /// Resolves transit spool root and article file paths from server options and message digest keys.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Article payload files are sharded under <c>Incoming/{level1}/{level2}/{digest}</c>, where each fan-out level
    /// uses <see cref="FanoutLevelLength"/> lowercase hexadecimal characters from the start of the digest. With the
    /// default layout this yields <c>Incoming/{aa}/{bb}/{digest}</c> and keeps directory fan-out bounded under
    /// sustained transit load.
    /// </para>
    /// <para>
    /// <see cref="ResolveSpoolDirectory"/> always returns a canonical absolute path via
    /// <see cref="Path.GetFullPath(string)"/>, normalizing <c>.</c>, <c>..</c>, and redundant separators for both
    /// rooted and relative configured values.
    /// </para>
    /// </remarks>
    public static class SpoolDirectoryUtilities
    {
        /// <summary>
        /// Default spool subdirectory name under <see cref="AppContext.BaseDirectory"/> when
        /// <see cref="NntpServerOptions.SpoolDir"/> is unset or whitespace.
        /// </summary>
        public const string DefaultSpoolSubdirectory = "Spool";

        /// <summary>
        /// Incoming spool subdirectory containing transit articles pending downstream ingestion.
        /// </summary>
        public const string IncomingSubdirectory = "Incoming";

        /// <summary>
        /// Number of lowercase hexadecimal characters in each digest fan-out directory level.
        /// </summary>
        /// <remarks>
        /// The first <see cref="FanoutLevelCount"/> levels under <see cref="IncomingSubdirectory"/> each consume this
        /// many characters from the start of the digest key.
        /// </remarks>
        public const int FanoutLevelLength = 2;

        /// <summary>
        /// Number of digest-derived fan-out directory levels between <see cref="IncomingSubdirectory"/> and the leaf
        /// digest file name.
        /// </summary>
        public const int FanoutLevelCount = 1;

        /// <summary>
        /// Total lowercase hexadecimal prefix length consumed by all fan-out levels
        /// (<see cref="FanoutLevelLength"/> × <see cref="FanoutLevelCount"/>).
        /// </summary>
        public const int FanoutPrefixHexLength = FanoutLevelLength * FanoutLevelCount;

        /// <summary>
        /// Resolves the canonical absolute spool root path from <see cref="NntpServerOptions.SpoolDir"/>.
        /// </summary>
        /// <param name="options">Server options containing spool configuration.</param>
        /// <returns>
        /// A normalized absolute spool root directory path. Unset or whitespace <see cref="NntpServerOptions.SpoolDir"/>
        /// resolves to <see cref="DefaultSpoolSubdirectory"/> under <see cref="AppContext.BaseDirectory"/>.
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// <para><b>Relative configured paths</b> are combined with <see cref="AppContext.BaseDirectory"/> before
        /// normalization, so values such as <c>.\Spool</c>, <c>..\Spool</c>, and <c>Spool\..\Incoming</c> collapse to
        /// canonical absolute paths.</para>
        /// <para><b>Rooted configured paths</b> are passed through <see cref="Path.GetFullPath(string)"/> so segments
        /// such as <c>..\</c> are resolved against the supplied root (for example <c>D:\Spool\..\Data</c> →
        /// <c>D:\Data</c> on Windows).</para>
        /// </remarks>
        public static string ResolveSpoolDirectory(NntpServerOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            if (string.IsNullOrWhiteSpace(options.SpoolDir))
            {
                return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, DefaultSpoolSubdirectory));
            }

            string configured = options.SpoolDir.Trim();
            if (Path.IsPathRooted(configured))
            {
                return Path.GetFullPath(configured);
            }

            return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configured));
        }

        /// <summary>
        /// Gets the incoming article spool directory under the supplied spool root.
        /// </summary>
        /// <param name="spoolDirectory">Absolute spool root directory, typically from <see cref="ResolveSpoolDirectory"/>.</param>
        /// <returns>
        /// The path <c>{spoolDirectory}/{<see cref="IncomingSubdirectory"/>}</c>.
        /// </returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="spoolDirectory"/> is null or empty.</exception>
        public static string GetIncomingDirectory(string spoolDirectory)
        {
            ArgumentException.ThrowIfNullOrEmpty(spoolDirectory);
            return Path.Combine(spoolDirectory, IncomingSubdirectory);
        }

        /// <summary>
        /// Gets the canonical transit article payload path for a digest key.
        /// </summary>
        /// <param name="spoolDirectory">Spool root directory.</param>
        /// <param name="digestHex">
        /// Lowercase hexadecimal digest key with length <see cref="HistoryKeyEncoder.DigestHexLength"/>.
        /// </param>
        /// <returns>
        /// A path in the form <c>Incoming/{level1}/…/{digest}</c> with <see cref="FanoutLevelCount"/> fan-out levels of
        /// <see cref="FanoutLevelLength"/> characters each, followed by the full digest as the leaf file name.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="spoolDirectory"/> or <paramref name="digestHex"/> is null, empty, or invalid.
        /// </exception>
        /// <remarks>
        /// Fan-out segments are taken from the digest via <see cref="ReadOnlySpan{T}.Slice(int, int)"/> and joined with
        /// <see cref="Path.Join(ReadOnlySpan{char}, ReadOnlySpan{char}, ReadOnlySpan{char}, ReadOnlySpan{char})"/> to
        /// avoid per-level substring allocations.
        /// </remarks>
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
        /// Validates a digest string for spool fan-out path generation.
        /// </summary>
        /// <param name="digestHex">Digest candidate.</param>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="digestHex"/> is null, empty, not exactly
        /// <see cref="HistoryKeyEncoder.DigestHexLength"/> characters, or contains non-lowercase hexadecimal characters.
        /// </exception>
        /// <remarks>
        /// A valid digest implicitly supplies at least <see cref="FanoutPrefixHexLength"/> characters for fan-out
        /// directory names because <see cref="HistoryKeyEncoder.DigestHexLength"/> exceeds the fan-out prefix length.
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
                bool isHexLower = (ch is >= '0' and <= '9') || (ch is >= 'a' and <= 'f');
                if (!isHexLower)
                {
                    throw new ArgumentException("Digest must contain lowercase hexadecimal characters only.", nameof(digestHex));
                }
            }
        }
    }
}
