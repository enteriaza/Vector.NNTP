// <copyright file="AuthMySqlTelemetry.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Auth.MySql.Telemetry
{
    /// <summary>
    /// OpenTelemetry-compatible <see cref="ActivitySource"/> for MySQL-backed NNTP authentication operations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Host registration:</b> Add <see cref="SourceName"/> to the host OpenTelemetry tracer provider
    /// (<c>builder.Tracing.AddSource(AuthMySqlTelemetry.SourceName)</c>) to collect spans. Without registration,
    /// activities are no-ops, matching the Encryption assembly pattern.
    /// </para>
    /// <para><b>Spans:</b> <c>auth.mysql.user.lookup</c>, <c>auth.mysql.validate.password</c>,
    /// <c>auth.mysql.validate.sasl</c>.</para>
    /// </remarks>
    internal static class AuthMySqlTelemetry
    {
        /// <summary>
        /// Activity source name for host SDK registration.
        /// </summary>
        internal const string SourceName = "Vector.NNTP.Auth.MySql";

        /// <summary>
        /// Shared activity source for credential validation and user-record lookups.
        /// </summary>
        internal static ActivitySource ActivitySource { get; } = new(SourceName, Utilities.Diagnostics.AssemblyInfoUtilities.ApplicationVersion);
    }
}
