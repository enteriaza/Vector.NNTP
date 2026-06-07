// <copyright file="SpamAssassin.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: per-article spamd TCP scan on the POST filter path (typical articles under 128 KiB); one connection per check.
// SpamAssassin.cs -- spamc-compatible client that sends Usenet articles to spamd for scanning and processing.

using Microsoft.Extensions.Options;

namespace Vector.NNTP.Filters.SpamAssassin
{
    /// <summary>
    /// Sends Usenet articles to a remote <c>spamd</c> process using the SpamAssassin network protocol (spamc-compatible).
    /// </summary>
    /// <remarks>
    /// <para><b>Performance:</b> HOT PATH on the POST filter — <see cref="CheckAsync"/> runs for articles under the configured size threshold.
    /// Each command opens a new TCP connection through <see cref="SpamdWireSession"/>.</para>
    /// <para><b>Protocol:</b> Sends <c>COMMAND SPAMC/x.y</c>, optional <c>Content-length</c>, the raw article bytes, then half-closes the send side. See
    /// <see href="https://apache.googlesource.com/spamassassin/+/de1db4d804b4bde5d91101f4870dc3cdbf4af688/3.1/spamd/PROTOCOL">spamd/PROTOCOL</see>.</para>
    /// <para><b>Article format:</b> Pass the full RFC 822 / NNTP article buffer (headers, blank line, body) as received on POST.</para>
    /// <para><b>Host selection:</b> Each command round-robins the first connect attempt across <see cref="SpamAssassinOptions.Hosts"/> using a lock-free
    /// counter; when the first host cannot be reached, remaining configured hosts are tried before the request fails.
    /// Connect-time retries catch <see cref="SpamdConnectionException"/> only — post-connect <see cref="SpamdProtocolException"/> errors are never masked by failover.</para>
    /// <para><b>Timeouts:</b> Each operation is bounded by <see cref="SpamAssassinOptions.OperationTimeoutMilliseconds"/> via a linked cancellation token in
    /// <see cref="ExecuteAsync"/>.</para>
    /// <para><b>Interface:</b> Implements <see cref="ISpamAssassin"/> (<see cref="CheckAsync"/> only). Other commands are available on this concrete type.</para>
    /// <para><b>Thread safety:</b> Instances are safe for concurrent use; each call uses its own connection and round-robin host selection is synchronized.</para>
    /// </remarks>
    /// <param name="options">Validated spamd host list, port, protocol version, and timeout settings.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is <see langword="null"/>.</exception>
    public sealed class SpamAssassin(SpamAssassinOptions options) : ISpamAssassin
    {
        /// <summary>
        /// Validated spamd host, port, protocol version, user header, and timeout settings supplied at construction.
        /// </summary>
        private readonly SpamAssassinOptions _options = options ?? throw new ArgumentNullException(nameof(options));

        /// <summary>
        /// Monotonically increasing counter used for lock-free round-robin host selection in <see cref="GetHostAttemptOrder"/>.
        /// </summary>
        /// <remarks>Interpreted as <see cref="uint"/> when indexing so overflow does not require reset or unsynchronized writes.</remarks>
        private int _hostRoundRobinIndex;

        /// <summary>
        /// Allowed <c>Message-class</c> header values for <see cref="TellAsync"/> per spamd <c>TELL</c> semantics.
        /// </summary>
        private static readonly HashSet<string> AllowedTellMessageClasses = new(StringComparer.OrdinalIgnoreCase)
        {
            "spam",
            "ham",
            "forget",
            "revoke",
            "report",
        };

        /// <summary>
        /// Initializes a new instance from validated <see cref="IOptions{TOptions}"/> configuration (typical DI registration path).
        /// </summary>
        /// <param name="options">Bound and validated <see cref="SpamAssassinOptions"/> from host configuration.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// Delegates to the primary constructor using <see cref="IOptions{TOptions}.Value"/> after null-checking the options wrapper.
        /// </remarks>
        public SpamAssassin(IOptions<SpamAssassinOptions> options)
            : this(options?.Value ?? throw new ArgumentNullException(nameof(options)))
        {
        }

