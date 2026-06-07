// <copyright file="SpamdCommand.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: spamc command enumeration consumed by wire session and public client API.
// SpamdCommand.cs -- spamc/spamd command names (SpamAssassin network protocol 1.2+).

namespace Vector.NNTP.Filters.SpamAssassin
{
    /// <summary>
    /// spamc command names sent as the first token on the request line (<c>COMMAND SPAMC/x.y</c>).
    /// </summary>
    /// <remarks>
    /// <para><b>Wire mapping:</b> Values are translated to upper-case wire tokens by <see cref="SpamdWireSession"/> (for example
    /// <see cref="Check"/> → <c>CHECK</c>, <see cref="ReportIfSpam"/> → <c>REPORT_IFSPAM</c>).</para>
    /// <para><b>Client API:</b> <see cref="SpamAssassin"/> exposes convenience methods for each command except
    /// <see cref="Skip"/> (protocol-only).</para>
    /// <para><b>Article body:</b> <see cref="Ping"/> and <see cref="Skip"/> omit the message body and <c>Content-length</c> on the wire;
    /// all other commands send the full POST article buffer after the header block.</para>
    /// <para><b>Protocol reference:</b>
    /// <see href="https://apache.googlesource.com/spamassassin/+/de1db4d804b4bde5d91101f4870dc3cdbf4af688/3.1/spamd/PROTOCOL">spamd/PROTOCOL</see>.</para>
    /// </remarks>
    public enum SpamdCommand
    {
        /// <summary>
        /// Classify only; response headers include a <c>Spam:</c> score line and typically no response body.
        /// </summary>
        /// <remarks>
        /// Wire token: <c>CHECK</c>. Invoked by <see cref="SpamAssassin.CheckAsync"/>; primary POST-filter scan command on the hot path.
        /// </remarks>
        Check = 0,

        /// <summary>
        /// Classify and return hit rule names in a comma-separated trailer after the header block.
        /// </summary>
        /// <remarks>
        /// Wire token: <c>SYMBOLS</c>. Invoked by <see cref="SpamAssassin.SymbolsAsync"/>; trailer parsed into
        /// <see cref="SpamdCheckResult.Symbols"/>.
        /// </remarks>
        Symbols = 1,

        /// <summary>
        /// Classify and return a full text report in the response body after the header block.
        /// </summary>
        /// <remarks>
        /// Wire token: <c>REPORT</c>. Invoked by <see cref="SpamAssassin.ReportAsync"/>; trailer parsed into
        /// <see cref="SpamdCheckResult.ReportText"/>.
        /// </remarks>
        Report = 2,

        /// <summary>
        /// Like <see cref="Report"/> when spamd marks the message as spam; otherwise the response body is empty.
        /// </summary>
        /// <remarks>
        /// Wire token: <c>REPORT_IFSPAM</c>. Invoked by <see cref="SpamAssassin.ReportIfSpamAsync"/>; may return
        /// <see langword="null"/> when ham yields no report body and no <c>Spam:</c> header.
        /// </remarks>
        ReportIfSpam = 3,

        /// <summary>
        /// Scan the message and return the rewritten article with SpamAssassin headers inserted in the response body.
        /// </summary>
        /// <remarks>
        /// Wire token: <c>PROCESS</c>. Invoked by <see cref="SpamAssassin.ProcessAsync"/>; body becomes
        /// <see cref="SpamdProcessResult.ProcessedArticle"/>.
        /// </remarks>
        Process = 4,

        /// <summary>
        /// Health check; no message body is sent and spamd responds with <c>PONG</c> on the status line.
        /// </summary>
        /// <remarks>
        /// Wire token: <c>PING</c>. Invoked by <see cref="SpamAssassin.PingAsync"/>; used for reachability probes only.
        /// </remarks>
        Ping = 5,

        /// <summary>
        /// Abort without scanning; opens a connection and sends <c>SKIP</c> with no message body.
        /// </summary>
        /// <remarks>
        /// Wire token: <c>SKIP</c>. Not exposed on <see cref="SpamAssassin"/>; available for low-level
        /// <see cref="SpamdWireSession.ExecuteAsync"/> callers that need to abandon a session cheaply.
        /// </remarks>
        Skip = 6,

        /// <summary>
        /// Bayesian learning or reporting via <c>TELL</c> with <c>Message-class</c> and optional <c>Set</c> / <c>Remove</c> headers.
        /// </summary>
        /// <remarks>
        /// Wire token: <c>TELL</c>. Invoked by <see cref="SpamAssassin.TellAsync"/> with extra request headers; response may include
        /// <c>DidSet</c> or <c>DidRemove</c>.
        /// </remarks>
        Tell = 7,
    }
}
