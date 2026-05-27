// <copyright file="INntpArticleStorage.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: reader article persistence contract (implemented by hosts).

namespace Vector.NNTP.Sockets.Storage
{
    /// <summary>
    /// Reader article retrieval and POST persistence; production hosts delegate to distributed storage workers.
    /// </summary>
    public interface INntpArticleStorage
    {
        /// <summary>
        /// Selects a group and returns estimated article range for GROUP response.
        /// </summary>
        /// <param name="groupName">Newsgroup name.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Group metadata or null when unknown.</returns>
        ValueTask<NntpGroupInfo?> SelectGroupAsync(string groupName, CancellationToken cancellationToken);

        /// <summary>
        /// Retrieves article bytes for ARTICLE/HEAD/BODY/STAT.
        /// </summary>
        /// <param name="groupName">Selected group or null for message-id lookup.</param>
        /// <param name="articleNumber">Article number when applicable.</param>
        /// <param name="messageId">Message-ID when applicable.</param>
        /// <param name="part">Article part requested.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Article payload or null when not found.</returns>
        ValueTask<NntpArticlePayload?> GetArticleAsync(
            string? groupName,
            long? articleNumber,
            string? messageId,
            NntpArticlePart part,
            CancellationToken cancellationToken);

        /// <summary>
        /// Stores a posted article body (headers + dot-stuffed body already normalized by handler).
        /// </summary>
        /// <param name="articleBytes">Full article bytes.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Assigned message-id or error indication.</returns>
        ValueTask<NntpPostResult> PostArticleAsync(ReadOnlyMemory<byte> articleBytes, CancellationToken cancellationToken);

        /// <summary>
        /// Lists active newsgroups for LIST ACTIVE (one name per line in the multi-line body).
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Group names, or <see langword="null"/> when the host does not implement LIST ACTIVE.</returns>
        ValueTask<IReadOnlyList<string>?> ListActiveAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<string>?>(null);

