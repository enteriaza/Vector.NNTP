// <copyright file="NntpCmdOver.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: OVER and XOVER command handler.

using Vector.NNTP.Sockets.Responses;
using Vector.NNTP.Sockets.Session;
using Vector.NNTP.Sockets.Storage;

namespace Vector.NNTP.Sockets.Transport.Commands
{
    /// <summary>
    /// Handles OVER and XOVER overview retrieval commands.
    /// </summary>
    internal static class NntpCmdOver
    {
        /// <summary>
        /// Returns overview database lines for a range of articles in the selected group.
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
            if (string.IsNullOrEmpty(session.State.SelectedGroup))
            {
                await NntpReaderErrors.WriteNoGroupSelected412(session, cancellationToken).ConfigureAwait(false);
                return true;
            }

            if (storage is null)
            {
                await NntpReaderErrors.WriteServiceUnavailable503(session, cancellationToken).ConfigureAwait(false);
                return true;
            }

            ParseRange(NntpCommandLineHelpers.ExtractArgument(line), out long? rangeLow, out long? rangeHigh);
            IReadOnlyList<string>? lines = await storage.GetOverviewAsync(
                session.State.SelectedGroup,
                rangeLow,
                rangeHigh,
                cancellationToken).ConfigureAwait(false);
            if (lines is null)
            {
                await session.Writer.WriteMultiLineAsync("224 Overview data follow", [], cancellationToken).ConfigureAwait(false);
                return true;
            }

            await session.Writer.WriteMultiLineAsync("224 Overview data follow", lines, cancellationToken).ConfigureAwait(false);
            return true;
        }

        private static void ParseRange(string? argument, out long? rangeLow, out long? rangeHigh)
        {
            rangeLow = null;
            rangeHigh = null;
            if (string.IsNullOrWhiteSpace(argument))
            {
                return;
            }

            int dash = argument.IndexOf('-', StringComparison.Ordinal);
            if (dash < 0)
            {
                if (long.TryParse(argument, NumberStyles.None, CultureInfo.InvariantCulture, out long single))
                {
                    rangeLow = single;
                    rangeHigh = single;
                }

                return;
            }

            ReadOnlySpan<char> left = argument.AsSpan(0, dash).Trim();
            ReadOnlySpan<char> right = argument.AsSpan(dash + 1).Trim();
            if (left.Length > 0 && long.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out long low))
            {
                rangeLow = low;
            }

            if (right.Length > 0 && long.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out long high))
            {
                rangeHigh = high;
            }
        }
    }
}
