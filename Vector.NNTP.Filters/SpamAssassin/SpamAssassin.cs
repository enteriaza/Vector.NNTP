// <copyright file="SpamAssassin.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: network I/O to spamd; one TCP connection per article check; suitable for POST filter integration.
// SpamAssassin.cs -- spamc-compatible client that sends Usenet articles to spamd for scanning and processing.

using Microsoft.Extensions.Options;

namespace Vector.NNTP.Filters.SpamAssassin
{
    /// <summary>
    /// Sends Usenet articles to a remote <c>spamd</c> process using the SpamAssassin network protocol (spamc-compatible).
    /// </summary>
    /// <remarks>
    /// <para><b>Protocol:</b> Each operation opens a new TCP connection, sends <c>COMMAND SPAMC/x.y</c>, optional
    /// <c>Content-length</c>, the raw article bytes, then half-closes the send side. See
    /// <see href="https://apache.googlesource.com/spamassassin/+/de1db4d804b4bde5d91101f4870dc3cdbf4af688/3.1/spamd/PROTOCOL">spamd/PROTOCOL</see>.</para>
    ///
    /// <para><b>Article format:</b> Pass the full RFC 822 / NNTP article buffer (headers, blank line, body) as received on POST.</para>
    ///
    /// <para><b>Thread safety:</b> Instances are safe for concurrent use; each call uses its own connection.</para>
    /// </remarks>
    /// <remarks>
    /// Initializes a new instance of the <see cref="SpamAssassin"/> class.
    /// </remarks>
    /// <param name="options">spamd host, port, and timeout settings.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is <see langword="null"/>.</exception>
    public sealed class SpamAssassin(SpamAssassinOptions options)
    {
        /// <summary>
        /// Validated spamd host, port, and timeout settings supplied at construction.
        /// </summary>
        /// <value>The spamd host, port, and timeout settings.</value>
        private readonly SpamAssassinOptions _options = options ?? throw new ArgumentNullException(nameof(options));

        /// <summary>
        /// Initializes a new instance of the <see cref="SpamAssassin"/> class from <see cref="IOptions{TOptions}"/>.
        /// </summary>
        /// <param name="options">Bound configuration.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is <see langword="null"/>.</exception>
        public SpamAssassin(IOptions<SpamAssassinOptions> options)
            : this(options?.Value ?? throw new ArgumentNullException(nameof(options)))
        {
        }

        /// <summary>
        /// Verifies that spamd is reachable (<c>PING</c> command).
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns><see langword="true"/> when spamd responds with <c>PONG</c>.</returns>
        /// <exception cref="SpamdProtocolException">Thrown on wire or protocol errors.</exception>
        public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
        {
            SpamdWireResponse response = await ExecuteAsync(SpamdCommand.Ping, ReadOnlyMemory<byte>.Empty, extraRequestHeaders: null, cancellationToken)
                .ConfigureAwait(false);
            bool pong = response.StatusLine.Contains("PONG", StringComparison.OrdinalIgnoreCase);
            return pong;
        }

        /// <summary>
        /// Classifies an article without modifying it (<c>CHECK</c>).
        /// </summary>
        /// <param name="articleUtf8">Full article octets (headers and body).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Spam score and threshold from the <c>Spam:</c> response header.</returns>
        /// <exception cref="SpamdProtocolException">Thrown when spamd rejects the request or the <c>Spam:</c> header is missing.</exception>
        public Task<SpamdCheckResult> CheckAsync(ReadOnlyMemory<byte> articleUtf8, CancellationToken cancellationToken = default)
        {
            return CheckCoreAsync(SpamdCommand.Check, articleUtf8, cancellationToken);
        }

        /// <summary>
        /// Classifies an article and returns hit symbols (<c>SYMBOLS</c>).
        /// </summary>
        /// <param name="articleUtf8">Full article octets.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Classification plus comma-separated rule names.</returns>
        /// <exception cref="SpamdProtocolException">Thrown when spamd rejects the request or the <c>Spam:</c> header is missing.</exception>
        public Task<SpamdCheckResult> SymbolsAsync(ReadOnlyMemory<byte> articleUtf8, CancellationToken cancellationToken = default)
        {
            return CheckCoreAsync(SpamdCommand.Symbols, articleUtf8, cancellationToken);
        }

        /// <summary>
        /// Classifies an article and returns a full text report (<c>REPORT</c>).
        /// </summary>
        /// <param name="articleUtf8">Full article octets.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Classification plus report body text.</returns>
        /// <exception cref="SpamdProtocolException">Thrown when spamd rejects the request or the <c>Spam:</c> header is missing.</exception>
        public Task<SpamdCheckResult> ReportAsync(ReadOnlyMemory<byte> articleUtf8, CancellationToken cancellationToken = default)
        {
            return CheckCoreAsync(SpamdCommand.Report, articleUtf8, cancellationToken);
        }

