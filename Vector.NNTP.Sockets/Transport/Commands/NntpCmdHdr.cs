// <copyright file="NntpCmdHdr.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: HDR and XHDR command handler.

namespace Vector.NNTP.Sockets.Transport.Commands
{
    using Protocol;
    using Responses;
    using Session;
    using Storage;

    /// <summary>
    /// Handles HDR and XHDR header field retrieval commands.
    /// </summary>
    internal static class NntpCmdHdr
    {
        /// <summary>
        /// Returns header field values for a range of articles in the selected group.
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

            if (!TryParseHdrArguments(NntpCommandLineHelpers.ExtractArgument(line), out string headerField, out long? rangeLow, out long? rangeHigh))
            {
                await NntpReaderErrors.WriteBadSyntax501(session, cancellationToken).ConfigureAwait(false);
                return true;
            }

            if (!HdrParameterSyntax.IsValid(headerField))
            {
                await NntpReaderErrors.WriteBadSyntax501(session, cancellationToken).ConfigureAwait(false);
                return true;
            }

            IReadOnlyList<string>? lines = await storage.GetHeadersAsync(
                session.State.SelectedGroup,
                headerField,
                rangeLow,
                rangeHigh,
                cancellationToken).ConfigureAwait(false);
            if (lines is null)
            {
                await session.Writer.WriteMultiLineAsync("225 Headers follow", Array.Empty<string>(), cancellationToken).ConfigureAwait(false);
                return true;
            }

            await session.Writer.WriteMultiLineAsync("225 Headers follow", lines, cancellationToken).ConfigureAwait(false);
            return true;
        }

        private static bool TryParseHdrArguments(
            string? argument,
            out string headerField,
            out long? rangeLow,
            out long? rangeHigh)
        {
            headerField = string.Empty;
            rangeLow = null;
            rangeHigh = null;
            if (string.IsNullOrWhiteSpace(argument))
            {
                return false;
            }

            int firstSpace = argument.IndexOf(' ');
            if (firstSpace < 0)
            {
                headerField = argument.Trim();
                return true;
            }

            headerField = argument[..firstSpace].Trim();
            string rangeText = argument[(firstSpace + 1)..].Trim();
            if (string.IsNullOrEmpty(rangeText))
            {
                return true;
            }

            int dash = rangeText.IndexOf('-', StringComparison.Ordinal);
            if (dash < 0)
            {
                if (long.TryParse(rangeText, NumberStyles.None, CultureInfo.InvariantCulture, out long single))
                {
                    rangeLow = single;
                    rangeHigh = single;
                    return true;
                }

                return false;
            }

            ReadOnlySpan<char> left = rangeText.AsSpan(0, dash).Trim();
            ReadOnlySpan<char> right = rangeText.AsSpan(dash + 1).Trim();
            if (left.Length > 0 && long.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out long low))
            {
                rangeLow = low;
            }

            if (right.Length > 0 && long.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out long high))
            {
                rangeHigh = high;
            }

            return true;
        }
    }
}
