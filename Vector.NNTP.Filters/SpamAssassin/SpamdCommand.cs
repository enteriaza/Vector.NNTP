// <copyright file="SpamdCommand.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: network I/O to spamd; one connection per request; allocations acceptable on post/reader paths.
// SpamdCommand.cs -- spamc/spamd command names (SpamAssassin network protocol 1.2+).

namespace Vector.NNTP.Filters.SpamAssassin
{
    /// <summary>
    /// spamc command names sent as the first token on the request line (<c>COMMAND SPAMC/x.y</c>).
    /// </summary>
    /// <remarks>
    /// <para>See the SpamAssassin spamd protocol:
    /// <see href="https://apache.googlesource.com/spamassassin/+/de1db4d804b4bde5d91101f4870dc3cdbf4af688/3.1/spamd/PROTOCOL">spamd/PROTOCOL</see>.</para>
    /// </remarks>
    public enum SpamdCommand
    {
        /// <summary>Classify only; response headers include <c>Spam:</c> score line.</summary>
        Check = 0,

        /// <summary>Classify and return hit symbols after the header block.</summary>
        Symbols = 1,

        /// <summary>Classify and return a full text report after the header block.</summary>
        Report = 2,

        /// <summary>Like <see cref="Report"/> only when the message is spam; otherwise an empty body.</summary>
        ReportIfSpam = 3,

        /// <summary>Scan and return the message with SpamAssassin headers inserted.</summary>
        Process = 4,

        /// <summary>Health check; no message body is sent.</summary>
        Ping = 5,

        /// <summary>Abort without scanning (connection opened then abandoned).</summary>
        Skip = 6,

        /// <summary>Bayesian learning / reporting (<c>TELL</c> with <c>Message-class</c> headers).</summary>
        Tell = 7,
    }
}
