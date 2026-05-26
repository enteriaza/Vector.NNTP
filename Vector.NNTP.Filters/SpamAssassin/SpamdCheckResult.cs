// <copyright file="SpamdCheckResult.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: result DTO for CHECK/SYMBOLS/REPORT spamd commands.
// SpamdCheckResult.cs -- Classification outcome from spamd header block and optional trailing payload.

namespace Vector.NNTP.Filters.SpamAssassin
{
    /// <summary>
    /// Parsed outcome of a spamd <c>CHECK</c>, <c>SYMBOLS</c>, or <c>REPORT</c> command.
    /// </summary>
    /// <remarks>
    /// <para><b>Spam header:</b> spamd returns <c>Spam: True ; score / threshold</c> or <c>False</c> in the response header block.</para>
    /// </remarks>
    public sealed class SpamdCheckResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SpamdCheckResult"/> class.
        /// </summary>
        /// <param name="isSpam">Whether spamd classified the message as spam.</param>
        /// <param name="score">Message score from the <c>Spam:</c> header.</param>
        /// <param name="threshold">Threshold from the <c>Spam:</c> header.</param>
        /// <param name="symbols">Hit rule names when the command was <see cref="SpamdCommand.Symbols"/>.</param>
        /// <param name="reportText">Report body when the command was <see cref="SpamdCommand.Report"/> or <see cref="SpamdCommand.ReportIfSpam"/>.</param>
        /// <param name="rawResponseHeaders">Unparsed response header lines (excluding the status line).</param>
        public SpamdCheckResult(
            bool isSpam,
            double score,
            double threshold,
            IReadOnlyList<string> symbols,
            string? reportText,
            IReadOnlyDictionary<string, string> rawResponseHeaders)
        {
            this.IsSpam = isSpam;
            this.Score = score;
            this.Threshold = threshold;
            this.Symbols = symbols;
            this.ReportText = reportText;
            this.RawResponseHeaders = rawResponseHeaders;
        }

        /// <summary>When <see langword="true"/>, spamd classified the article as spam.</summary>
        public bool IsSpam { get; }

        /// <summary>SpamAssassin score from the <c>Spam:</c> response header.</summary>
        public double Score { get; }

        /// <summary>Required score threshold from the <c>Spam:</c> response header.</summary>
        public double Threshold { get; }

        /// <summary>Rule names returned by <c>SYMBOLS</c> (empty for <c>CHECK</c>).</summary>
        public IReadOnlyList<string> Symbols { get; }

        /// <summary>Human-readable report text when requested (may be empty for ham with <c>REPORT_IFSPAM</c>).</summary>
        public string? ReportText { get; }

        /// <summary>Additional response headers returned by spamd (keys are lower-case).</summary>
        public IReadOnlyDictionary<string, string> RawResponseHeaders { get; }
    }
}
