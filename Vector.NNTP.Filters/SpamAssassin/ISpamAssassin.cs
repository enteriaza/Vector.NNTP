// <copyright file="ISpamAssassin.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: narrow DI contract for spamd CHECK integration, mocking, and spool postprocess injection.
// ISpamAssassin.cs -- Abstraction implemented by <see cref="SpamAssassin"/> for transit spool spam scanning.

namespace Vector.NNTP.Filters.SpamAssassin
{
    /// <summary>
    /// Classifies NNTP articles via a remote <c>spamd</c> <see cref="SpamdCommand.Check"/> exchange without modifying the message.
    /// </summary>
    /// <remarks>
    /// <para><b>Scope:</b> The contract surface is intentionally narrow (CHECK only) for transit spool integration. Use
    /// <see cref="SpamAssassin"/> directly when <see cref="SpamdCommand.Symbols"/>, <see cref="SpamdCommand.Report"/>,
    /// <see cref="SpamdCommand.Process"/>, or <see cref="SpamdCommand.Tell"/> are required.</para>
    /// <para><b>Implementation:</b> Production hosts register <see cref="SpamAssassin"/> as the singleton implementation via
    /// <c>AddSpamAssassin</c> in <c>Vector.NNTP.Filters.DependencyInjection</c>.</para>
    /// <para><b>Consumer:</b> <c>ArticleSpoolPostprocessor</c> calls <see cref="CheckAsync"/> for articles under the configured scan size;
    /// <see cref="SpamdProtocolException"/> is typically handled fail-open (article accepted) while logging structured error fields.</para>
    /// <para><b>Connection model:</b> Implementations are expected to open a fresh TCP connection per call rather than pooling sessions across posts.</para>
    /// <para><b>Article input:</b> Callers pass the full RFC 822 / NNTP POST buffer (headers, blank line, body). The spool layer may prepend
    /// synthetic headers (for example <c>X-NNTP-Posting-Host</c>) before invoking this method.</para>
    /// <para><b>Thread safety:</b> Implementations must be safe for concurrent calls from multiple writer pumps or protocol handlers.</para>
    /// </remarks>
    public interface ISpamAssassin
    {
        /// <summary>
        /// Classifies an article without modifying it using the spamc <c>CHECK</c> command.
        /// </summary>
        /// <param name="articleUtf8">Full article octets (headers, blank line, and body) to send after the spamc request header block.</param>
        /// <param name="cancellationToken">
        /// Cancellation token. Production <see cref="SpamAssassin"/> also links an operation timeout from
        /// <see cref="SpamAssassinOptions.OperationTimeoutMilliseconds"/>.
        /// </param>
        /// <returns>
        /// Parsed <see cref="SpamdCheckResult"/> with <see cref="SpamdCheckResult.IsSpam"/>, score, threshold, and response headers from spamd.
        /// </returns>
        /// <exception cref="SpamdProtocolException">
        /// Thrown when the wire exchange fails, spamd returns a non-zero status, the response is malformed, or the <c>Spam:</c> header is missing.
        /// Connect failures and connect-timeout cancellation are thrown as <see cref="SpamdConnectionException"/> (a subclass); post-connect wire failures use <see cref="SpamdProtocolException"/> directly.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        /// Thrown when <paramref name="cancellationToken"/> is canceled or an implementation-specific operation timeout fires during I/O after connect.
        /// </exception>
        Task<SpamdCheckResult> CheckAsync(ReadOnlyMemory<byte> articleUtf8, CancellationToken cancellationToken = default);
    }
}
