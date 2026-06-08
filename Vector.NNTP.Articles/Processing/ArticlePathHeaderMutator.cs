// <copyright file="ArticlePathHeaderMutator.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: bounded header rewrite for transit Path hop token prepending.

using System.Text;
using Vector.NNTP.Articles.Scanning;

namespace Vector.NNTP.Articles.Processing
{
    /// <summary>
    /// Static helper that prepends a transit hop token to the <c>Path:</c> header of a raw NNTP article byte buffer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Caller:</b> Invoked from <see cref="ArticleSpoolPreprocessor.PreprocessAsync"/> when
    /// <see cref="Sockets.Configuration.NntpServerOptions.PathAppend"/> is non-empty whitespace after trimming. Shallow header syntax
    /// validation should already have succeeded; exceptions from <see cref="PrependPathAppend"/> are caught by the
    /// preprocessor and converted into <see cref="ArticleSpoolPreprocessResult"/> failures with message
    /// <c>Path header mutation failed: …</c>.
    /// </para>
    /// <para>
    /// <b>Existing <c>Path:</c>:</b> <see cref="PrependPathAppend"/> scans header lines and, on the first match from
    /// <see cref="IsPathHeaderLine"/>, prepends <c>{pathAppend}!</c> to that line's value via
    /// <see cref="RewriteExistingPath"/>. Folded continuation lines (leading space or tab) are left unchanged;
    /// multiline <c>Path</c> values are rare on transit feeds and rewriting the first physical line is sufficient for
    /// hop tracing.
    /// </para>
    /// <para>
    /// <b>Missing <c>Path:</c>:</b> When no <c>Path</c> field is found (including when <c>Path::</c> lookalikes are
    /// rejected), <see cref="InsertNewPathHeader"/> inserts <c>Path: {pathAppend}</c> as the <em>first</em> header line
    /// (before <c>Message-ID</c>, <c>Newsgroups</c>, and other fields), matching common INN/Diablo/Cyclone injection
    /// ordering. RFCs do not mandate header order, but transit tooling often expects <c>Path</c> near the top of the
    /// block.
    /// </para>
    /// <para>
    /// <b>Allocations:</b> Always returns a new <see cref="byte"/> array on success paths and when no header terminator
    /// is found (defensive copy). <see cref="RewriteExistingPath"/> assembles replacement lines from UTF-8 spans and a
    /// single hop <see cref="Encoding.ASCII"/> encode without round-tripping the existing path value through
    /// <see cref="string"/>. Body bytes and bytes outside the rewritten header region are copied verbatim.
    /// </para>
    /// <para><b>Threading:</b> Stateless static methods; safe for concurrent writer pumps without synchronization.</para>
    /// </remarks>
    public static class ArticlePathHeaderMutator
    {
        /// <summary>
        /// Canonical NNTP <c>Path</c> header field name used for insertion and matching.
        /// </summary>
        /// <remarks>
        /// <see cref="PathFieldNameMatchBytes"/> and <see cref="CanonicalPathLinePrefixBytes"/> are derived from this
        /// constant so the field name is defined in one place. Emitted on output as canonical <c>Path: </c> regardless
        /// of source line casing.
        /// </remarks>
        private const string PathFieldName = "Path";

        /// <summary>
        /// Lowercase ASCII bytes of <see cref="PathFieldName"/> for case-insensitive header-line matching.
        /// </summary>
        /// <remarks>
        /// Initialized once at type load. Compared via <see cref="ArticleByteScanSimd.StartsWithAsciiIgnoreCase"/>
        /// against physical header lines in <see cref="IsPathHeaderLine"/>.
        /// </remarks>
        private static readonly byte[] PathFieldNameMatchBytes = Encoding.ASCII.GetBytes(PathFieldName.ToLowerInvariant());

