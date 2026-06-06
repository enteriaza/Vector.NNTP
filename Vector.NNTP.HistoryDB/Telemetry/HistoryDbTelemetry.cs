// <copyright file="HistoryDbTelemetry.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HistoryDbTelemetry.cs -- OpenTelemetry activity source for HistoryDB cold-path operations.

using System.Diagnostics;
using Vector.NNTP.Utilities.Diagnostics;

namespace Vector.NNTP.HistoryDB.Telemetry
{
    /// <summary>
    /// OpenTelemetry-compatible <see cref="ActivitySource"/> for HistoryDB rebuild, Redis, persist, and sweep operations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Host registration:</b> Add <see cref="SourceName"/> to the host OpenTelemetry tracer provider
    /// (<c>builder.Tracing.AddSource(HistoryDbTelemetry.SourceName)</c>) to collect spans. Without registration,
    /// activities are no-ops, matching the MessageBus and Auth.MySql assembly pattern.
    /// </para>
    /// <para><b>Spans:</b> <c>history.rebuild</c>, <c>history.check.redis</c>, <c>history.record.redis</c>,
    /// <c>history.rocks.persist</c>, and <c>history.rocks.sweep</c>. Memory-hit CHECK is intentionally not traced.</para>
    /// </remarks>
    internal static class HistoryDbTelemetry
    {
        /// <summary>
        /// Activity source name for host SDK registration.
        /// </summary>
        internal const string SourceName = "Vector.NNTP.HistoryDB";

        /// <summary>
        /// Shared telemetry source for HistoryDB activities.
        /// </summary>
        internal static ActivitySource ActivitySource { get; } = new(
            SourceName,
            AssemblyInfoUtilities.ApplicationVersion);
    }
}
