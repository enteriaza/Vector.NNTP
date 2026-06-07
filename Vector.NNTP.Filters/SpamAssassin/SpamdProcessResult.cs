// <copyright file="SpamdProcessResult.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: result DTO for PROCESS spamd command.
// SpamdProcessResult.cs -- Modified Usenet article bytes and optional classification metadata.

namespace Vector.NNTP.Filters.SpamAssassin
{
    /// <summary>
    /// Outcome of a spamd <c>PROCESS</c> command: the rewritten article and optional <c>Spam:</c> metadata.
    /// </summary>
    /// <param name="processedArticle">
    /// Article bytes after spamd processing (typically the same RFC 822 / NNTP POST buffer with X-Spam-* headers inserted).
    /// Must not be <see langword="null"/>.
    /// </param>
    /// <param name="classification">
    /// Parsed <c>Spam:</c> header when spamd included it in the response header block; otherwise <see langword="null"/>.
    /// </param>
    /// <param name="rawResponseHeaders">
    /// Response header map from spamd excluding the status line (for example <c>Spam</c>, <c>Content-length</c>).
    /// Must not be <see langword="null"/>; lookup is case-insensitive.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="processedArticle"/> or <paramref name="rawResponseHeaders"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para><b>Producer:</b> Returned by <see cref="SpamAssassin.ProcessAsync"/> after a successful <c>PROCESS</c> exchange via
    /// <see cref="SpamdWireSession"/>.</para>
    /// <para><b>Ownership:</b> <paramref name="processedArticle"/> and <paramref name="rawResponseHeaders"/> are retained by reference and are not copied.
    /// Callers must not mutate the article buffer or header dictionary after construction even though the properties are get-only.</para>
    /// <para><b>Headers:</b> Header name lookup on <see cref="RawResponseHeaders"/> is case-insensitive; keys preserve the casing returned on the wire.</para>
    /// </remarks>
    public sealed class SpamdProcessResult(
        byte[] processedArticle,
        SpamdCheckResult? classification,
        IReadOnlyDictionary<string, string> rawResponseHeaders)
    {

        /// <summary>
        /// Gets the rewritten article octets returned in the spamd <c>PROCESS</c> response body.
        /// </summary>
        /// <remarks>
        /// Typically the input POST buffer with SpamAssassin annotation headers prepended or appended. Retained by reference without copying;
        /// callers must not modify the array after this instance is constructed.
        /// </remarks>
        public byte[] ProcessedArticle { get; } = processedArticle ?? throw new ArgumentNullException(nameof(processedArticle));

        /// <summary>
        /// Gets classification parsed from the response <c>Spam:</c> header when spamd included one; otherwise <see langword="null"/>.
        /// </summary>
        /// <remarks>
        /// Populated by <see cref="SpamdWireSession.TryParseSpamHeader"/> when the header block contains a recognizable
        /// <c>Spam: True ; score / threshold</c> or <c>False</c> line. A <see langword="null"/> value does not imply the
        /// <c>PROCESS</c> failed — only that no parseable <c>Spam:</c> header was present.
        /// </remarks>
        public SpamdCheckResult? Classification { get; } = classification;

        /// <summary>
        /// Gets the response header map from spamd excluding the <c>SPAMD/x.y</c> status line.
        /// </summary>
        /// <remarks>
        /// Retained by reference without copying. Header name lookup is case-insensitive; keys preserve wire casing.
        /// Callers should treat the dictionary as read-only and must not mutate it after construction.
        /// </remarks>
        public IReadOnlyDictionary<string, string> RawResponseHeaders { get; } =
            rawResponseHeaders ?? throw new ArgumentNullException(nameof(rawResponseHeaders));
    }
}