        /// <summary>
        /// Canonical <c>{PathFieldName}: </c> prefix bytes emitted when rewriting or inserting a <c>Path</c> line.
        /// </summary>
        /// <remarks>
        /// Includes the required space after the colon. Used by <see cref="BuildPathInsertLineBytes"/> and
        /// <see cref="RewriteExistingPath"/> so output lines normalize to <c>Path: </c> even when the source used
        /// <c>PATH:</c> or <c>path:</c>.
        /// </remarks>
        private static readonly byte[] CanonicalPathLinePrefixBytes = Encoding.ASCII.GetBytes($"{PathFieldName}: ");

        /// <summary>
        /// Prepends a path hop token into the article header block.
        /// </summary>
        /// <param name="article">
        /// Raw article bytes including headers, separator, and body. Not modified in place; callers retain the original
        /// buffer when mutation fails upstream.
        /// </param>
        /// <param name="pathAppend">
        /// Hop token to record (for example a hostname or <c>host!alias</c> segment). Trimmed before use; must be
        /// non-empty and must not contain CR/LF. Encoded with <see cref="Encoding.ASCII"/> when written into the header
        /// (callers should supply ASCII hop tokens consistent with transit <c>Path</c> conventions).
        /// </param>
        /// <returns>
        /// A new byte array containing the mutated article. When a <c>Path</c> line is rewritten, output uses the
        /// canonical <c>Path: </c> prefix regardless of the source line's casing. When no <c>Path</c> field exists, the
        /// new line is prepended before all original bytes. When no header terminator is found, returns a defensive copy
        /// of <paramref name="article"/> unchanged.
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="pathAppend"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="pathAppend"/> is empty or whitespace after trimming, or contains CR/LF
        /// delimiters. <see cref="ArticleSpoolPreprocessor"/> skips invocation when
        /// <see cref="Sockets.Configuration.NntpServerOptions.PathAppend"/> is unset; an empty hop here indicates a configuration or call-site
        /// mistake rather than a no-op.
        /// </exception>
        /// <remarks>
        /// <para>
        /// Scans only the header region bounded by <see cref="FindHeaderTerminator"/>. The first
        /// <see cref="IsPathHeaderLine"/> match wins; later <c>Path</c> lines are not modified. Physical lines beginning
        /// with space or tab are never considered <c>Path</c> fields (continuations of a prior line).
        /// </para>
        /// <para>
        /// When no <c>Path</c> field exists, <see cref="InsertNewPathHeader"/> prepends
        /// <c>{PathFieldName}: {hop}{newline}</c> before all original bytes. The newline bytes detected by
        /// <see cref="FindHeaderTerminator"/> preserve the article's observed line-ending style for the inserted line.
        /// </para>
        /// <para>
        /// When an existing <c>Path</c> value is empty after the colon, the rewritten line is <c>Path: {hop}</c> without
        /// a trailing <c>!</c>. Non-empty values become <c>Path: {hop}!{existing}</c> with existing bytes copied
        /// verbatim from the source line.
        /// </para>
        /// </remarks>
        public static byte[] PrependPathAppend(ReadOnlySpan<byte> article, string pathAppend)
        {
            ArgumentNullException.ThrowIfNull(pathAppend);
            string hop = pathAppend.Trim();
            if (hop.Length == 0)
            {
                throw new ArgumentException("Path append token cannot be empty or whitespace.", nameof(pathAppend));
            }

            if (hop.Contains('\r') || hop.Contains('\n'))
            {
                throw new ArgumentException("Path append token cannot contain CR/LF characters.", nameof(pathAppend));
            }

            int headerEndIndex = FindHeaderTerminator(article, out ReadOnlySpan<byte> newlineBytes);
            if (headerEndIndex < 0)
            {
                return article.ToArray();
            }

            ReadOnlySpan<byte> headerSpan = article[..headerEndIndex];
            byte[] insertedHeaderBytes = BuildPathInsertLineBytes(hop, newlineBytes);

            int lineStart = 0;
            while (lineStart < headerSpan.Length)
            {
                int lineEnd = FindNextLineEnd(headerSpan, lineStart);
                ReadOnlySpan<byte> line = TrimTrailingCr(headerSpan[lineStart..lineEnd]);
                if (IsPathHeaderLine(line))
                {
                    return RewriteExistingPath(article, lineStart, lineEnd, hop);
                }

                lineStart = lineEnd < headerSpan.Length ? lineEnd + 1 : headerSpan.Length;
            }

            return InsertNewPathHeader(article, insertedHeaderBytes);
        }

