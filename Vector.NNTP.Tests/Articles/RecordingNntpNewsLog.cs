// <copyright file="RecordingNntpNewsLog.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.Articles.Logging;
using Vector.NNTP.Articles.Storage;

namespace Vector.NNTP.Tests.Articles;

/// <summary>
/// Test double that records the most recent INN news log invocation.
/// </summary>
internal sealed class RecordingNntpNewsLog : INntpNewsLog
{
    /// <summary>
    /// Gets all recorded news log invocations in emission order.
    /// </summary>
    internal List<NewsLogEntry> Entries { get; } = [];

    /// <summary>
    /// Gets the most recent status code written (<c>+</c>, <c>-</c>, <c>c</c>, or <c>j</c>).
    /// </summary>
    internal char? LastStatus => this.Entries.Count == 0 ? null : this.Entries[^1].Status;

    /// <summary>
    /// Gets the most recent Message-ID passed to the logger.
    /// </summary>
    internal string? LastMessageId => this.Entries.Count == 0 ? null : this.Entries[^1].MessageId;

    /// <summary>
    /// Gets the most recent reject reason.
    /// </summary>
    internal string? LastReason => this.Entries.Count == 0 ? null : this.Entries[^1].Reason;

    /// <inheritdoc />
    public void LogAccepted(string messageId, in NntpSpoolArticleOrigin origin, ReadOnlySpan<byte> articleBytes)
    {
        _ = origin;
        _ = articleBytes;
        this.Entries.Add(new NewsLogEntry('+', messageId, null));
    }

    /// <inheritdoc />
    public void LogRejected(
        string messageId,
        in NntpSpoolArticleOrigin origin,
        ReadOnlySpan<byte> articleBytes,
        string reason)
    {
        _ = origin;
        _ = articleBytes;
        this.Entries.Add(new NewsLogEntry('-', messageId, reason));
    }

    /// <inheritdoc />
    public void LogCancelProcessed(string messageId, in NntpSpoolArticleOrigin origin, ReadOnlySpan<byte> articleBytes)
    {
        _ = origin;
        _ = articleBytes;
        this.Entries.Add(new NewsLogEntry('c', messageId, null));
    }

    /// <inheritdoc />
    public void LogJunked(string messageId, in NntpSpoolArticleOrigin origin, ReadOnlySpan<byte> articleBytes)
    {
        _ = origin;
        _ = articleBytes;
        this.Entries.Add(new NewsLogEntry('j', messageId, null));
    }

    /// <summary>
    /// One recorded INN news log invocation.
    /// </summary>
    /// <param name="Status">INN status code (<c>+</c>, <c>-</c>, <c>c</c>, or <c>j</c>).</param>
    /// <param name="MessageId">Transit Message-ID token.</param>
    /// <param name="Reason">Reject reason when <paramref name="Status"/> is <c>-</c>; otherwise <see langword="null"/>.</param>
    internal readonly record struct NewsLogEntry(char Status, string MessageId, string? Reason);
}
