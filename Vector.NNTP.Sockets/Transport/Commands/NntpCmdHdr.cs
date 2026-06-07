// <copyright file="NntpCmdHdr.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: HDR and XHDR command handler.

using Vector.NNTP.Sockets.Protocol;
using Vector.NNTP.Sockets.Responses;
using Vector.NNTP.Sockets.Session;
using Vector.NNTP.Sockets.Storage;

namespace Vector.NNTP.Sockets.Transport.Commands
{
    /// <summary>
    /// Handles HDR and XHDR header field retrieval commands.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When a syntactically valid Message-ID selector is supplied, header lookup by message-id is not yet implemented;
    /// the handler falls back to the default group range until storage grows message-id HDR support.
    /// </para>
    /// </remarks>
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
                await session.Writer.WriteMultiLineAsync("225 Headers follow", [], cancellationToken).ConfigureAwait(false);
                return true;
            }

            await session.Writer.WriteMultiLineAsync("225 Headers follow", lines, cancellationToken).ConfigureAwait(false);
            return true;
        }

        /// <summary>
        /// Tries to parse the arguments for the HDR command.
        /// </summary>
        /// <param name="argument">The command line argument.</param>
        /// <param name="headerField">The header field name.</param>
        /// <param name="rangeLow">The low end of the range.</param>
        /// <param name="rangeHigh">The high end of the range.</param>
        /// <returns><see langword="true"/> when arguments are syntactically valid.</returns>
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
            string selectorText = argument[(firstSpace + 1)..].Trim();
            if (string.IsNullOrEmpty(selectorText))
            {
                return true;
            }

            return ArticleRangeOrMessageIdSyntax.TryParse(selectorText, out rangeLow, out rangeHigh, out string? _);
        }
    }
}
