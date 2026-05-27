// <copyright file="NntpReaderErrors.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: shared RFC 3977 reader error responses.

using Vector.NNTP.Sockets.Session;

namespace Vector.NNTP.Sockets.Responses
{
    /// <summary>
    /// Writes common reader-mode NNTP error responses (RFC 3977).
    /// </summary>
    internal static class NntpReaderErrors
    {
        /// <summary>
        /// Writes <c>412 No newsgroup has been selected</c>.
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="ValueTask"/> that completes when the response is sent.</returns>
        internal static ValueTask WriteNoGroupSelected412(NntpSession session, CancellationToken cancellationToken)
        {
            return session.Writer.WriteLineAsync("412 No newsgroup has been selected", cancellationToken);
        }

        /// <summary>
        /// Writes <c>411 No such news group</c>.
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="ValueTask"/> that completes when the response is sent.</returns>
        internal static ValueTask WriteNoSuchGroup411(NntpSession session, CancellationToken cancellationToken)
        {
            return session.Writer.WriteLineAsync("411 No such news group", cancellationToken);
        }

        /// <summary>
        /// Writes <c>430 No such article found</c>.
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="ValueTask"/> that completes when the response is sent.</returns>
        internal static ValueTask WriteNoSuchArticle430(NntpSession session, CancellationToken cancellationToken)
        {
            return session.Writer.WriteLineAsync("430 No such article found", cancellationToken);
        }

        /// <summary>
        /// Writes <c>423 No such article number in this group</c>.
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="ValueTask"/> that completes when the response is sent.</returns>
        internal static ValueTask WriteArticleOutOfRange423(NntpSession session, CancellationToken cancellationToken)
        {
            return session.Writer.WriteLineAsync("423 No such article number in this group", cancellationToken);
        }

        /// <summary>
        /// Writes <c>503 Command not supported</c>.
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="ValueTask"/> that completes when the response is sent.</returns>
        internal static ValueTask WriteServiceUnavailable503(NntpSession session, CancellationToken cancellationToken)
        {
            return session.Writer.WriteLineAsync("503 Command not supported", cancellationToken);
        }

        /// <summary>
        /// Writes <c>501 Syntax error in parameters or arguments</c>.
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="ValueTask"/> that completes when the response is sent.</returns>
        internal static ValueTask WriteBadSyntax501(NntpSession session, CancellationToken cancellationToken)
        {
            return session.Writer.WriteLineAsync("501 Syntax error in parameters or arguments", cancellationToken);
        }
    }
}