        /// <summary>
        /// Verifies that spamd is reachable using the <see cref="SpamdCommand.Ping"/> command.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token linked to <see cref="SpamAssassinOptions.OperationTimeoutMilliseconds"/>.</param>
        /// <returns>
        /// <see langword="true"/> when the status line contains <c>PONG</c> (case-insensitive); <see langword="false"/> when spamd responds but the
        /// status line does not indicate pong.
        /// </returns>
        /// <exception cref="SpamdProtocolException">Thrown on connect failure or other wire/protocol errors (including wrapped cancellation on connect).</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation timeout or <paramref name="cancellationToken"/> fires during I/O after connect.</exception>
        public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
        {
            SpamdWireResponse response = await ExecuteAsync(SpamdCommand.Ping, ReadOnlyMemory<byte>.Empty, extraRequestHeaders: null, cancellationToken)
                .ConfigureAwait(false);
            bool pong = response.StatusLine.Contains("PONG", StringComparison.OrdinalIgnoreCase);
            return pong;
        }

        /// <summary>
        /// Classifies an article without modifying it using the <see cref="SpamdCommand.Check"/> command.
        /// </summary>
        /// <param name="articleUtf8">Full article octets (headers, blank line, and body).</param>
        /// <param name="cancellationToken">Cancellation token linked to <see cref="SpamAssassinOptions.OperationTimeoutMilliseconds"/>.</param>
        /// <returns>Spam classification parsed from the <c>Spam:</c> response header.</returns>
        /// <exception cref="SpamdProtocolException">Thrown when spamd rejects the request, the response is malformed, or the <c>Spam:</c> header is missing.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation timeout or <paramref name="cancellationToken"/> fires during I/O.</exception>
        public Task<SpamdCheckResult> CheckAsync(ReadOnlyMemory<byte> articleUtf8, CancellationToken cancellationToken = default)
        {
            return CheckCoreAsync(SpamdCommand.Check, articleUtf8, cancellationToken);
        }

        /// <summary>
        /// Classifies an article and returns hit rule names using the <see cref="SpamdCommand.Symbols"/> command.
        /// </summary>
        /// <param name="articleUtf8">Full article octets (headers, blank line, and body).</param>
        /// <param name="cancellationToken">Cancellation token linked to <see cref="SpamAssassinOptions.OperationTimeoutMilliseconds"/>.</param>
        /// <returns>Classification plus symbol names parsed from the comma-separated response body trailer.</returns>
        /// <exception cref="SpamdProtocolException">Thrown when spamd rejects the request, the response is malformed, or the <c>Spam:</c> header is missing.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation timeout or <paramref name="cancellationToken"/> fires during I/O.</exception>
        public Task<SpamdCheckResult> SymbolsAsync(ReadOnlyMemory<byte> articleUtf8, CancellationToken cancellationToken = default)
        {
            return CheckCoreAsync(SpamdCommand.Symbols, articleUtf8, cancellationToken);
        }

        /// <summary>
        /// Classifies an article and returns a full text report using the <see cref="SpamdCommand.Report"/> command.
        /// </summary>
        /// <param name="articleUtf8">Full article octets (headers, blank line, and body).</param>
        /// <param name="cancellationToken">Cancellation token linked to <see cref="SpamAssassinOptions.OperationTimeoutMilliseconds"/>.</param>
        /// <returns>Classification plus report text from the response body trailer.</returns>
        /// <exception cref="SpamdProtocolException">Thrown when spamd rejects the request, the response is malformed, or the <c>Spam:</c> header is missing.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation timeout or <paramref name="cancellationToken"/> fires during I/O.</exception>
        public Task<SpamdCheckResult> ReportAsync(ReadOnlyMemory<byte> articleUtf8, CancellationToken cancellationToken = default)
        {
            return CheckCoreAsync(SpamdCommand.Report, articleUtf8, cancellationToken);
        }

