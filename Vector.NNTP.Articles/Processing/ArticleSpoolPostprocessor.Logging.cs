// <copyright file="ArticleSpoolPostprocessor.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH (Tier 2 logging): LoggerMessage definitions for spamd fail-open events on eligible articles.

namespace Vector.NNTP.Articles.Processing
{
    /// <summary>
    /// LoggerMessage definitions for <see cref="ArticleSpoolPostprocessor"/> spamd fail-open diagnostics.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Emitted only when <see cref="ArticleSpoolPostprocessor"/> invokes SpamAssassin and the remote check faults.
    /// Header validation and filter rejections are returned to <see cref="Storage.NntpSpoolWriterPump"/> as failure
    /// results without logging from this partial.
    /// </para>
    /// <para><b>Fail-open policy:</b> Protocol and unexpected spamd errors accept the article; warnings are logged here.</para>
    /// </remarks>
    internal sealed partial class ArticleSpoolPostprocessor
    {
        /// <summary>
        /// Logs a spamd protocol failure that fails open and accepts the article.
        /// </summary>
        /// <param name="logger">Category logger.</param>
        /// <param name="exception">spamd protocol exception.</param>
        /// <param name="messageId">Transit Message-ID.</param>
        [LoggerMessage(
            EventId = 1,
            Level = LogLevel.Warning,
            Message = "Spamd check failed open for Message-ID {MessageId}; accepting article.")]
        private static partial void LogSpamdFailedOpen(ILogger logger, Exception exception, string messageId);

        /// <summary>
        /// Logs an unexpected spamd failure that fails open and accepts the article.
        /// </summary>
        /// <param name="logger">Category logger.</param>
        /// <param name="exception">Unexpected exception.</param>
        /// <param name="messageId">Transit Message-ID.</param>
        [LoggerMessage(
            EventId = 2,
            Level = LogLevel.Warning,
            Message = "Unexpected spamd check failure for Message-ID {MessageId}; accepting article (fail-open).")]
        private static partial void LogSpamdUnexpectedFailure(ILogger logger, Exception exception, string messageId);
    }
}
