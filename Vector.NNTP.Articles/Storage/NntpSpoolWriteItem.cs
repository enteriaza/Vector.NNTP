// <copyright file="NntpSpoolWriteItem.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: immutable transit spool queue payload carried from enqueue through writer pump processing.

namespace Vector.NNTP.Articles.Storage
{
    /// <summary>
    /// Immutable queue payload describing one transit article awaiting asynchronous spool disk write by
    /// <see cref="NntpSpoolWriterPump"/>.
    /// </summary>
    /// <param name="MessageId">
    /// RFC 5322-style <c>Message-ID</c> header value for the article. Used by <see cref="NntpSpoolWriterPump"/> for
    /// preprocessing, postprocessing, HistoryDB release/commit, structured pump logs, and
    /// <see cref="Logging.INntpNewsLog"/> lines. Must be non-empty when constructed by
    /// <see cref="NntpSpoolTransitStorage"/> (validated before digest computation and enqueue).
    /// </param>
    /// <param name="ArticleBytes">
    /// Raw article bytes copied at enqueue time (headers, header/body separator, and optional body as received on the
    /// wire). <see cref="NntpSpoolWriteQueue"/> uses <c>ArticleBytes.Length</c> for byte-budget admission and
    /// <see cref="NntpSpoolWriteQueue.NotifyDequeued(int)"/> accounting even when later preprocess or postprocess stages
    /// reject the article or replace the payload bytes written to disk. Callers must not mutate this array after the item
    /// is constructed.
    /// </param>
    /// <param name="MessageIdDigestHex">
    /// Lowercase Blake3 digest hex of <paramref name="MessageId"/>, produced by
    /// <see cref="HistoryDB.Encoding.HistoryKeyEncoder.EncodeHexLower(string)"/> at enqueue. Precomputed so
    /// <see cref="NntpSpoolWriterPump"/> can resolve
    /// <see cref="Diagnostics.SpoolDirectoryUtilities.GetArticleFilePath"/> without re-hashing on the hot path. Must
    /// satisfy the lowercase hexadecimal constraints enforced by spool path utilities.
    /// </param>
    /// <param name="Origin">
    /// Peer identity and UTC reception metadata captured at enqueue. Carried on every queued item for
    /// <see cref="Metrics.NntpSpoolMetrics"/> rejection/acceptance rollups, <see cref="Logging.INntpNewsLog"/> feed
    /// resolution, and optional SpamAssassin scan header synthesis in
    /// <see cref="Processing.ArticleSpoolPostprocessor"/> regardless of whether a spam check runs for the article.
    /// </param>
    /// <remarks>
    /// <para><b>Role:</b> Value-type queue element with no behavior. Keeps transit admission, bounded queue accounting,
    /// writer pump, metrics, and news-log contracts explicit while avoiding extra allocations beyond the
    /// <paramref name="ArticleBytes"/> copy performed by <see cref="NntpSpoolTransitStorage"/>.</para>
    /// <para><b>Lifecycle:</b></para>
    /// <list type="number">
    /// <item>
    /// <description>
    /// <see cref="NntpSpoolTransitStorage.TakeThisAsync"/> validates size limits, computes
    /// <paramref name="MessageIdDigestHex"/>, copies incoming bytes to <paramref name="ArticleBytes"/>, and builds an
    /// instance when TAKETHIS (and IHAVE body transfer) accepts an article for spool persistence.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <see cref="NntpSpoolWriteQueue.TryEnqueue"/> stores the item in the bounded channel when item-count and byte
    /// budgets allow.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <see cref="NntpSpoolWriterPump"/> dequeues the item, runs preprocess and postprocess on
    /// <paramref name="ArticleBytes"/>, writes the postprocessed payload to disk under
    /// <paramref name="MessageIdDigestHex"/>, and always calls
    /// <see cref="NntpSpoolWriteQueue.NotifyDequeued(int)"/> with the original enqueued byte length in a
    /// <c>finally</c> block.
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// <b>Field usage summary:</b> <paramref name="MessageId"/> drives HistoryDB and operator-facing identifiers;
    /// <paramref name="MessageIdDigestHex"/> drives on-disk path layout only; <paramref name="Origin"/> drives feed and
    /// spam-scan metadata; <paramref name="ArticleBytes"/> is the mutable-through-preprocess/postprocess input buffer
    /// but must remain unchanged in length for queue byte accounting after enqueue.
    /// </para>
    /// <para>
    /// Positional record semantics expose <paramref name="MessageId"/>, <paramref name="ArticleBytes"/>,
    /// <paramref name="MessageIdDigestHex"/>, and <paramref name="Origin"/> as init-only properties with synthesized
    /// equality. The type does not validate invariants in its constructor; producers are responsible for non-empty
    /// message identifiers, digest format, and array immutability after construction.
    /// </para>
    /// <para><b>Threading:</b> After enqueue, instances may be read concurrently by one pump worker that dequeued the
    /// item. The shared <paramref name="ArticleBytes"/> array must not be mutated by producers once the item is queued.</para>
    /// </remarks>
    public sealed record NntpSpoolWriteItem(
        string MessageId,
        byte[] ArticleBytes,
        string MessageIdDigestHex,
        NntpSpoolArticleOrigin Origin);
}
