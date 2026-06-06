// RabbitMqConnectionFactory.Diagnostics.cs -- client property value formatting for diagnostic logging.
//
// Contains the LogClientProperties method that iterates all client properties and emits a Debug-level log
// entry per property via the [LoggerMessage] source-generated LogClientProperty method in
// RabbitMqConnectionFactory.Logging.cs.
//
// Client property values are formatted via FormattingUtilities.FormatObjectValue in the shared Utilities
// namespace, which centralises the byte[] UTF-8 decoding and length-capped truncation logic for AMQP
// long-string values.
//
// Endpoint formatting (FormatEndpointSummary, FormatHostPort, AppendHostPort) has been moved to
// FormattingUtilities in the shared Utilities namespace to eliminate duplication and centralise IPv6
// bracket-notation logic used across the codebase.
//
// All methods are pure and stateless -- they read only from their parameters and allocate only the return value.
//
// Caller:
//   RabbitMqConnectionFactory.cs -- CreateConnectionAsync calls LogClientProperties after factory construction and
//   before the connection attempt.  LogClientProperties iterates factory.ClientProperties and calls the
//   [LoggerMessage] source-generated LogClientProperty (EventId 105, Debug) for each entry.
//
// Security:
//   FormatObjectValue decodes byte[] as UTF-8 with a length cap of MaxClientPropertyValueLength (1 KB)
//   to prevent excessive memory allocation from a malicious or misconfigured broker sending oversized AMQP table
//   values.  The factory.ClientProperties dictionary does not contain credentials -- it holds only operational
//   metadata (product, version, platform, application, machine) populated by PopulateClientProperties and the
//   RabbitMQ client library's defaults.
//
// Cross-platform:
//   Fully portable.  All APIs used (FormattingUtilities.FormatObjectValue, ILogger.IsEnabled, dictionary
//   iteration) are part of the .NET Base Class Library and behave identically on Windows (x64) and Linux (x64)
//   on .NET 8.  No P/Invoke, no OS-specific APIs, no architecture-specific intrinsics.
//
// SIMD applicability:
//   Not applicable.  This file contains dictionary iteration and delegates to FormattingUtilities for string
//   formatting.  There are no contiguous memory buffers, byte-level pattern searches, or bulk numeric
//   operations that would benefit from vector instructions.

using RabbitMQ.Client;
using Vector.NNTP.Utilities.Diagnostics;

namespace Vector.NNTP.MessageBus.Connections
{
    /// <summary>
    /// Client property diagnostic formatting for <see cref="RabbitMqConnectionFactory"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Responsibility:</b> Iterates all <see cref="ConnectionFactory.ClientProperties"/> and
    /// emits a <see cref="LogLevel.Debug"/>-level log entry per property via the <c>[LoggerMessage]</c>
    /// source-generated <see cref="LogClientProperty"/> method defined in <c>RabbitMqConnectionFactory.Logging.cs</c>.</para>
    ///
    /// <para><b>AMQP long-string decoding:</b> The RabbitMQ.Client library stores its default properties
    /// (<c>product</c>, <c>version</c>, <c>copyright</c>, <c>information</c>) as AMQP long-strings -- <c>byte[]</c>
    /// on the .NET side.  Without decoding, these render as <c>System.Byte[]</c> in log output.
    /// <see cref="FormattingUtilities.FormatObjectValue"/> detects <c>byte[]</c> values and decodes them as UTF-8
    /// with a length cap of <see cref="MaxClientPropertyValueLength"/> (1 KB).</para>
    ///
    /// <para><b>Security:</b> <see cref="ConnectionFactory.ClientProperties"/> is a separate
    /// dictionary from the credential fields (<see cref="ConnectionFactory.UserName"/>,
    /// <see cref="ConnectionFactory.Password"/>).  No credentials are present in client
    /// properties.</para>
    ///
    /// <para><b>Cross-platform:</b> Fully portable.  All APIs used are BCL types available on all .NET 8 runtimes
    /// (Windows x64, Linux x64).  No P/Invoke, no OS-specific APIs.</para>
    ///
    /// <para><b>SIMD applicability:</b> Not applicable.  Dictionary iteration and string formatting have no
    /// vectorisable computation paths.</para>
    /// </remarks>
    internal sealed partial class RabbitMqConnectionFactory
    {

        #region Private Methods -- Client Property Formatting

        /// <summary>
        /// Logs all <see cref="ConnectionFactory.ClientProperties"/> at <see cref="LogLevel.Debug"/>
        /// for operational diagnostics.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="IRabbitMqConnectionFactory"/> usage from the connection pool -- after factory
        /// and endpoint construction, before the connection attempt.</para>
        ///
        /// <para><b>AMQP long-string decoding:</b> The RabbitMQ.Client library stores its default properties
        /// (<c>product</c>, <c>version</c>, <c>copyright</c>, <c>information</c>) as AMQP long-strings -- <c>byte[]</c>
        /// on the .NET side.  Without decoding, these render as <c>System.Byte[]</c> or hex dumps in log output.
        /// <see cref="FormattingUtilities.FormatObjectValue"/> detects <c>byte[]</c> values and decodes them as UTF-8.
        /// All other value types (<c>string</c>, <c>int</c>, etc.) use their default <see cref="object.ToString"/>
        /// representation.</para>
        ///
        /// <para><b>Guard:</b> The logger level is checked via <see cref="ILogger.IsEnabled(LogLevel)"/> before iterating
        /// -- this is required by CONTRIBUTING.md Guard Clauses for Expensive Logging because the loop allocates a
        /// formatted string per property via <see cref="FormattingUtilities.FormatObjectValue"/>.  When Debug logging is
        /// disabled (the common production configuration), the method is a no-op: a single <c>IsEnabled</c> check with
        /// no allocations.</para>
        ///
        /// <para><b>Security:</b> <see cref="ConnectionFactory.ClientProperties"/> is a separate
        /// dictionary from the credential fields (<see cref="ConnectionFactory.UserName"/>,
        /// <see cref="ConnectionFactory.Password"/>).  The dictionary contains only operational metadata
        /// (<c>product</c>, <c>version</c>, <c>platform</c>, <c>application</c>, <c>machine</c>, etc.) populated by
        /// <see cref="PopulateClientProperties"/> and the library's defaults.  No credentials are present.</para>
        /// </remarks>
        /// <param name="factory">The configured factory whose properties to log.</param>
        private void LogClientProperties(ConnectionFactory factory)
        {
            if (!Logger.IsEnabled(LogLevel.Debug))
                return;
            foreach (KeyValuePair<string, object?> kvp in factory.ClientProperties)
            {
                LogClientProperty(kvp.Key, FormattingUtilities.FormatObjectValue(kvp.Value, MaxClientPropertyValueLength));
            }
        }

        #endregion

    }
}