        /// <summary>
        /// Lists article numbers in the selected group for LISTGROUP.
        /// </summary>
        /// <param name="groupName">Selected newsgroup.</param>
        /// <param name="rangeLow">Inclusive low bound (optional).</param>
        /// <param name="rangeHigh">Inclusive high bound (optional).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Article numbers, or <see langword="null"/> when not implemented.</returns>
        ValueTask<IReadOnlyList<long>?> ListGroupAsync(
            string groupName,
            long? rangeLow,
            long? rangeHigh,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<long>?>(null);

        /// <summary>
        /// Retrieves OVER/XOVER overview lines for the current selection.
        /// </summary>
        /// <param name="groupName">Selected newsgroup.</param>
        /// <param name="rangeLow">Inclusive low article number (optional).</param>
        /// <param name="rangeHigh">Inclusive high article number (optional).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Overview tab-separated lines, or <see langword="null"/> when not implemented.</returns>
        ValueTask<IReadOnlyList<string>?> GetOverviewAsync(
            string groupName,
            long? rangeLow,
            long? rangeHigh,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<string>?>(null);

        /// <summary>
        /// Retrieves HDR/XHDR header field values for the current selection.
        /// </summary>
        /// <param name="groupName">Selected newsgroup.</param>
        /// <param name="headerField">Header field name (without colon).</param>
        /// <param name="rangeLow">Inclusive low article number (optional).</param>
        /// <param name="rangeHigh">Inclusive high article number (optional).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Header lines (<c>number value</c>), or <see langword="null"/> when not implemented.</returns>
        ValueTask<IReadOnlyList<string>?> GetHeadersAsync(
            string groupName,
            string headerField,
            long? rangeLow,
            long? rangeHigh,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<string>?>(null);

        /// <summary>
        /// Returns the next article number after <paramref name="currentArticleNumber"/> in <paramref name="groupName"/>.
        /// </summary>
        /// <param name="groupName">Selected newsgroup.</param>
        /// <param name="currentArticleNumber">Current article number.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>
        /// The next article number, <c>-1</c> when no next article exists, or <see langword="null"/> when not implemented.
        /// </returns>
        ValueTask<long?> GetNextArticleNumberAsync(
            string groupName,
            long currentArticleNumber,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<long?>(null);

        /// <summary>
        /// Returns the previous article number before <paramref name="currentArticleNumber"/> in <paramref name="groupName"/>.
        /// </summary>
        /// <param name="groupName">Selected newsgroup.</param>
        /// <param name="currentArticleNumber">Current article number.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>
        /// The previous article number, <c>-1</c> when no previous article exists, or <see langword="null"/> when not implemented.
        /// </returns>
        ValueTask<long?> GetPreviousArticleNumberAsync(
            string groupName,
            long currentArticleNumber,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<long?>(null);

        /// <summary>
        /// Resolves the Message-ID for an article (STAT response line).
        /// </summary>
        /// <param name="groupName">Selected newsgroup or null for global lookup.</param>
        /// <param name="articleNumber">Article number when applicable.</param>
        /// <param name="messageId">Message-ID when applicable.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Message-ID including angle brackets, or <see langword="null"/> when not found or not implemented.</returns>
        ValueTask<string?> GetArticleMessageIdAsync(
            string? groupName,
            long? articleNumber,
            string? messageId,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>(null);
    }

    /// <summary>
    /// Article part selector for retrieval commands.
    /// </summary>
    public enum NntpArticlePart
    {
        /// <summary>Full article.</summary>
        Full = 0,

        /// <summary>Headers only.</summary>
        Head = 1,

        /// <summary>Body only.</summary>
        Body = 2,

        /// <summary>STAT (headers summary).</summary>
        Stat = 3,
    }

    /// <summary>
    /// GROUP command metadata.
    /// </summary>
    public sealed class NntpGroupInfo
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NntpGroupInfo"/> class.
        /// </summary>
        /// <param name="name">Group name.</param>
        /// <param name="articleCount">Estimated count.</param>
        /// <param name="lowWater">Low water mark.</param>
        /// <param name="highWater">High water mark.</param>
        public NntpGroupInfo(string name, int articleCount, long lowWater, long highWater)
        {
            this.Name = name;
            this.ArticleCount = articleCount;
            this.LowWater = lowWater;
            this.HighWater = highWater;
        }

        /// <summary>Gets the group name.</summary>
        public string Name { get; }

        /// <summary>Gets the estimated article count.</summary>
        public int ArticleCount { get; }

        /// <summary>Gets the low water mark.</summary>
        public long LowWater { get; }

        /// <summary>Gets the high water mark.</summary>
        public long HighWater { get; }
    }

    /// <summary>
    /// Retrieved article bytes and number for NNTP multi-line responses.
    /// </summary>
    public sealed class NntpArticlePayload
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NntpArticlePayload"/> class.
        /// </summary>
        /// <param name="articleNumber">Article number in group.</param>
        /// <param name="body">Article bytes (may include headers per part).</param>
        public NntpArticlePayload(long articleNumber, ReadOnlyMemory<byte> body)
        {
            this.ArticleNumber = articleNumber;
            this.Body = body;
        }

        /// <summary>Gets the article number.</summary>
        public long ArticleNumber { get; }

        /// <summary>Gets the payload bytes.</summary>
        public ReadOnlyMemory<byte> Body { get; }
    }

    /// <summary>
    /// POST command outcome.
    /// </summary>
    public readonly struct NntpPostResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NntpPostResult"/> struct.
        /// </summary>
        /// <param name="success">Whether POST succeeded.</param>
        /// <param name="messageId">Assigned message-id on success.</param>
        public NntpPostResult(bool success, string? messageId)
        {
            this.Success = success;
            this.MessageId = messageId;
        }

        /// <summary>Gets whether POST succeeded.</summary>
        public bool Success { get; }

        /// <summary>Gets the assigned message-id.</summary>
        public string? MessageId { get; }
    }
}
