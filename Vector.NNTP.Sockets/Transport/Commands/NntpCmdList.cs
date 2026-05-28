// <copyright file="NntpCmdList.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: LIST command handler.

using Vector.NNTP.Sockets.Responses;
using Vector.NNTP.Sockets.Session;
using Vector.NNTP.Sockets.Storage;

namespace Vector.NNTP.Sockets.Transport.Commands
{
    /// <summary>
    /// Handles LIST and LIST keyword variants.
    /// </summary>
    internal static class NntpCmdList
    {
        /// <summary>
        /// Gets the overview format lines.
        /// </summary>
        /// <returns>The overview format lines.</returns>
        private static readonly string[] OverviewFmtLines =
        [
            "Subject:",
            "From:",
            "Date:",
            "Message-ID:",
            "References:",
            ":bytes",
            ":lines",
            "Xref:full",
        ];

        /// <summary>
        /// Dispatches LIST, LIST ACTIVE, or LIST OVERVIEW.FMT.
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="storage">Article storage (may be null).</param>
        /// <param name="line">Full command line.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns><see langword="true"/> to continue the session.</returns>
        internal static async ValueTask<bool> DispatchAsync(
            NntpSession session,
            INntpArticleStorage? storage,
            string line,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(line);
            string? keyword = NntpCommandLineHelpers.ExtractArgument(line);
            if (string.IsNullOrEmpty(keyword))
            {
                return await ListActiveAsync(session, storage, cancellationToken).ConfigureAwait(false);
            }

            if (keyword.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase))
            {
                return await ListActiveAsync(session, storage, cancellationToken).ConfigureAwait(false);
            }

            if (keyword.Equals("OVERVIEW.FMT", StringComparison.OrdinalIgnoreCase))
            {
                await session.Writer.WriteMultiLineAsync("215 Order of fields in overview database.", OverviewFmtLines, cancellationToken).ConfigureAwait(false);
                return true;
            }

            await NntpReaderErrors.WriteBadSyntax501(session, cancellationToken).ConfigureAwait(false);
            return true;
        }

        /// <summary>
        /// Lists the active newsgroups.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="storage">The article storage.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the active newsgroups are listed.</returns>
        private static async ValueTask<bool> ListActiveAsync(
            NntpSession session,
            INntpArticleStorage? storage,
            CancellationToken cancellationToken)
        {
            if (storage is null)
            {
                await session.Writer.WriteMultiLineAsync("215 Newsgroups in form", [], cancellationToken).ConfigureAwait(false);
                return true;
            }

            IReadOnlyList<string>? groups = await storage.ListActiveAsync(cancellationToken).ConfigureAwait(false);
            if (groups is null)
            {
                await session.Writer.WriteMultiLineAsync("215 Newsgroups in form", [], cancellationToken).ConfigureAwait(false);
                return true;
            }

            await session.Writer.WriteMultiLineAsync("215 Newsgroups in form", groups, cancellationToken).ConfigureAwait(false);
            return true;
        }
    }
}
