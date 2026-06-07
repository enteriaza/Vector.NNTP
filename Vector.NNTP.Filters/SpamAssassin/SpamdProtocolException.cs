// <copyright file="SpamdProtocolException.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: spamd wire or response parse failures surfaced to callers and logs.
// SpamdProtocolException.cs -- Exception type for non-zero spamd exit codes, malformed replies, and transport failures.

using System.Runtime.Serialization;
using System.Security;

namespace Vector.NNTP.Filters.SpamAssassin
{
    /// <summary>
    /// Indicates spamd returned a non-zero sysexits code, an unexpected reply, or the wire session failed after connect.
    /// </summary>
    /// <remarks>
    /// <para><b>Failure classes:</b> Public constructors map to post-connect wire-session failure modes; TCP connect failures use
    /// <see cref="SpamdConnectionException"/> instead:</para>
    /// <list type="number">
    /// <item><description>Malformed protocol or unexpected response text — message-only constructor; <see cref="ExitCode"/> is <see langword="null"/> and <see cref="IsSpamdError"/> is <see langword="false"/>.</description></item>
    /// <item><description>Send, receive, or cancellation failure after connect — message plus inner <see cref="Exception"/>; <see cref="ExitCode"/> is <see langword="null"/> and <see cref="IsSpamdError"/> is <see langword="false"/>.</description></item>
    /// <item><description>Non-zero spamd status line — exit code, status message, and full status line are populated; <see cref="IsSpamdError"/> is <see langword="true"/>.</description></item>
    /// </list>
    /// <para><b>Operations:</b> Transit spool integration typically treats this as a fail-open signal (accept the article) while logging
    /// structured fields such as <see cref="ExitCode"/> and <see cref="StatusMessage"/> rather than <see cref="Exception.Message"/> alone.</para>
    /// <para><b>Serialization:</b> Marked <see cref="SerializableAttribute"/> so instances can round-trip through legacy binary formatters
    /// and diagnostic dumps that preserve custom exception state.</para>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// catch (SpamdProtocolException ex)
    /// {
    ///     if (ex.IsSpamdError)
    ///     {
    ///         logger.LogWarning(ex, "spamd status {ExitCode} {StatusMessage}", ex.ExitCode, ex.StatusMessage);
    ///     }
    ///     else
    ///     {
    ///         logger.LogWarning(ex, "SpamAssassin wire failure");
    ///     }
    /// }
    /// ]]></code>
    /// </example>
    /// </remarks>
    [Serializable]
    public class SpamdProtocolException : Exception
    {
        /// <summary>
        /// Initializes a new instance for malformed replies or protocol violations without an underlying I/O exception.
        /// </summary>
        /// <param name="message">Human-readable error description; forwarded to <see cref="Exception.Message"/>.</param>
        /// <remarks>
        /// <see cref="ExitCode"/>, <see cref="StatusMessage"/>, and <see cref="StatusLine"/> remain <see langword="null"/>.
        /// Thrown by <see cref="SpamdWireSession"/> when response lines, headers, or bodies cannot be parsed.
        /// </remarks>
        public SpamdProtocolException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance wrapping a send, receive, or cancellation failure after the TCP session is established.
        /// </summary>
        /// <param name="message">Human-readable error description; forwarded to <see cref="Exception.Message"/>.</param>
        /// <param name="innerException">
        /// Underlying failure (typically <see cref="SocketException"/>, <see cref="IOException"/>, or
        /// <see cref="OperationCanceledException"/>); may be <see langword="null"/> per base <see cref="Exception"/> behavior.
        /// </param>
        /// <remarks>
        /// <see cref="ExitCode"/>, <see cref="StatusMessage"/>, and <see cref="StatusLine"/> remain <see langword="null"/>.
        /// For failures before connect completes, see <see cref="SpamdConnectionException"/>.
        /// </remarks>
        protected SpamdProtocolException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Initializes a new instance for a non-zero spamd status line (<c>SPAMD/x.y CODE MESSAGE</c>).
        /// </summary>
        /// <param name="exitCode">sysexits.h code from the status line (non-zero).</param>
        /// <param name="statusMessage">Text after the numeric code (for example <c>EX_TEMPFAIL</c> or <c>Can't create user directory</c>).</param>
        /// <param name="statusLine">Full first-line response returned by spamd.</param>
        /// <remarks>
        /// Sets <see cref="ExitCode"/>, <see cref="StatusMessage"/>, and <see cref="StatusLine"/> and synthesizes
        /// <see cref="Exception.Message"/> as <c>spamd returned exit code {exitCode} ({statusMessage}): {statusLine}</c>.
        /// </remarks>
        public SpamdProtocolException(int exitCode, string statusMessage, string statusLine)
            : base($"spamd returned exit code {exitCode} ({statusMessage}): {statusLine}")
        {
            ExitCode = exitCode;
            StatusMessage = statusMessage;
            StatusLine = statusLine;
        }

