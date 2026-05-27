// <copyright file="PostFilterParsedArticle.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: diagnostics/formatting/startup validation; readability over micro-optimization; allocations acceptable.
// PostFilterParsedArticle.cs -- Parsed NNTP article (headers + body) for PostFilter stages.

namespace Vector.NNTP.Filters.PostFilter
{
    /// <summary>
    /// Parsed NNTP article (headers + body) for filter stages.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="PostFilterParsedArticle"/> class.
    /// </remarks>
    /// <param name="rawUtf8">Original article octets (UTF-8 assumed for text checks).</param>
    /// <param name="headerLineCount">Number of header lines (before blank line).</param>
    /// <param name="headers">Lowercase header field names to decoded values.</param>
    /// <param name="bodyStart">Start index of body in <paramref name="rawUtf8"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="rawUtf8"/> or <paramref name="headers"/> is <see langword="null"/>.</exception>
    public sealed class PostFilterParsedArticle(byte[] rawUtf8, int headerLineCount, IReadOnlyDictionary<string, string> headers, int bodyStart)
    {

        /// <summary>Full article octets as received on the POST path (UTF-8 assumed for text scanning).</summary>
        public byte[] RawUtf8 { get; } = rawUtf8 ?? throw new ArgumentNullException(nameof(rawUtf8));

        /// <summary>Count of logical header lines before the blank line separator (diagnostics and style checks).</summary>
        public int HeaderLineCount { get; } = headerLineCount;

        /// <summary>Decoded header field values keyed by lowercase name (for example <c>from</c>, <c>subject</c>).</summary>
        public IReadOnlyDictionary<string, string> Headers { get; } = headers ?? throw new ArgumentNullException(nameof(headers));

        /// <summary>Zero-based index in <see cref="RawUtf8"/> where the body begins (first byte after the header/body blank line).</summary>
        public int BodyStart { get; } = bodyStart;

        /// <summary>Body slice spanning <see cref="BodyStart"/> through the end of <see cref="RawUtf8"/> (may be empty).</summary>
        public ReadOnlySpan<byte> Body => RawUtf8.AsSpan(BodyStart);

        /// <summary>Returns a header value by lowercase name, or <see cref="string.Empty"/> when absent.</summary>
        /// <param name="lowerName">Lowercase header name.</param>
        /// <returns>Value or empty.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="lowerName"/> is <see langword="null"/>.</exception>
        public string GetHeader(string lowerName)
        {
            ArgumentNullException.ThrowIfNull(lowerName);
            return Headers.TryGetValue(lowerName, out string? v) ? v : string.Empty;
        }
    }
}

