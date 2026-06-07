// <copyright file="NullNntpNewsLog.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: no-op INN news log for unit tests and DI fallbacks without host configuration.

using Vector.NNTP.Articles.Storage;

namespace Vector.NNTP.Articles.Logging
{
    /// <summary>
    /// Null-object implementation of <see cref="INntpNewsLog"/> that discards all accept, reject, cancel, and junk events.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Registered by <see cref="DependencyInjection.ServiceCollectionExtensions.AddNntpArticlesTransitSpool"/> when
    /// <c>IConfiguration</c> is not supplied (typical in unit tests). Production NNTPD hosts pass configuration and
    /// receive <see cref="NntpNewsLog"/> instead.
    /// </para>
    /// <para>
    /// Tests may also inject <see cref="Instance"/> directly into storage and writer components without going through DI.
    /// </para>
    /// <para>
    /// All methods are no-ops: no Serilog logger is created, no <c>news</c> file is opened, and no lines are formatted.
    /// Parameters are read only to satisfy analyzer discard rules; callers should not depend on side effects.
    /// </para>
    /// <para><b>Thread safety:</b> Stateless after construction; safe for concurrent writer pumps.</para>
    /// </remarks>
    internal sealed class NullNntpNewsLog : INntpNewsLog
    {
        /// <summary>
        /// Gets the shared singleton no-op logger used for DI fallback and direct test wiring.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Lazily initialized once by the runtime. Prefer this instance over allocating new
        /// <see cref="NullNntpNewsLog"/> objects so tests and fallback registration share one object identity.
        /// </para>
        /// </remarks>
        internal static NullNntpNewsLog Instance { get; } = new();

        /// <summary>
        /// Discards a spool commit accept event that would otherwise emit a plus line.
        /// </summary>
        /// <param name="messageId">Article Message-ID (ignored).</param>
        /// <param name="origin">Enqueue origin metadata (ignored).</param>
        /// <param name="articleBytes">Committed article bytes (ignored).</param>
        /// <remarks>Never throws. Does not invoke <see cref="NntpNewsLogFormatter"/> or touch the filesystem.</remarks>
        public void LogAccepted(string messageId, in NntpSpoolArticleOrigin origin, ReadOnlySpan<byte> articleBytes)
        {
            _ = messageId;
            _ = origin;
            _ = articleBytes;
        }

        /// <summary>
        /// Discards a pipeline or storage rejection that would otherwise emit a minus line with a reason.
        /// </summary>
        /// <param name="messageId">Article Message-ID (ignored).</param>
        /// <param name="origin">Enqueue origin metadata (ignored).</param>
        /// <param name="articleBytes">Article bytes used for Path feed fallback on real loggers (ignored).</param>
        /// <param name="reason">Operator-facing rejection reason (ignored).</param>
        /// <remarks>Never throws. Does not sanitize or persist <paramref name="reason"/>.</remarks>
        public void LogRejected(
            string messageId,
            in NntpSpoolArticleOrigin origin,
            ReadOnlySpan<byte> articleBytes,
            string reason)
        {
            _ = messageId;
            _ = origin;
            _ = articleBytes;
            _ = reason;
        }

        /// <summary>
        /// Discards a processed cancel event that would otherwise emit a cancel line after spool commit.
        /// </summary>
        /// <param name="messageId">Cancel article Message-ID (ignored).</param>
        /// <param name="origin">Enqueue origin metadata (ignored).</param>
        /// <param name="articleBytes">Committed cancel article bytes (ignored).</param>
        /// <remarks>Never throws. Does not parse <c>Control</c> headers or emit cancel targets.</remarks>
        public void LogCancelProcessed(string messageId, in NntpSpoolArticleOrigin origin, ReadOnlySpan<byte> articleBytes)
        {
            _ = messageId;
            _ = origin;
            _ = articleBytes;
        }

        /// <summary>
        /// Discards a junk-newsgroup accept event that would otherwise emit a junk line.
        /// </summary>
        /// <param name="messageId">Article Message-ID (ignored).</param>
        /// <param name="origin">Enqueue origin metadata (ignored).</param>
        /// <param name="articleBytes">Article bytes used for feed and size on real loggers (ignored).</param>
        /// <remarks>
        /// <para>
        /// Provided for contract completeness on <see cref="INntpNewsLog"/>; production transit spool code does not call
        /// this method today.
        /// </para>
        /// <para>Never throws.</para>
        /// </remarks>
        public void LogJunked(string messageId, in NntpSpoolArticleOrigin origin, ReadOnlySpan<byte> articleBytes)
        {
            _ = messageId;
            _ = origin;
            _ = articleBytes;
        }
    }
}