        /// <summary>
        /// Classifies an article and returns a report only when spamd marks it as spam (<c>REPORT_IFSPAM</c>).
        /// </summary>
        /// <param name="articleUtf8">Full article octets.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Classification when spam; ham may yield a result without report text.</returns>
        /// <exception cref="SpamdProtocolException">Thrown on wire errors.</exception>
        public async Task<SpamdCheckResult?> ReportIfSpamAsync(ReadOnlyMemory<byte> articleUtf8, CancellationToken cancellationToken = default)
        {
            SpamdWireResponse response = await ExecuteAsync(SpamdCommand.ReportIfSpam, articleUtf8, extraRequestHeaders: null, cancellationToken)
                .ConfigureAwait(false);
            SpamdCheckResult? parsed = SpamdWireSession.TryParseSpamHeader(response.Headers, response.Body, SpamdCommand.ReportIfSpam);
            return parsed is null && response.Body.Length == 0
                ? null
                : parsed ?? throw new SpamdProtocolException("spamd REPORT_IFSPAM response did not include a Spam header.");
        }

        /// <summary>
        /// Scans an article and returns the modified message with SpamAssassin headers inserted (<c>PROCESS</c>).
        /// </summary>
        /// <param name="articleUtf8">Full article octets.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Rewritten article bytes and optional classification metadata from response headers.</returns>
        /// <exception cref="SpamdProtocolException">Thrown on wire or protocol errors.</exception>
        public async Task<SpamdProcessResult> ProcessAsync(ReadOnlyMemory<byte> articleUtf8, CancellationToken cancellationToken = default)
        {
            SpamdWireResponse response = await ExecuteAsync(SpamdCommand.Process, articleUtf8, extraRequestHeaders: null, cancellationToken)
                .ConfigureAwait(false);

            if (response.Body.Length == 0)
            {
                throw new SpamdProtocolException("spamd PROCESS returned an empty message body.");
            }

            SpamdCheckResult? classification = SpamdWireSession.TryParseSpamHeader(response.Headers, response.Body, SpamdCommand.Process);
            return new SpamdProcessResult(response.Body, classification, response.Headers);
        }

        /// <summary>
        /// Sends a <c>TELL</c> command to train spamd (learn, forget, report, or revoke).
        /// </summary>
        /// <param name="articleUtf8">Article that was classified.</param>
        /// <param name="messageClass">Typically <c>spam</c> or <c>ham</c>.</param>
        /// <param name="setTargets">Optional <c>Set:</c> header value (for example <c>local</c> or <c>local, remote</c>).</param>
        /// <param name="removeTargets">Optional <c>Remove:</c> header value.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Response headers (may include <c>DidSet</c> / <c>DidRemove</c>).</returns>
        /// <exception cref="SpamdProtocolException">Thrown on wire errors.</exception>
        public async Task<IReadOnlyDictionary<string, string>> TellAsync(
            ReadOnlyMemory<byte> articleUtf8,
            string messageClass,
            string? setTargets,
            string? removeTargets,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(messageClass);

            Dictionary<string, string> tellHeaders = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Message-class"] = messageClass,
            };

            if (!string.IsNullOrWhiteSpace(setTargets))
            {
                tellHeaders["Set"] = setTargets;
            }

            if (!string.IsNullOrWhiteSpace(removeTargets))
            {
                tellHeaders["Remove"] = removeTargets;
            }

            SpamdWireResponse response = await ExecuteAsync(SpamdCommand.Tell, articleUtf8, tellHeaders, cancellationToken)
                .ConfigureAwait(false);
            return response.Headers;
        }

        /// <summary>
        /// Shared CHECK/SYMBOLS/REPORT implementation.
        /// </summary>
        private async Task<SpamdCheckResult> CheckCoreAsync(
            SpamdCommand command,
            ReadOnlyMemory<byte> articleUtf8,
            CancellationToken cancellationToken)
        {
            SpamdWireResponse response = await ExecuteAsync(command, articleUtf8, extraRequestHeaders: null, cancellationToken)
                .ConfigureAwait(false);
            SpamdCheckResult? parsed = SpamdWireSession.TryParseSpamHeader(response.Headers, response.Body, command);
            return parsed is null ? throw new SpamdProtocolException($"spamd {command} response did not include a Spam header.") : parsed;
        }

        /// <summary>
        /// Opens a connection, runs one command, and disposes the session.
        /// </summary>
        private async Task<SpamdWireResponse> ExecuteAsync(
            SpamdCommand command,
            ReadOnlyMemory<byte> articleUtf8,
            IReadOnlyDictionary<string, string>? extraRequestHeaders,
            CancellationToken cancellationToken)
        {
            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.OperationTimeoutMilliseconds);

            SpamdWireSession session = await SpamdWireSession.ConnectAsync(_options, timeoutCts.Token).ConfigureAwait(false);
            await using (session.ConfigureAwait(false))
            {
                return await session.ExecuteAsync(command, articleUtf8, extraRequestHeaders, timeoutCts.Token).ConfigureAwait(false);
            }
        }
    }
}

