// <copyright file="SpamdProtocolException.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: spamd wire or response parse failures.
// SpamdProtocolException.cs -- Exception type for non-zero spamd exit codes and malformed replies.

namespace Vector.NNTP.Filters.SpamAssassin
{
    /// <summary>
    /// Indicates spamd returned a non-zero sysexits code, an unexpected reply, or the TCP session failed.
    /// </summary>
    public sealed class SpamdProtocolException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SpamdProtocolException"/> class.
        /// </summary>
        /// <param name="message">Human-readable error description.</param>
        public SpamdProtocolException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SpamdProtocolException"/> class.
        /// </summary>
        /// <param name="message">Human-readable error description.</param>
        /// <param name="innerException">Underlying socket or I/O exception.</param>
        public SpamdProtocolException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SpamdProtocolException"/> class for a spamd status line.
        /// </summary>
        /// <param name="exitCode">sysexits.h code from the <c>SPAMD/x.y CODE ...</c> status line.</param>
        /// <param name="statusMessage">Text after the code on the status line.</param>
        /// <param name="statusLine">Full status line returned by spamd.</param>
        public SpamdProtocolException(int exitCode, string statusMessage, string statusLine)
            : base($"spamd returned exit code {exitCode} ({statusMessage}): {statusLine}")
        {
            this.ExitCode = exitCode;
            this.StatusMessage = statusMessage;
            this.StatusLine = statusLine;
        }

        /// <summary>sysexits.h code from the spamd status line (0 means <c>EX_OK</c>).</summary>
        public int? ExitCode { get; }

        /// <summary>Status text after the numeric code on the spamd status line.</summary>
        public string? StatusMessage { get; }

        /// <summary>Full first-line response from spamd.</summary>
        public string? StatusLine { get; }
    }
}