        /// <summary>
        /// Deserializes exception state produced by legacy binary formatters.
        /// </summary>
        /// <param name="info">Serialization payload containing <see cref="ExitCode"/>, <see cref="StatusMessage"/>, and <see cref="StatusLine"/>.</param>
        /// <param name="context">Serialization stream context; unused.</param>
        /// <remarks>
        /// Restores custom fields in addition to base <see cref="Exception"/> state. Private on this sealed type; invoked only by
        /// legacy formatters — application code must not call this constructor.
        /// </remarks>
        /// <exception cref="SerializationException">Thrown when required serialization entries are missing or invalid.</exception>
        /// <exception cref="SecurityException">Thrown when the caller lacks serialization permission.</exception>
        [Obsolete("This API supports obsolete formatter-based serialization. It should not be called from application code.", DiagnosticId = "SYSLIB0051", UrlFormat = "https://aka.ms/dotnet-warnings/{0}")]
        protected SpamdProtocolException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            ExitCode = (int?)info.GetValue(nameof(ExitCode), typeof(int?));
            StatusMessage = (string?)info.GetValue(nameof(StatusMessage), typeof(string));
            StatusLine = (string?)info.GetValue(nameof(StatusLine), typeof(string));
        }

        /// <summary>
        /// Gets the sysexits.h code from the spamd status line when spamd returned a structured error; otherwise <see langword="null"/>.
        /// </summary>
        /// <remarks>
        /// <see langword="null"/> for malformed protocol text, transport failures, and cancellation — there is no meaningful spamd exit code in those cases.
        /// When non-null, <see cref="IsSpamdError"/> is <see langword="true"/>.
        /// </remarks>
        public int? ExitCode { get; }

        /// <summary>
        /// Gets the status text after the numeric code on the spamd status line (for example <c>EX_PROTOCOL</c> or <c>EX_UNAVAILABLE</c>).
        /// </summary>
        /// <remarks>
        /// <see langword="null"/> when the failure was not produced by the non-zero status-line constructor.
        /// </remarks>
        public string? StatusMessage { get; }

        /// <summary>
        /// Gets the full first-line response from spamd (<c>SPAMD/x.y CODE MESSAGE</c>).
        /// </summary>
        /// <remarks>
        /// <see langword="null"/> when the failure was not produced by the non-zero status-line constructor.
        /// </remarks>
        public string? StatusLine { get; }

        /// <summary>
        /// Gets a value indicating whether this exception represents a non-zero spamd status line rather than a transport or parse failure.
        /// </summary>
        /// <remarks>
        /// Equivalent to <c><see cref="ExitCode"/>.HasValue</c>. Use in <c>catch</c> blocks to distinguish spamd-reported errors
        /// (for example <c>EX_TEMPFAIL</c>) from wire or parsing failures where only <see cref="Exception.Message"/> and
        /// <see cref="Exception.InnerException"/> are populated.
        /// </remarks>
        public bool IsSpamdError => ExitCode.HasValue;

        /// <summary>
        /// Populates the serialization payload with custom exception state for legacy binary formatters.
        /// </summary>
        /// <param name="info">Serialization payload to populate with <see cref="ExitCode"/>, <see cref="StatusMessage"/>, and <see cref="StatusLine"/>.</param>
        /// <param name="context">Serialization stream context; forwarded to the base implementation.</param>
        /// <remarks>
        /// Application code must not call this override; it exists only for formatter-based round-trip of custom fields.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="info"/> is <see langword="null"/>.</exception>
        /// <exception cref="SecurityException">Thrown when the caller lacks serialization permission.</exception>
        [Obsolete("This API supports obsolete formatter-based serialization. It should not be called from application code.", DiagnosticId = "SYSLIB0051", UrlFormat = "https://aka.ms/dotnet-warnings/{0}")]
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(nameof(ExitCode), ExitCode, typeof(int?));
            info.AddValue(nameof(StatusMessage), StatusMessage, typeof(string));
            info.AddValue(nameof(StatusLine), StatusLine, typeof(string));
        }
    }
}
