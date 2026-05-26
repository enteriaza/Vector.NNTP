// <copyright file="LengthLimitedReadStream.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: diagnostics/formatting/startup validation; readability over micro-optimization; allocations acceptable.
// LengthLimitedReadStream.Logging.cs -- Source-generated [LoggerMessage] partial methods for LengthLimitedReadStream.
//
// Uses the [LoggerMessage] source generator pattern mandated by CONTRIBUTING.md for compile-time validation,
// zero-allocation logging, and consistent structure.  The source generator discovers the _logger field (ILogger?)
// by convention.  Because the field is nullable, the generated code includes a null-check before formatting --
// when _logger is null (e.g. unit tests), the method is a zero-cost no-op.
//
// Event ID allocation:
//   300-309  Limit Enforcement  (LengthLimitedReadStream.cs)
//
// Naming convention:
//   Each method name matches the logical operation it logs, enabling grep-based correlation between log output
//   (which includes the EventId name) and the source code definition.
//
// Log level policy (aligned with CONTRIBUTING.md Log Levels):
//   Warning  -- Response size limit exceeded: indicates a potential security concern (compromised or MITM'd API
//               endpoint) or an unexpectedly large legitimate response.  The caller
//               (AcmeCertificateProvider.SendCloudflareRequestAsync) catches the resulting
//               InvalidOperationException and may log it at a higher level if needed.
//
// Security:
//   No method logs credentials, API tokens, zone IDs, or infrastructure identifiers.  The {Operation} parameter
//   is a short descriptor (e.g. "POST /dns_records") that does not contain sensitive content.  {MaxBytes} and
//   {TotalBytesRead} are numeric values with no sensitive information.
//
// ASCII-only:
//   All Message strings contain only ASCII characters (U+0020-U+007E) per CONTRIBUTING.md.  Unicode characters
//   (em-dash, arrows, etc.) are replaced with their ASCII equivalents (--,  ->, etc.).
//
// SIMD applicability:
//   Not applicable.  This file contains only [LoggerMessage] attribute declarations and XML documentation.  No
//   executable logic, no buffers, no computation.
//
// Cross-platform compatibility:
//   Fully compatible with Linux and Windows.  [LoggerMessage] source-generated methods use only BCL logging
//   abstractions.  No platform-specific APIs.

namespace Vector.NNTP.Utilities.IO
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessage"/> partial methods for <see cref="LengthLimitedReadStream"/>.
    /// </summary>
    public sealed partial class LengthLimitedReadStream
    {
        /// <summary>
        /// Logs that the cumulative byte limit has been reached or exceeded, immediately before the
        /// <see cref="InvalidOperationException"/> is thrown by <see cref="ThrowLimitExceeded"/>.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="ThrowLimitExceeded"/> -- invoked when a read attempt finds
        /// <c>remaining &lt;= 0</c>.  Guarded by <c>_logger is not null</c> at the call site so the method is only
        /// called when a logger was provided at construction time.</para>
        ///
        /// <para><b>Level rationale:</b> <see cref="LogLevel.Warning"/> because exceeding the response size limit
        /// indicates a potential security concern (compromised or MITM'd API endpoint) or an unexpectedly large
        /// legitimate response.  The caller's <c>SendCloudflareRequestAsync</c> (or equivalent) catches the resulting <see cref="InvalidOperationException"/> and may
        /// log it at a higher level if needed.  Warning provides structured context (<c>Operation</c>,
        /// <c>MaxBytes</c>, <c>TotalBytesRead</c>) that is captured by Serilog sinks even if the exception message
        /// is not parsed as structured data.</para>
        ///
        /// <para><b>Security:</b> <c>{Operation}</c> is a short descriptor (e.g. <c>"POST /dns_records"</c>) that
        /// does not contain credentials or infrastructure identifiers.  <c>{MaxBytes}</c> and
        /// <c>{TotalBytesRead}</c> are numeric values with no sensitive content.</para>
        ///
        /// <para><b>Format specifier:</b> <c>:N0</c> on <c>{MaxBytes}</c> and <c>{TotalBytesRead}</c> produces
        /// locale-aware thousand separators (e.g. <c>1,048,576</c>) for human readability in log viewers.  The
        /// structured log properties retain the raw <see cref="long"/> values for programmatic filtering
        /// and alerting.</para>
        /// </remarks>
        [LoggerMessage(EventId = 300, Level = LogLevel.Warning,
            Message = "Certificates: Cloudflare API {Operation} response exceeded the {MaxBytes:N0}-byte safety limit " +
                      "at {TotalBytesRead:N0} bytes read -- possible compromised endpoint")]
        private partial void LogLimitExceeded(string operation, long maxBytes, long totalBytesRead);
    }
}
