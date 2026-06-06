// <copyright file="MessageBusTelemetry.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// MessageBusTelemetry.cs -- OpenTelemetry activity source for MessageBus publish and consume operations.

using System.Diagnostics;

namespace Vector.NNTP.MessageBus.Telemetry
{
    /// <summary>
    /// OpenTelemetry-compatible <see cref="ActivitySource"/> for MessageBus publish and consume operations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Host registration:</b> Add <see cref="SourceName"/> to the host OpenTelemetry tracer provider
    /// (<c>builder.Tracing.AddSource(MessageBusTelemetry.SourceName)</c>) to collect spans. Without registration,
    /// activities are no-ops, matching the Auth.MySql and Encryption assembly pattern.
    /// </para>
    /// <para><b>Spans:</b> <c>messagebus.publish</c>, <c>messagebus.consume</c>, and connection/pool lifecycle spans
    /// emitted by pool, publisher, and consumer components.</para>
    /// <para>The source is process-wide and reused for all activity creation to avoid repeated allocations.</para>
    /// </remarks>
    internal static class MessageBusTelemetry
    {
        /// <summary>
        /// Activity source name for host SDK registration.
        /// </summary>
        internal const string SourceName = "Vector.NNTP.MessageBus";

        /// <summary>
        /// Shared telemetry source for MessageBus activities.
        /// </summary>
        internal static ActivitySource ActivitySource { get; } = new(
            SourceName,
            Utilities.Diagnostics.AssemblyInfoUtilities.ApplicationVersion);
    }
}