        /// <summary>
        /// Classifies an article and returns a report body only when spamd marks it as spam (<see cref="SpamdCommand.ReportIfSpam"/>).
        /// </summary>
        /// <param name="articleUtf8">Full article octets (headers, blank line, and body).</param>
        /// <param name="cancellationToken">Cancellation token linked to <see cref="SpamAssassinOptions.OperationTimeoutMilliseconds"/>.</param>
        /// <returns>
        /// A <see cref="SpamdCheckResult"/> when spamd returns a <c>Spam:</c> header or non-empty body; <see langword="null"/> when ham yields an empty
        /// body and no parseable <c>Spam:</c> header.
        /// </returns>
        /// <exception cref="SpamdProtocolException">Thrown on wire/protocol errors, or when the response has a body but no recognizable <c>Spam:</c> header.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation timeout or <paramref name="cancellationToken"/> fires during I/O.</exception>
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
        /// Scans an article and returns the modified message with SpamAssassin headers inserted (<see cref="SpamdCommand.Process"/>).
        /// </summary>
        /// <param name="articleUtf8">Full article octets (headers, blank line, and body).</param>
        /// <param name="cancellationToken">Cancellation token linked to <see cref="SpamAssassinOptions.OperationTimeoutMilliseconds"/>.</param>
        /// <returns>Rewritten article bytes and optional <see cref="SpamdCheckResult"/> metadata when a <c>Spam:</c> header is present.</returns>
        /// <exception cref="SpamdProtocolException">Thrown on wire/protocol errors or when spamd returns an empty <c>PROCESS</c> body.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation timeout or <paramref name="cancellationToken"/> fires during I/O.</exception>
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
        /// Sends a <see cref="SpamdCommand.Tell"/> command to train spamd (learn, forget, report, or revoke).
        /// </summary>
        /// <param name="articleUtf8">Article octets associated with the training action (headers and body).</param>
        /// <param name="messageClass">Required <c>Message-class</c> header value: <c>spam</c>, <c>ham</c>, <c>forget</c>, <c>revoke</c>, or <c>report</c>.</param>
        /// <param name="setTargets">Optional <c>Set:</c> header value (for example <c>local</c> or <c>local, remote</c>); omitted when null or whitespace.</param>
        /// <param name="removeTargets">Optional <c>Remove:</c> header value; omitted when null or whitespace.</param>
        /// <param name="cancellationToken">Cancellation token linked to <see cref="SpamAssassinOptions.OperationTimeoutMilliseconds"/>.</param>
        /// <returns>
        /// Response headers from spamd (case-insensitive lookup; keys preserve wire casing). May include <c>DidSet</c> or <c>DidRemove</c>.
        /// </returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="messageClass"/> is null, empty, or not a recognized <c>TELL</c> class.</exception>
        /// <exception cref="SpamdProtocolException">Thrown on connect failure or other wire/protocol errors (including wrapped cancellation on connect).</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation timeout or <paramref name="cancellationToken"/> fires during I/O after connect.</exception>
        public async Task<IReadOnlyDictionary<string, string>> TellAsync(
            ReadOnlyMemory<byte> articleUtf8,
            string messageClass,
            string? setTargets,
            string? removeTargets,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(messageClass);
            if (!AllowedTellMessageClasses.Contains(messageClass.Trim()))
            {
                throw new ArgumentException(
                    $"Message-class must be one of: {string.Join(", ", AllowedTellMessageClasses)}.",
                    nameof(messageClass));
            }

            Dictionary<string, string> tellHeaders = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Message-class"] = messageClass.Trim(),
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
        /// Shared implementation for <see cref="CheckAsync"/>, <see cref="SymbolsAsync"/>, and <see cref="ReportAsync"/>.
        /// </summary>
        /// <param name="command">Classification command to execute.</param>
        /// <param name="articleUtf8">Full article octets sent after the spamc header block.</param>
        /// <param name="cancellationToken">Cancellation token linked to the operation timeout.</param>
        /// <returns>Parsed classification from the <c>Spam:</c> response header and command-specific trailer content.</returns>
        /// <exception cref="SpamdProtocolException">Thrown when the <c>Spam:</c> header is missing or the wire exchange fails.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation timeout or <paramref name="cancellationToken"/> fires during I/O.</exception>
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
        /// Connects to spamd with connect-time failover, runs one command, and disposes the wire session.
        /// </summary>
        /// <param name="command">Wire command to execute.</param>
        /// <param name="articleUtf8">Article octets for commands that send a body; ignored for <see cref="SpamdCommand.Ping"/>.</param>
        /// <param name="extraRequestHeaders">Optional additional request headers (used by <see cref="TellAsync"/>); may be <see langword="null"/>.</param>
        /// <param name="cancellationToken">Caller cancellation token; linked with <see cref="SpamAssassinOptions.OperationTimeoutMilliseconds"/>.</param>
        /// <returns>Parsed wire response from <see cref="SpamdWireSession.ExecuteAsync"/>.</returns>
        /// <exception cref="SpamdProtocolException">
        /// Thrown when every configured host fails to connect (as <see cref="SpamdConnectionException"/>), or when the command exchange fails after a successful connect.
        /// </exception>
        /// <exception cref="OperationCanceledException">Thrown when the linked operation timeout or <paramref name="cancellationToken"/> fires during the exchange.</exception>
        private async Task<SpamdWireResponse> ExecuteAsync(
            SpamdCommand command,
            ReadOnlyMemory<byte> articleUtf8,
            IReadOnlyDictionary<string, string>? extraRequestHeaders,
            CancellationToken cancellationToken)
        {
            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.OperationTimeoutMilliseconds);

            SpamdWireSession session = await ConnectWithFailoverAsync(timeoutCts.Token).ConfigureAwait(false);
            await using (session.ConfigureAwait(false))
            {
                return await session.ExecuteAsync(command, articleUtf8, extraRequestHeaders, timeoutCts.Token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Opens a <see cref="SpamdWireSession"/> to the next round-robin host, then tries remaining configured hosts when connect fails.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for each connect attempt.</param>
        /// <returns>A connected session to the first reachable host.</returns>
        /// <exception cref="SpamdConnectionException">Thrown when every host in the attempt order fails to connect.</exception>
        /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled; failover stops immediately.</exception>
        /// <remarks>
        /// Catches only <see cref="SpamdConnectionException"/> from <see cref="SpamdWireSession.ConnectAsync"/> so a future protocol error thrown during
        /// connect setup would not trigger silent failover to another host.
        /// </remarks>
        private async Task<SpamdWireSession> ConnectWithFailoverAsync(CancellationToken cancellationToken)
        {
            string[] hosts = GetHostAttemptOrder();
            SpamdConnectionException? lastFailure = null;

            foreach (string host in hosts)
            {
                try
                {
                    return await SpamdWireSession.ConnectAsync(host, _options, cancellationToken).ConfigureAwait(false);
                }
                catch (SpamdConnectionException ex)
                {
                    lastFailure = ex;
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                }
            }

            throw lastFailure is null
                ? new SpamdConnectionException("No spamd hosts are configured.")
                : new SpamdConnectionException($"Failed to connect to any of {hosts.Length} configured spamd host(s).", lastFailure);
        }

        /// <summary>
        /// Builds the host attempt order for the current command using lock-free round-robin over <see cref="SpamAssassinOptions.Hosts"/>.
        /// </summary>
        /// <returns>
        /// Hostnames or IP addresses to try in order. When multiple hosts are configured, the first entry rotates per call and remaining entries
        /// follow in ring order for connect-time failover.
        /// </returns>
        /// <remarks>
        /// When <see cref="SpamAssassinOptions.Hosts"/> is null or empty, returns a single-element array containing <see cref="SpamAssassinOptions.Host"/>.
        /// </remarks>
        private string[] GetHostAttemptOrder()
        {
            string[] hosts = _options.Hosts;
            if (hosts is null || hosts.Length == 0)
            {
                return [_options.Host];
            }

            if (hosts.Length == 1)
            {
                return hosts;
            }

            int counter = Interlocked.Increment(ref _hostRoundRobinIndex);
            uint normalized = unchecked((uint)counter);
            int start = (int)((normalized - 1) % (uint)hosts.Length);
            string[] order = new string[hosts.Length];
            for (int i = 0; i < hosts.Length; i++)
            {
                order[i] = hosts[(start + i) % hosts.Length];
            }

            return order;
        }
    }
}