        /// <summary>
        /// Inserts a new <c>Path:</c> line before the first byte of the original article.
        /// </summary>
        /// <param name="article">Original article bytes including headers, separator, and body.</param>
        /// <param name="pathHeaderBytes">
        /// Prepared <c>Path:</c> header bytes including the line terminator (for example <c>Path: hop\r\n</c>) from
        /// <see cref="BuildPathInsertLineBytes"/>.
        /// </param>
        /// <returns>
        /// A new array whose first bytes are <paramref name="pathHeaderBytes"/> followed by the full original
        /// <paramref name="article"/> content unchanged.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Produces <c>Path: {hop}\r\nMessage-ID: …</c> rather than inserting <c>Path</c> after existing headers.
        /// Performs one allocation sized to the combined length.
        /// </para>
        /// <para>Never throws.</para>
        /// </remarks>
        private static byte[] InsertNewPathHeader(ReadOnlySpan<byte> article, ReadOnlySpan<byte> pathHeaderBytes)
        {
            byte[] result = new byte[pathHeaderBytes.Length + article.Length];
            pathHeaderBytes.CopyTo(result);
            article.CopyTo(result.AsSpan(pathHeaderBytes.Length));
            return result;
        }

        /// <summary>
        /// Rewrites the first matched <c>Path:</c> line to prepend the configured hop token.
        /// </summary>
        /// <param name="article">Original article bytes.</param>
        /// <param name="lineStart">Matched <c>Path</c> line start index within <paramref name="article"/>.</param>
        /// <param name="lineEnd">
        /// Index of the line-feed byte terminating the matched line, or the end of the header span when the final header
        /// line has no trailing LF within the scanned region.
        /// </param>
        /// <param name="hop">Trimmed ASCII hop token to insert before any existing path value.</param>
        /// <returns>
        /// A new array with the matched physical line replaced by <c>Path: {hop}</c> or <c>Path: {hop}!{existing}</c>,
        /// preserving the original line terminator and all bytes from <paramref name="lineEnd"/> onward (including folded
        /// continuation lines).
        /// </returns>
        /// <remarks>
        /// <para>
        /// Existing value bytes after the field delimiter are taken from index <c>{PathFieldName.Length} + 1</c>
        /// onward (after <c>{PathFieldName}:</c>), with leading whitespace trimmed. The replacement is built from
        /// <see cref="CanonicalPathLinePrefixBytes"/>, ASCII hop bytes, an optional <c>!</c>, and the existing value span
        /// copied verbatim without decoding the existing path to <see cref="string"/>.
        /// </para>
        /// <para>
        /// Bytes before <paramref name="lineStart"/> and from <paramref name="lineEnd"/> onward (including the line
        /// terminator byte and any folded continuation lines) are copied unchanged.
        /// </para>
        /// <para>Never throws.</para>
        /// </remarks>
        private static byte[] RewriteExistingPath(ReadOnlySpan<byte> article, int lineStart, int lineEnd, string hop)
        {
            ReadOnlySpan<byte> line = TrimTrailingCr(article[lineStart..lineEnd]);
            int valueStartIndex = PathFieldName.Length + 1;
            ReadOnlySpan<byte> existing = line.Length > valueStartIndex ? line[valueStartIndex..] : [];
            while (!existing.IsEmpty && (existing[0] is (byte)' ' or (byte)'\t'))
            {
                existing = existing[1..];
            }

            ReadOnlySpan<byte> pathPrefix = CanonicalPathLinePrefixBytes;
            int hopByteCount = Encoding.ASCII.GetByteCount(hop);
            int rewrittenLength = pathPrefix.Length + hopByteCount + (existing.IsEmpty ? 0 : 1 + existing.Length);
            byte[] rewrittenBytes = new byte[rewrittenLength];
            int offset = 0;
            pathPrefix.CopyTo(rewrittenBytes.AsSpan(offset));
            offset += pathPrefix.Length;
            offset += Encoding.ASCII.GetBytes(hop, rewrittenBytes.AsSpan(offset));
            if (!existing.IsEmpty)
            {
                rewrittenBytes[offset++] = (byte)'!';
                existing.CopyTo(rewrittenBytes.AsSpan(offset));
            }

            int originalLength = lineEnd - lineStart;
            int lengthDelta = rewrittenBytes.Length - originalLength;
            byte[] result = new byte[article.Length + lengthDelta];

            article[..lineStart].CopyTo(result);
            rewrittenBytes.CopyTo(result.AsSpan(lineStart));
            article[lineEnd..].CopyTo(result.AsSpan(lineStart + rewrittenBytes.Length));
            return result;
        }

