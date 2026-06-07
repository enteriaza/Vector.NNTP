// <copyright file="SpamdCheckResult.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: result DTO for CHECK/SYMBOLS/REPORT spamd commands.
// SpamdCheckResult.cs -- Classification outcome from spamd header block and optional trailing payload.

namespace Vector.NNTP.Filters.SpamAssassin
{
    /// <summary>
    /// Parsed outcome of a spamd classification command that returns a <c>Spam:</c> response header.
    /// </summary>
    /// <param name="isSpam">Whether spamd classified the message as spam per the <c>Spam:</c> header flag.</param>
    /// <param name="score">Message score from the <c>Spam:</c> header.</param>
    /// <param name="threshold">Required threshold from the <c>Spam:</c> header.</param>
    /// <param name="symbols">
    /// Hit rule names when the originating command was <see cref="SpamdCommand.Symbols"/>; otherwise an empty list.
    /// Must not be <see langword="null"/>.
    /// </param>
    /// <param name="reportText">
    /// Report body text when the originating command was <see cref="SpamdCommand.Report"/> or <see cref="SpamdCommand.ReportIfSpam"/>; otherwise <see langword="null"/>.
    /// </param>
    /// <param name="rawResponseHeaders">
    /// Response header map from spamd excluding the status line. Must not be <see langword="null"/>; lookup is case-insensitive.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="symbols"/> or <paramref name="rawResponseHeaders"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para><b>Spam header:</b> Built from the wire form <c>Spam: True ; score / threshold</c> or <c>Spam: False ; score / threshold</c>
    /// parsed by <see cref="SpamdWireSession.TryParseSpamHeader"/>.</para>
    /// <para><b>Commands:</b> Returned by <see cref="SpamAssassin.CheckAsync"/>, <see cref="SpamAssassin.SymbolsAsync"/>,
    /// <see cref="SpamAssassin.ReportAsync"/>, and <see cref="SpamAssassin.ReportIfSpamAsync"/> (when a result is produced).
    /// Also embedded as <see cref="SpamdProcessResult.Classification"/> for <c>PROCESS</c> when a <c>Spam:</c> header is present.</para>
    /// <para><b>Trailing payload:</b> <paramref name="symbols"/> and <paramref name="reportText"/> are populated only for commands that return
    /// body content after the header block (<see cref="SpamdCommand.Symbols"/>, <see cref="SpamdCommand.Report"/>,
    /// <see cref="SpamdCommand.ReportIfSpam"/>).</para>
    /// <para><b>Ownership:</b> <paramref name="rawResponseHeaders"/> is retained by reference and is not copied; callers must not mutate the
    /// dictionary after construction. <paramref name="symbols"/> is stored by reference — do not mutate the list after construction.</para>
    /// <para><b>Headers:</b> Header name lookup on <see cref="RawResponseHeaders"/> is case-insensitive; keys preserve wire casing.</para>
    /// </remarks>
    public sealed class SpamdCheckResult(
        bool isSpam,
        double score,
        double threshold,
        IReadOnlyList<string> symbols,
        string? reportText,
        IReadOnlyDictionary<string, string> rawResponseHeaders)
    {

        /// <summary>
        /// Gets a value indicating whether spamd classified the article as spam per the <c>Spam:</c> response header.
        /// </summary>
        public bool IsSpam { get; } = isSpam;

        /// <summary>
        /// Gets the SpamAssassin score from the <c>Spam:</c> response header.
        /// </summary>
        /// <remarks>Culture-invariant floating-point value parsed from the header (for example <c>5.1</c>).</remarks>
        public double Score { get; } = score;

        /// <summary>
        /// Gets the required score threshold from the <c>Spam:</c> response header.
        /// </summary>
        /// <remarks>Culture-invariant floating-point value parsed from the header (for example <c>5.0</c>).</remarks>
        public double Threshold { get; } = threshold;

        /// <summary>
        /// Gets hit rule names when the originating command was <see cref="SpamdCommand.Symbols"/>; otherwise an empty list.
        /// </summary>
        /// <remarks>
        /// Populated from a comma-separated trailer body. Stored by reference; callers must not mutate the list after construction.
        /// </remarks>
        public IReadOnlyList<string> Symbols { get; } = symbols ?? throw new ArgumentNullException(nameof(symbols));

        /// <summary>
        /// Gets human-readable report text when the originating command returned a report body; otherwise <see langword="null"/>.
        /// </summary>
        /// <remarks>
        /// For <see cref="SpamdCommand.ReportIfSpam"/>, ham messages may yield <see langword="null"/> or empty text even when
        /// <see cref="IsSpam"/> is <see langword="false"/>.
        /// </remarks>
        public string? ReportText { get; } = reportText;

        /// <summary>
        /// Gets the response header map from spamd excluding the <c>SPAMD/x.y</c> status line.
        /// </summary>
        /// <remarks>
        /// Retained by reference without copying. Header name lookup is case-insensitive; keys preserve wire casing.
        /// Callers should treat the dictionary as read-only and must not mutate it after construction.
        /// </remarks>
        public IReadOnlyDictionary<string, string> RawResponseHeaders { get; } =
            rawResponseHeaders ?? throw new ArgumentNullException(nameof(rawResponseHeaders));
    }
}
