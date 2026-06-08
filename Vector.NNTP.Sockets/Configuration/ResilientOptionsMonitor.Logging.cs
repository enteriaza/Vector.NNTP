// <copyright file="ResilientOptionsMonitor.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// EventIds 500-501 (resilient options monitor reload lifecycle).

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Vector.NNTP.Sockets.Configuration
{
    /// <summary>
    /// Source-generated logging helpers for <see cref="ResilientOptionsMonitor{TOptions}"/>.
    /// </summary>
    internal sealed partial class ResilientOptionsMonitor<TOptions>
        where TOptions : class
    {
        /// <summary>
        /// Logs that a configuration reload failed validation and the previous options snapshot was retained.
        /// </summary>
        /// <param name="logger">Category logger. Must not be <see langword="null"/>.</param>
        /// <param name="failures">
        /// Pipe-delimited validation messages from <see cref="OptionsValidationException.Failures"/>.
        /// </param>
        [LoggerMessage(
            EventId = 500,
            Level = LogLevel.Error,
            Message = "Configuration reload failed validation; retaining previous options: {Failures}")]
        private static partial void LogOptionsValidationFailedRetainedPrevious(ILogger logger, string failures);

        /// <summary>
        /// Logs that a change listener threw while handling an accepted reload.
        /// </summary>
        /// <param name="logger">Category logger. Must not be <see langword="null"/>.</param>
        /// <param name="exception">Exception raised by the listener.</param>
        [LoggerMessage(
            EventId = 501,
            Level = LogLevel.Warning,
            Message = "Options change listener faulted during reload notification")]
        private static partial void LogChangeListenerFault(ILogger logger, Exception exception);
    }
}
