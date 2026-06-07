// <copyright file="NntpNewsLogFormatter.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: INN pathlog/news line assembly for transit spool accept/reject logging.

using System.Globalization;
using System.Text;

namespace Vector.NNTP.Articles.Logging
{
    /// <summary>
    /// Builds INN-compatible <c>pathlog/news</c> log lines for article accept, reject, cancel, and junk events.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called by <see cref="NntpNewsLog"/> immediately before Serilog writes a line to <c>{LogDir}/news</c>. Each public
    /// formatter returns a single line without a trailing newline; the Serilog template supplies the record terminator.
    /// </para>
    /// <para><b>Timestamp semantics:</b></para>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// Production callers pass <see cref="DateTimeOffset.Now"/> at the instant the pipeline outcome is known (event time,
    /// not article enqueue time).
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <see cref="FormatTimestamp"/> converts the supplied offset to local time and renders
    /// <c>MMM dd HH:mm:ss.fff</c> using <see cref="CultureInfo.InvariantCulture"/> for the month and punctuation.
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// <b>Message-ID normalization:</b> <see cref="NormalizeMessageId"/> trims input and emits exactly one pair of angle
    /// brackets so transit command tokens that already include brackets are not double-wrapped.
    /// </para>
    /// <para>
    /// <b>Downstream site placeholder:</b> Accept and junk lines append <see cref="NntpNewsLogFeedNames.UnknownSite"/>
    /// until newsfeeds routing supplies real site lists.
    /// </para>
    /// <para><b>Thread safety:</b> All members are static and stateless; safe for concurrent writer pumps.</para>
    /// </remarks>
    internal static class NntpNewsLogFormatter
    {
        /// <summary>
        /// Maximum number of characters retained in a minus-line reason after <see cref="SanitizeReason"/> processing.
        /// </summary>
        /// <remarks>
        /// Longer sanitized reasons are truncated without an ellipsis so INN lines stay single-line and bounded for log
        /// aggregation tools.
        /// </remarks>
        private const int MaxReasonLength = 512;

        /// <summary>
        /// Formats an accepted article plus line after successful spool commit.
        /// </summary>
        /// <param name="timestamp">Event timestamp, typically <see cref="DateTimeOffset.Now"/> at log time.</param>
        /// <param name="feed">Incoming feed identifier from <see cref="NntpNewsFeedResolver"/>.</param>
        /// <param name="messageId">Article Message-ID with or without angle brackets.</param>
        /// <param name="articleByteLength">Total committed article size in bytes (bare decimal, no brackets).</param>
        /// <returns>
        /// A single INN news log line of the form
        /// <c>{timestamp} + {feed} &lt;message-id&gt; {size} ?</c> without a trailing newline.
        /// </returns>
        /// <remarks>
        /// <para>
        /// The trailing site token is always <see cref="NntpNewsLogFeedNames.UnknownSite"/> in the initial transit spool
        /// implementation.
        /// </para>
        /// <para>Does not throw for normal string inputs; see <see cref="NormalizeMessageId"/> for Message-ID validation.</para>
        /// </remarks>
        internal static string FormatAccepted(
            DateTimeOffset timestamp,
            string feed,
            string messageId,
            int articleByteLength)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{FormatTimestamp(timestamp)} + {feed} {NormalizeMessageId(messageId)} {articleByteLength} {NntpNewsLogFeedNames.UnknownSite}");
        }

