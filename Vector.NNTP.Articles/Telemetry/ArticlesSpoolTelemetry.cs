// <copyright file="ArticlesSpoolTelemetry.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// ArticlesSpoolTelemetry.cs -- OpenTelemetry activity source for transit spool pipeline operations.

namespace Vector.NNTP.Articles.Telemetry
{
    /// <summary>
    /// OpenTelemetry-compatible <see cref="ActivitySource"/> for transit spool preprocess, postprocess, write, and
    /// HistoryDB coordination.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Role:</b> Central tracing surface for the <c>Vector.NNTP.Articles</c> assembly. Spans bracket per-item work in
    /// <see cref="Storage.NntpSpoolWriterPump"/> and SpamAssassin checks in
    /// <see cref="Processing.ArticleSpoolPostprocessor"/>. Complements counters and histograms on
    /// <see cref="Metrics.NntpSpoolMetrics"/>.
    /// </para>
    /// <para>
    /// <b>Host registration:</b> Add <see cref="SourceName"/> to the host OpenTelemetry tracer provider
    /// (<c>builder.Tracing.AddSource(ArticlesSpoolTelemetry.SourceName)</c>) to collect spans. Without registration,
    /// <c>ActivitySource.StartActivity(...)</c> returns <see langword="null"/> and callers treat activities as no-ops.
    /// </para>
    /// <para><b>Span catalog:</b></para>
    /// <list type="table">
    /// <listheader><term>Operation name</term><description>Emitter and <see cref="ActivityKind"/></description></listheader>
    /// <item><term><c>nntp.spool.preprocess</c></term><description><see cref="Storage.NntpSpoolWriterPump"/> — Internal.</description></item>
    /// <item><term><c>nntp.spool.postprocess</c></term><description><see cref="Storage.NntpSpoolWriterPump"/> — Internal.</description></item>
    /// <item><term><c>nntp.spool.spamd.check</c></term><description><see cref="Processing.ArticleSpoolPostprocessor"/> — Internal.</description></item>
    /// <item><term><c>nntp.spool.write</c></term><description><see cref="Storage.NntpSpoolWriterPump"/> disk I/O — Client.</description></item>
    /// <item><term><c>nntp.spool.history.release</c></term><description><see cref="Storage.NntpSpoolWriterPump"/> cleanup — Internal.</description></item>
    /// <item><term><c>nntp.spool.history.commit</c></term><description><see cref="Storage.NntpSpoolWriterPump"/> post-write commit — Internal.</description></item>
    /// </list>
    /// <para>
    /// <b>Error status:</b> Emitters call <see cref="Activity.SetStatus(ActivityStatusCode, string?)"/> on unexpected backend
    /// faults (write I/O, history commit/release failures). Expected article rejections (header, spam classification) do not
    /// mark spans as errored.
    /// </para>
    /// <para>
    /// <b>Privacy:</b> Spans do not attach Message-IDs, peer addresses, or article payloads. Structured logging remains on
    /// <see cref="ILogger"/> categories and <see cref="Logging.INntpNewsLog"/>.
    /// </para>
    /// <para><b>Threading:</b> Static read-only <see cref="ActivitySource"/>; safe for concurrent writer pump workers.</para>
    /// </remarks>
    internal static class ArticlesSpoolTelemetry
    {
        /// <summary>
        /// Logical name registered with the host OpenTelemetry tracer provider via <c>AddSource</c>.
        /// </summary>
        /// <value>Literal <c>Vector.NNTP.Articles</c>.</value>
        internal const string SourceName = "Vector.NNTP.Articles";

        /// <summary>
        /// Operation name for preprocess spans emitted by <see cref="Storage.NntpSpoolWriterPump"/>.
        /// </summary>
        internal const string PreprocessOperation = "nntp.spool.preprocess";

        /// <summary>
        /// Operation name for postprocess spans emitted by <see cref="Storage.NntpSpoolWriterPump"/>.
        /// </summary>
        internal const string PostprocessOperation = "nntp.spool.postprocess";

        /// <summary>
        /// Operation name for SpamAssassin check spans in <see cref="Processing.ArticleSpoolPostprocessor"/>.
        /// </summary>
        internal const string SpamdCheckOperation = "nntp.spool.spamd.check";

        /// <summary>
        /// Operation name for atomic spool write spans emitted by <see cref="Storage.NntpSpoolWriterPump"/>.
        /// </summary>
        internal const string WriteOperation = "nntp.spool.write";

        /// <summary>
        /// Operation name for HistoryDB reservation release spans.
        /// </summary>
        internal const string HistoryReleaseOperation = "nntp.spool.history.release";

        /// <summary>
        /// Operation name for HistoryDB reservation commit spans after successful spool persistence.
        /// </summary>
        internal const string HistoryCommitOperation = "nntp.spool.history.commit";

        /// <summary>
        /// Shared activity source for spool pipeline operations in this assembly.
        /// </summary>
        /// <value>
        /// Singleton <see cref="ActivitySource"/> constructed with <see cref="SourceName"/> and
        /// <see cref="Utilities.Diagnostics.AssemblyInfoUtilities.ApplicationVersion"/>.
        /// </value>
        internal static ActivitySource ActivitySource { get; } = new(
            SourceName,
            Utilities.Diagnostics.AssemblyInfoUtilities.ApplicationVersion);
    }
}
