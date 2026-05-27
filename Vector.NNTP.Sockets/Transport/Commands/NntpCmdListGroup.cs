// <copyright file="NntpCmdListGroup.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: LISTGROUP command handler.

namespace Vector.NNTP.Sockets.Transport.Commands
{
    using Responses;
    using Session;
    using Storage;

    /// <summary>
    /// Handles the NNTP LISTGROUP command.
    /// </summary>
    internal static class NntpCmdListGroup
    {
        /// <summary>
        /// Lists article numbers in the selected group, optionally constrained by a range.
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
            IReadOnlyList<long>? numbers = await storage.ListGroupAsync(
                session.State.SelectedGroup,
                rangeLow,
                rangeHigh,
                cancellationToken).ConfigureAwait(false);
            if (numbers is null)
            {
                await NntpReaderErrors.WriteServiceUnavailable503(session, cancellationToken).ConfigureAwait(false);
                return true;
            }

            long low = session.State.SelectedGroupLowWater ?? 0;
            long high = session.State.SelectedGroupHighWater ?? 0;
            long count = numbers.Count;
            string[] lines = new string[numbers.Count];
            for (int i = 0; i < numbers.Count; i++)
            {
                lines[i] = numbers[i].ToString(CultureInfo.InvariantCulture);
            }

            await session.Writer.WriteMultiLineAsync(
                $"215 {count} {low} {high} {session.State.SelectedGroup}",
                lines,
                cancellationToken).ConfigureAwait(false);
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