        /// <summary>
        /// Formats a junk-newsgroup accept line for articles filed to trash instead of normal groups.
        /// </summary>
        /// <param name="timestamp">Event timestamp, typically <see cref="DateTimeOffset.Now"/> at log time.</param>
        /// <param name="feed">Incoming feed identifier from <see cref="NntpNewsFeedResolver"/>.</param>
        /// <param name="messageId">Article Message-ID with or without angle brackets.</param>
        /// <param name="articleByteLength">Total article size in bytes (bare decimal, no brackets).</param>
        /// <returns>
        /// A single INN news log line of the form
        /// <c>{timestamp} j {feed} &lt;message-id&gt; {size} ?</c> without a trailing newline.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Reserved for future accepted-to-junk-newsgroup filing. The formatter is unit-tested in v1 even though transit
        /// spool code does not call <see cref="INntpNewsLog.LogJunked"/> yet.
        /// </para>
        /// <para>Does not throw for normal string inputs; see <see cref="NormalizeMessageId"/> for Message-ID validation.</para>
        /// </remarks>
        internal static string FormatJunked(
            DateTimeOffset timestamp,
            string feed,
            string messageId,
            int articleByteLength)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{FormatTimestamp(timestamp)} j {feed} {NormalizeMessageId(messageId)} {articleByteLength} {NntpNewsLogFeedNames.UnknownSite}");
        }

        /// <summary>
        /// Formats a rejected article minus line with a sanitized operator-facing reason.
        /// </summary>
        /// <param name="timestamp">Event timestamp, typically <see cref="DateTimeOffset.Now"/> at log time.</param>
        /// <param name="feed">Incoming feed identifier from <see cref="NntpNewsFeedResolver"/>.</param>
        /// <param name="messageId">Article Message-ID with or without angle brackets.</param>
        /// <param name="reason">Raw failure reason from storage enqueue, preprocessing, postprocessing, or write failure.</param>
        /// <returns>
        /// A single INN news log line of the form
        /// <c>{timestamp} - {feed} &lt;message-id&gt; {reason}</c> without a trailing newline.
        /// </returns>
        /// <remarks>
        /// <para>
        /// <paramref name="reason"/> is passed through <see cref="SanitizeReason"/> before emission. Write failures in
        /// production typically supply only an exception type name; full exception detail remains in application logs.
        /// </para>
        /// <para>Does not throw for normal string inputs; see <see cref="NormalizeMessageId"/> for Message-ID validation.</para>
        /// </remarks>
        internal static string FormatRejected(
            DateTimeOffset timestamp,
            string feed,
            string messageId,
            string reason)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{FormatTimestamp(timestamp)} - {feed} {NormalizeMessageId(messageId)} {SanitizeReason(reason)}");
        }

        /// <summary>
        /// Formats a processed cancel line after a cancel article is committed to the spool.
        /// </summary>
        /// <param name="timestamp">Event timestamp, typically <see cref="DateTimeOffset.Now"/> at log time.</param>
        /// <param name="feed">Incoming feed identifier from <see cref="NntpNewsFeedResolver"/>.</param>
        /// <param name="messageId">Cancel article Message-ID with or without angle brackets.</param>
        /// <param name="cancelTargetMessageId">
        /// Target Message-ID parsed from the <c>Control</c> header, with or without angle brackets. Pass
        /// <see cref="NntpNewsLogFeedNames.UnknownFeed"/> when the target could not be parsed (renders as bare
        /// <c>?</c> without brackets).
        /// </param>
        /// <returns>
        /// A single INN news log line of the form
        /// <c>{timestamp} c {feed} &lt;message-id&gt; Cancelling &lt;target&gt;</c>, or
        /// <c>... Cancelling ?</c> when the target is unknown.
        /// </returns>
        /// <remarks>
        /// <para>
        /// When <paramref name="cancelTargetMessageId"/> equals <see cref="NntpNewsLogFeedNames.UnknownFeed"/>, the
        /// literal question mark is emitted without angle brackets to match INN cancel lines for unparseable targets.
        /// </para>
        /// <para>Does not throw for normal string inputs; see <see cref="NormalizeMessageId"/> for Message-ID validation.</para>
        /// </remarks>
        internal static string FormatCancelProcessed(
            DateTimeOffset timestamp,
            string feed,
            string messageId,
            string cancelTargetMessageId)
        {
            string target = cancelTargetMessageId == NntpNewsLogFeedNames.UnknownFeed
                ? NntpNewsLogFeedNames.UnknownFeed
                : NormalizeMessageId(cancelTargetMessageId);
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{FormatTimestamp(timestamp)} c {feed} {NormalizeMessageId(messageId)} Cancelling {target}");
        }

        /// <summary>
        /// Formats an event timestamp as the INN news log line prefix.
        /// </summary>
        /// <param name="timestamp">Instant to render, usually <see cref="DateTimeOffset.Now"/> from production callers.</param>
        /// <returns>
        /// Local-time string formatted as <c>MMM dd HH:mm:ss.fff</c> (for example <c>Jun 07 21:55:01.102</c>).
        /// </returns>
        /// <remarks>
        /// <para>
        /// Uses <see cref="DateTimeOffset.ToLocalTime"/> so operators see wall-clock time on the host writing the log.
        /// Month abbreviations and numeric fields use <see cref="CultureInfo.InvariantCulture"/>.
        /// </para>
        /// <para>Never throws.</para>
        /// </remarks>
        internal static string FormatTimestamp(DateTimeOffset timestamp)
        {
            DateTime local = timestamp.ToLocalTime().DateTime;
            return local.ToString("MMM dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Normalizes a Message-ID token for INN news log output with exactly one pair of angle brackets.
        /// </summary>
        /// <param name="messageId">Raw Message-ID from transit commands or header values.</param>
        /// <returns>
        /// Trimmed Message-ID surrounded by a single pair of angle brackets (for example <c>&lt;msgid@example.com&gt;</c>).
        /// </returns>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="messageId"/> is <see langword="null"/> or empty.
        /// </exception>
        /// <remarks>
        /// <para>
        /// Leading and trailing whitespace is removed. If the input is already bracketed, the outer brackets are stripped,
        /// the inner token is trimmed, and a fresh bracket pair is applied so output never contains doubled brackets.
        /// </para>
        /// </remarks>
        internal static string NormalizeMessageId(string messageId)
        {
            ArgumentException.ThrowIfNullOrEmpty(messageId);
            ReadOnlySpan<char> span = messageId.AsSpan().Trim();
            if (span.Length >= 2 && span[0] == '<' && span[^1] == '>')
            {
                span = span[1..^1].Trim();
            }

            return string.Create(CultureInfo.InvariantCulture, $"<{span}>");
        }

        /// <summary>
        /// Collapses a reject reason to a single log-safe line with bounded length.
        /// </summary>
        /// <param name="reason">Raw failure reason from storage, preprocessing, postprocessing, or write failure.</param>
        /// <returns>
        /// Sanitized single-line reason text. Returns the literal <c>Rejected</c> when
        /// <paramref name="reason"/> is null, empty, or whitespace-only.
        /// </returns>
        /// <remarks>
        /// <para><b>Normalization steps:</b></para>
        /// <list type="number">
        /// <item><description>Trim leading and trailing whitespace from <paramref name="reason"/>.</description></item>
        /// <item>
        /// <description>
        /// Replace carriage return, line feed, and tab characters with a single ASCII space, collapsing consecutive
        /// whitespace runs to one space.
        /// </description>
        /// </item>
        /// <item>
        /// <description>
        /// Truncate to <see cref="MaxReasonLength"/> characters when the result exceeds the cap (no ellipsis appended).
        /// </description>
        /// </item>
        /// </list>
        /// <para>Never throws.</para>
        /// </remarks>
        internal static string SanitizeReason(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return "Rejected";
            }

            StringBuilder builder = new(reason.Length);
            bool previousWasWhitespace = false;
            foreach (char c in reason.Trim())
            {
                if (c is '\r' or '\n' or '\t')
                {
                    if (!previousWasWhitespace)
                    {
                        _ = builder.Append(' ');
                        previousWasWhitespace = true;
                    }

                    continue;
                }

                _ = builder.Append(c);
                previousWasWhitespace = char.IsWhiteSpace(c);
            }

            if (builder.Length > MaxReasonLength)
            {
                builder.Length = MaxReasonLength;
            }

            return builder.ToString();
        }
    }
}
