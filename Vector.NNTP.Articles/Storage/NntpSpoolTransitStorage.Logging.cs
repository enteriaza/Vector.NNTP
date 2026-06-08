// <copyright file="NntpSpoolTransitStorage.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// EventId range: 300-319 (transit spool enqueue admission diagnostics).

namespace Vector.NNTP.Articles.Storage
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> logging partial for
    /// <see cref="NntpSpoolTransitStorage"/> enqueue admission diagnostics.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>EventId band:</b> 300-319 on <c>ILogger&lt;NntpSpoolTransitStorage&gt;</c>. Per-article writer failures
    /// remain on <see cref="NntpSpoolWriterPump"/> EventIds 1-99.
    /// </para>
    /// </remarks>
    internal sealed partial class NntpSpoolTransitStorage
    {
        /// <summary>
        /// Logs sustained enqueue reject pressure when the spool queue is saturated.
        /// </summary>
        /// <param name="logger">Transit storage category logger.</param>
        /// <param name="queueDepth">Current queue depth at rejection time.</param>
        /// <param name="queueCapacity">Configured queue item capacity.</param>
        /// <remarks>
        /// Rate-limited by the caller; pairs with <see cref="Metrics.NntpSpoolMetrics.RecordQueueSaturationLog"/>.
        /// </remarks>
        [LoggerMessage(
            EventId = 300,
            Level = LogLevel.Warning,
            Message = "Spool queue saturated; enqueue rejected (depth {QueueDepth}/{QueueCapacity}).")]
        private static partial void LogQueueSaturation(ILogger logger, long queueDepth, int queueCapacity);

        /// <summary>
        /// Logs a successful enqueue when trace-level diagnostics are enabled.
        /// </summary>
        /// <param name="logger">Transit storage category logger.</param>
        /// <param name="messageId">Transit Message-ID accepted into the queue.</param>
        /// <param name="payloadBytes">Enqueued article byte length.</param>
        [LoggerMessage(
            EventId = 301,
            Level = LogLevel.Debug,
            Message = "Spool enqueue accepted Message-ID {MessageId} ({PayloadBytes} bytes).")]
        private static partial void LogEnqueueAccepted(ILogger logger, string messageId, int payloadBytes);
    }
}