        /// <summary>
        /// Assembles <c>Path: {hop}{newline}</c> bytes without a string round-trip for the full line.
        /// </summary>
        /// <param name="hop">Trimmed ASCII hop token (already validated by <see cref="PrependPathAppend"/>).</param>
        /// <param name="newlineBytes">
        /// Observed line ending from <see cref="FindHeaderTerminator"/> (<c>\r\n</c> or <c>\n</c>).
        /// </param>
        /// <returns>
        /// Single allocated buffer containing <see cref="CanonicalPathLinePrefixBytes"/>, ASCII hop bytes, and
        /// <paramref name="newlineBytes"/>.
        /// </returns>
        /// <remarks>Never throws for hop tokens that fit in ASCII encoding.</remarks>
        private static byte[] BuildPathInsertLineBytes(string hop, ReadOnlySpan<byte> newlineBytes)
        {
            int hopByteCount = Encoding.ASCII.GetByteCount(hop);
            byte[] insertedHeaderBytes = new byte[CanonicalPathLinePrefixBytes.Length + hopByteCount + newlineBytes.Length];
            int offset = 0;
            CanonicalPathLinePrefixBytes.CopyTo(insertedHeaderBytes.AsSpan(offset));
            offset += CanonicalPathLinePrefixBytes.Length;
            offset += Encoding.ASCII.GetBytes(hop, insertedHeaderBytes.AsSpan(offset));
            newlineBytes.CopyTo(insertedHeaderBytes.AsSpan(offset));
            return insertedHeaderBytes;
        }

        /// <summary>
        /// Locates the header/body separator and captures the article's line-ending style.
        /// </summary>
        /// <param name="article">Full article bytes to scan.</param>
        /// <param name="newlineBytes">
        /// When a terminator is found, the line ending observed in the article (<c>\r\n</c> or <c>\n</c>). When no
        /// terminator is found, defaults to <c>\r\n</c> for prepared insert lines (the insert path is not taken when
        /// scanning fails).
        /// </param>
        /// <returns>
        /// Index within <paramref name="article"/> of the first byte of the header/body separator, or <c>-1</c> when no
        /// <c>\r\n\r\n</c> or <c>\n\n</c> delimiter exists. Header scanning uses
        /// <c>article[..returnValue]</c> as the exclusive header field region.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Delegates to <see cref="ArticleByteScanSimd.FindHeaderSeparator"/> for SIMD-accelerated separator detection.
        /// For <c>\r\n\r\n</c>, returns the index of the first <c>\r</c> in the separator. For <c>\n\n</c>, returns the
        /// index of the first <c>\n</c> in the separator. This differs from
        /// <see cref="ArticleByteScanSimd.FindHeaderEnd"/> indexing, which points at the start of the blank line within
        /// the separator; both approaches limit iteration to header field lines only.
        /// </para>
        /// <para>Never throws.</para>
        /// </remarks>
        private static int FindHeaderTerminator(ReadOnlySpan<byte> article, out ReadOnlySpan<byte> newlineBytes)
        {
            (int headerEnd, _) = ArticleByteScanSimd.FindHeaderSeparator(article);
            if (headerEnd < 0)
            {
                newlineBytes = "\r\n"u8;
                return -1;
            }

            if (headerEnd + 1 < article.Length &&
                article[headerEnd] == (byte)'\r' &&
                article[headerEnd + 1] == (byte)'\n')
            {
                newlineBytes = "\r\n"u8;
                return headerEnd - 2;
            }

            newlineBytes = "\n"u8;
            return headerEnd - 1;
        }

