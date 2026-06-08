// <copyright file="AuthMySqlTelemetry.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// AuthMySqlTelemetry.cs -- OpenTelemetry activity source for MySQL-backed NNTP authentication.

namespace Vector.NNTP.Auth.MySql.Telemetry
{
    /// <summary>
    /// OpenTelemetry-compatible <see cref="ActivitySource"/> for MySQL-backed NNTP authentication operations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Role:</b> Central tracing surface for the <c>Vector.NNTP.Auth.MySql</c> assembly. Spans bracket user-record
    /// database lookups and credential finalization in <see cref="Records.MySqlUserRecordStore"/> and
    /// <see cref="Credentials.MySqlNntpCredentialValidator"/>. Complements counters and histograms on
    /// <see cref="AuthMySqlMetrics"/> (metrics record outcomes and lookup duration; traces expose operation boundaries).
    /// </para>
    /// <para>
    /// <b>Host registration:</b> Add <see cref="SourceName"/> to the host OpenTelemetry tracer provider
    /// (<c>builder.Tracing.AddSource(AuthMySqlTelemetry.SourceName)</c>) to collect spans. Without registration,
    /// <c>ActivitySource.StartActivity(...)</c> returns <see langword="null"/> and callers treat activities as no-ops,
    /// matching the <c>Vector.NNTP.Encryption</c> and <c>Vector.NNTP.HistoryDB</c> assembly pattern.
    /// </para>
    /// <para><b>Span catalog:</b></para>
    /// <list type="table">
    /// <listheader><term>Operation name</term><description>Emitter and <see cref="ActivityKind"/></description></listheader>
    /// <item>
    /// <term><c>auth.mysql.user.lookup</c></term>
    /// <description>
    /// <see cref="Records.MySqlUserRecordStore"/> sync/async lookup paths — <see cref="ActivityKind.Client"/> (outbound
    /// database I/O).
    /// </description>
    /// </item>
    /// <item>
    /// <term><c>auth.mysql.validate.password</c></term>
    /// <description>
    /// <see cref="Credentials.MySqlNntpCredentialValidator"/> password finalization (for example AUTHINFO USER/PASS) —
    /// <see cref="ActivityKind.Internal"/>.
    /// </description>
    /// </item>
    /// <item>
    /// <term><c>auth.mysql.validate.sasl</c></term>
    /// <description>
    /// <see cref="Credentials.MySqlNntpCredentialValidator"/> SASL finalization after mechanism handlers complete —
    /// <see cref="ActivityKind.Internal"/>.
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// <b>Error status:</b> Emitters call <see cref="Activity.SetStatus(ActivityStatusCode, string?)"/> with
    /// <see cref="Configuration.AuthMySqlFailureReason"/> text only on unexpected backend faults (lookup exceptions,
    /// transient validation failures). Expected rejections such as invalid credentials do not mark the span as errored.
    /// </para>
    /// <para>
    /// <b>Privacy:</b> Spans do not attach usernames, passwords, or client IP tags at this layer; structured auth logging
    /// remains on <see cref="Microsoft.Extensions.Logging.ILogger"/> categories in the credential validator and record store.
    /// </para>
    /// <para><b>Thread safety:</b> Static read-only <see cref="ActivitySource"/>; safe for concurrent NNTP session handlers.</para>
    /// <para><b>Allocation:</b> One process-wide <see cref="ActivitySource"/> instance; per-auth <see cref="Activity"/>
    /// objects are created only when tracing is enabled.</para>
    /// </remarks>
    internal static class AuthMySqlTelemetry
    {
        /// <summary>
        /// Logical name registered with the host OpenTelemetry tracer provider via <c>AddSource</c>.
        /// </summary>
        /// <value>Literal <c>Vector.NNTP.Auth.MySql</c>.</value>
        /// <remarks>
        /// Must match the first argument passed to the <see cref="ActivitySource"/> constructor backing
        /// <see cref="ActivitySource"/>. Hosts that omit this name from tracing configuration receive no Auth.MySql spans.
        /// </remarks>
        internal const string SourceName = "Vector.NNTP.Auth.MySql";

        /// <summary>
        /// Shared activity source for credential validation and user-record lookups in this assembly.
        /// </summary>
        /// <value>
        /// A singleton <see cref="ActivitySource"/> constructed with <see cref="SourceName"/> and
        /// <see cref="Utilities.Diagnostics.AssemblyInfoUtilities.ApplicationVersion"/> as the source version.
        /// </value>
        /// <remarks>
        /// <para>
        /// Initialized once at type load. Callers use <c>using Activity? activity = ActivitySource.StartActivity(...)</c>
        /// so activities are disposed when the scoped operation completes.
        /// </para>
        /// <para>
        /// Version metadata helps trace backends correlate span schema changes with deployed assembly builds; it does not
        /// affect span naming.
        /// </para>
        /// </remarks>
        internal static ActivitySource ActivitySource { get; } = new(SourceName, Utilities.Diagnostics.AssemblyInfoUtilities.ApplicationVersion);
    }
}
