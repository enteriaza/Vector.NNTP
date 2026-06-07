// <copyright file="NntpSpoolWriteItem.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: immutable transit spool queue payload carried from enqueue through writer pump processing.

namespace Vector.NNTP.Articles.Storage
{
    /// <summary>
    /// Immutable queue payload describing one transit article awaiting asynchronous spool disk write.
    /// </summary>
    /// <param name="MessageId">
    /// RFC 5322-style message identifier for the article. Used by <see cref="NntpSpoolWriterPump"/> for preprocessing,
    /// postprocessing, history reservation release on failure, and operator-facing logs. Must be non-empty when constructed by
    /// <see cref="NntpSpoolTransitStorage"/>.
    /// </param>
    /// <param name="ArticleBytes">
    /// Raw article body bytes copied at enqueue time. <see cref="NntpSpoolWriteQueue"/> uses
    /// <c>ArticleBytes.Length</c> for byte-budget admission and <see cref="NntpSpoolWriteQueue.NotifyDequeued(int)"/>
    /// accounting even when later preprocess or postprocess stages reject or replace the payload. Callers must not mutate
    /// this array after the item is constructed.
    /// </param>
    /// <param name="MessageIdDigestHex">
    /// Lowercase Blake3 digest hex of <paramref name="MessageId"/>, typically from
    /// <see cref="HistoryDB.Encoding.HistoryKeyEncoder.EncodeHexLower(string)"/>. Precomputed at enqueue so the writer
    /// pump can resolve <see cref="Diagnostics.SpoolDirectoryUtilities.GetArticleFilePath"/> without re-hashing on the
    /// hot path.
    /// </param>
    /// <param name="Origin">
    /// Peer identity and UTC reception timestamp captured at enqueue for SpamAssassin scan header synthesis on the
    /// writer cold path.
    /// </param>
    /// <remarks>
    /// <para><b>Lifecycle:</b></para>
    /// <list type="number">
    /// <item>
    /// <description>
    /// <see cref="NntpSpoolTransitStorage"/> copies incoming bytes and builds a digest when
    /// <c>TakeThisAsync</c> accepts an article.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <see cref="NntpSpoolWriteQueue.TryEnqueue"/> stores the item in the bounded channel when item-count and
    /// byte budgets allow.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <see cref="NntpSpoolWriterPump"/> dequeues the item, runs preprocess and postprocess on
    /// <paramref name="ArticleBytes"/>, and writes the postprocessed payload to disk under
    /// <paramref name="MessageIdDigestHex"/>.
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// Positional record semantics expose <paramref name="MessageId"/>, <paramref name="ArticleBytes"/>,
    /// <paramref name="MessageIdDigestHex"/>, and <paramref name="Origin"/> as init-only properties. The type carries no
    /// behavior; it exists to keep queue, metrics, and writer contracts explicit and allocation-friendly on the transit hot
    /// path.
    /// </para>
    /// </remarks>
    public sealed record NntpSpoolWriteItem(
        string MessageId,
        byte[] ArticleBytes,
        string MessageIdDigestHex,
        NntpSpoolArticleOrigin Origin);
}
