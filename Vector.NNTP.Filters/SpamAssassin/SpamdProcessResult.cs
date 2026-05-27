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
    /// <remarks>
    /// Initializes a new instance of the <see cref="SpamdProcessResult"/> class.
    /// </remarks>
    /// <param name="processedArticle">Article bytes after spamd processing (headers may include X-Spam-* fields).</param>
    /// <param name="classification">Parsed <c>Spam:</c> header when spamd included it; otherwise <see langword="null"/>.</param>
    /// <param name="rawResponseHeaders">Unparsed response header lines (excluding the status line).</param>
    public sealed class SpamdProcessResult(
        byte[] processedArticle,
        SpamdCheckResult? classification,
        IReadOnlyDictionary<string, string> rawResponseHeaders)
    {

        /// <summary>Rewritten article octets (typically RFC 822 / NNTP POST buffer with added SpamAssassin headers).</summary>
        public byte[] ProcessedArticle { get; } = processedArticle ?? throw new ArgumentNullException(nameof(processedArticle));

        /// <summary>Classification parsed from response headers when present.</summary>
        public SpamdCheckResult? Classification { get; } = classification;

        /// <summary>Additional response headers returned by spamd (keys are lower-case).</summary>
        public IReadOnlyDictionary<string, string> RawResponseHeaders { get; } = rawResponseHeaders;
    }
}
