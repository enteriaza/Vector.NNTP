// <copyright file="ClusterEnvelopeEpochPrefilter.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// ClusterEnvelopeEpochPrefilter.Logging.cs -- Source-generated [LoggerMessage] static partial methods.

using System.Text.Json;

namespace Vector.NNTP.Encryption.Cluster
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> partial methods for
    /// <see cref="ClusterEnvelopeEpochPrefilter"/>.
    /// </summary>
    internal static partial class ClusterEnvelopeEpochPrefilter
    {
        /// <summary>
        /// Logs that envelope JSON prefilter parsing failed due to malformed JSON.
        /// </summary>
        /// <param name="logger">Logger for cluster envelope diagnostics.</param>
        /// <param name="exceptionType">JSON exception type name.</param>
        /// <param name="ex">The JSON parsing exception.</param>
        [LoggerMessage(EventId = 347, Level = LogLevel.Debug,
            Message = "Certificates: Cluster envelope epoch prefilter JSON parse failed ({ExceptionType})")]
        internal static partial void LogEnvelopePrefilterJsonFailed(ILogger logger, string exceptionType, JsonException ex);
    }
}