        /// <summary>
        /// Finds the end offset of the physical line starting at <paramref name="lineStart"/>.
        /// </summary>
        /// <param name="headerSpan">Header bytes excluding the body separator.</param>
        /// <param name="lineStart">Zero-based line start index within <paramref name="headerSpan"/>.</param>
        /// <returns>
        /// Index of the line-feed byte that terminates the line, or <paramref name="headerSpan"/>.<see cref="ReadOnlySpan{T}.Length"/>
        /// when the final header line has no trailing LF within the span.
        /// </returns>
        /// <remarks>
        /// Thin wrapper over <see cref="ArticleByteScanSimd.IndexOfLineFeed"/> scoped to the header region. Never throws.
        /// </remarks>
        private static int FindNextLineEnd(ReadOnlySpan<byte> headerSpan, int lineStart)
        {
            return ArticleByteScanSimd.IndexOfLineFeed(headerSpan, lineStart, headerSpan.Length);
        }

        /// <summary>
        /// Removes a trailing carriage return from a physical line span.
        /// </summary>
        /// <param name="line">Line content ending at (but not including) the line-feed byte.</param>
        /// <returns>
        /// <paramref name="line"/> shortened by one byte when it ends with <c>\r</c>; otherwise the original span.
        /// </returns>
        /// <remarks>
        /// Normalizes <c>\r\n</c>-terminated lines before field-name matching and value extraction. Never throws.
        /// </remarks>
        private static ReadOnlySpan<byte> TrimTrailingCr(ReadOnlySpan<byte> line)
        {
            return line.Length > 0 && line[^1] == '\r' ? line[..^1] : line;
        }

        /// <summary>
        /// Determines whether a physical header line is a <c>Path</c> field rather than a lookalike such as <c>Path::</c>.
        /// </summary>
        /// <param name="line">Physical header line without trailing CR/LF (continuations are not passed here).</param>
        /// <returns>
        /// <see langword="true"/> when the line begins with <see cref="PathFieldName"/> case-insensitively, the delimiter
        /// colon sits at index <see cref="PathFieldName"/>.<see cref="string.Length"/>, and the following byte is not a
        /// second colon.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Compares normalized line bytes against <see cref="PathFieldNameMatchBytes"/> via
        /// <see cref="ArticleByteScanSimd.StartsWithAsciiIgnoreCase"/>. Accepts <c>Path:</c>, <c>PATH:</c>, and
        /// <c>path:</c>. Rejects <c>Path::</c> and lines shorter than <c>{PathFieldName.Length} + 1</c> bytes.
        /// </para>
        /// <para>
        /// Malformed field names should already be rejected by <see cref="ArticleSpoolPreprocessor"/>; this guard
        /// prevents rewriting lines that merely share a <c>Path</c> prefix (for example <c>Path:: bad</c>), causing
        /// <see cref="PrependPathAppend"/> to insert a new top-level <c>Path</c> line instead.
        /// </para>
        /// <para>Never throws.</para>
        /// </remarks>
        private static bool IsPathHeaderLine(ReadOnlySpan<byte> line)
        {
            int minimumLength = PathFieldName.Length + 1;
            if (line.Length < minimumLength)
            {
                return false;
            }

            if (!ArticleByteScanSimd.StartsWithAsciiIgnoreCase(line, PathFieldNameMatchBytes))
            {
                return false;
            }

            if (line[PathFieldName.Length] != (byte)':')
            {
                return false;
            }

            int valueStartIndex = PathFieldName.Length + 1;
            return line.Length == valueStartIndex || line[valueStartIndex] != (byte)':';
        }
    }
}
