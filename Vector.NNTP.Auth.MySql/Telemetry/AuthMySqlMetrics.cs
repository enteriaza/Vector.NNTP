// <copyright file="AuthMySqlMetrics.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: OpenTelemetry-style metrics for MySQL-backed NNTP authentication.

using System.Diagnostics.Metrics;
using Vector.NNTP.Utilities.Diagnostics;

namespace Vector.NNTP.Auth.MySql.Telemetry
{
    /// <summary>
    /// OpenTelemetry <see cref="Meter"/> instruments for MySQL-backed user lookups and credential validation in this
    /// assembly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Role:</b> Records bounded-outcome counters and lookup latency for reader authentication. Complements distributed
    /// traces from <see cref="AuthMySqlTelemetry"/> (metrics aggregate outcomes; traces bracket individual operations).
    /// </para>
    /// <para><b>Producers:</b></para>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <see cref="Records.MySqlUserRecordStore"/> — <see cref="RecordLookup"/> for database outcomes and
    /// <see cref="RecordLookupDuration"/> on every sync/async lookup attempt (including failures).
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <see cref="Records.CachingMySqlUserRecordStore"/> — <see cref="RecordLookup"/> with <c>cache_hit</c> on
    /// username-only authentication cache hits.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <see cref="Credentials.MySqlNntpCredentialValidator"/> — <see cref="RecordLookup"/> with <c>cache_hit</c> on
    /// password-fingerprint cache hits and <see cref="RecordValidate"/> on password and SASL finalization outcomes.
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// <b>Registration:</b> Singleton via
    /// <see cref="DependencyInjection.ServiceCollectionExtensions.AddNntpMySqlAuth"/>; one instance is injected into the
    /// record store, caching decorator, and credential validator.
    /// </para>
    /// <para>
    /// <b>Cardinality:</b> Tag values are fixed literal strings chosen by callers. Do not pass usernames, client IPs, or
    /// raw mechanism names — only the documented outcome and mechanism buckets.
    /// </para>
    /// <para><b>Thread safety:</b> <see cref="Meter"/> instruments are safe for concurrent NNTP session handlers.</para>
    /// </remarks>
    internal sealed class AuthMySqlMetrics
    {
        /// <summary>
        /// Shared <see cref="Meter"/> instance for the <c>Vector.NNTP.Auth.MySql</c> assembly.
        /// </summary>
        /// <value>
        /// Named <c>Vector.NNTP.Auth.MySql</c> with version <see cref="AssemblyInfoUtilities.ApplicationVersion"/>.
        /// </value>
        /// <remarks>
        /// All <see cref="AuthMySqlMetrics"/> instances register instruments on this static meter. Host OpenTelemetry metric
        /// exporters subscribe to meters by name at the SDK layer. Version metadata aligns with
        /// <see cref="AuthMySqlTelemetry.ActivitySource"/> for consistent build correlation in backends.
        /// </remarks>
        private static readonly Meter Meter = new("Vector.NNTP.Auth.MySql", AssemblyInfoUtilities.ApplicationVersion);

        /// <summary>
        /// Counter instrument <c>auth.mysql.lookup</c> tagged by <c>outcome</c>.
        /// </summary>
        /// <remarks>
        /// Incremented by <see cref="RecordLookup"/>. Does not record lookup duration; see
        /// <see cref="_lookupDurationMs"/>.
        /// </remarks>
        private readonly Counter<long> _lookup;

        /// <summary>
        /// Counter instrument <c>auth.mysql.validate</c> tagged by <c>outcome</c> and <c>mechanism</c>.
        /// </summary>
        /// <remarks>Incremented by <see cref="RecordValidate"/> once per credential finalization attempt.</remarks>
        private readonly Counter<long> _validate;

        /// <summary>
        /// Histogram instrument <c>auth.mysql.lookup.duration_ms</c> for MySQL round-trip latency.
        /// </summary>
        /// <remarks>
        /// Recorded by <see cref="RecordLookupDuration"/> only from <see cref="Records.MySqlUserRecordStore"/> after
        /// actual database I/O. Cache hits do not emit duration samples.
        /// </remarks>
        private readonly Histogram<double> _lookupDurationMs;

        /// <summary>
        /// Initializes counter and histogram instruments on the shared <see cref="Meter"/>.
        /// </summary>
        /// <remarks>
        /// <para>Creates:</para>
        /// <list type="bullet">
        /// <item><description><c>auth.mysql.lookup</c> — <see cref="Counter{T}"/> with <c>outcome</c> tag.</description></item>
        /// <item><description><c>auth.mysql.validate</c> — <see cref="Counter{T}"/> with <c>outcome</c> and <c>mechanism</c> tags.</description></item>
        /// <item><description><c>auth.mysql.lookup.duration_ms</c> — <see cref="Histogram{T}"/> without dimensions.</description></item>
        /// </list>
        /// <para>Called once per DI singleton instance at host startup.</para>
        /// </remarks>
        internal AuthMySqlMetrics()
        {
            _lookup = Meter.CreateCounter<long>("auth.mysql.lookup");
            _validate = Meter.CreateCounter<long>("auth.mysql.validate");
            _lookupDurationMs = Meter.CreateHistogram<double>("auth.mysql.lookup.duration_ms");
        }

        /// <summary>
        /// Increments <c>auth.mysql.lookup</c> for a user-record lookup or cache hit.
        /// </summary>
        /// <param name="outcome">
        /// Bounded <c>outcome</c> tag. Expected values:
        /// <c>found</c> (row returned from MySQL),
        /// <c>not_found</c> (query succeeded, no row),
        /// <c>transient_failure</c> (lookup fault classified by <see cref="Configuration.AuthMySqlFailureClassifier"/>),
        /// or <c>cache_hit</c> (successful-authentication cache served the record without MySQL I/O).
        /// </param>
        /// <remarks>
        /// <para>
        /// <c>cache_hit</c> is emitted by <see cref="Records.CachingMySqlUserRecordStore"/> (username-only entries) and by
        /// <see cref="Credentials.MySqlNntpCredentialValidator"/> (password-fingerprint entries). Database paths in
        /// <see cref="Records.MySqlUserRecordStore"/> emit <c>found</c>, <c>not_found</c>, or <c>transient_failure</c>
        /// only.
        /// </para>
        /// <para>Never throws. Callers must not pass unbounded strings (for example account names) as tags.</para>
        /// </remarks>
        internal void RecordLookup(string outcome)
        {
            _lookup.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
        }

        /// <summary>
        /// Records elapsed MySQL lookup time in milliseconds on <c>auth.mysql.lookup.duration_ms</c>.
        /// </summary>
        /// <param name="durationMs">
        /// Wall-clock milliseconds for a single lookup attempt, typically from <see cref="Stopwatch"/>
        /// around connection open through reader completion in <see cref="Records.MySqlUserRecordStore"/>.
        /// </param>
        /// <remarks>
        /// <para>
        /// Emitted in a <c>finally</c> block for both successful and failed database lookups. Not called on authentication
        /// cache hits that bypass <see cref="Records.MySqlUserRecordStore"/>.
        /// </para>
        /// <para>Never throws.</para>
        /// </remarks>
        internal void RecordLookupDuration(double durationMs)
        {
            _lookupDurationMs.Record(durationMs);
        }

        /// <summary>
        /// Increments <c>auth.mysql.validate</c> for a credential finalization outcome.
        /// </summary>
        /// <param name="outcome">
        /// Bounded <c>outcome</c> tag: <c>success</c>, <c>invalid_credentials</c> (user not found, disabled account,
        /// mechanism not permitted, or password mismatch), or <c>transient_failure</c> (unexpected backend fault
        /// returning <see cref="Sockets.Authentication.NntpAuthResult.TransientFailure"/>).
        /// </param>
        /// <param name="mechanism">
        /// Bounded <c>mechanism</c> tag mapped by <c>MapMechanismMetric</c> in
        /// <see cref="Credentials.MySqlNntpCredentialValidator"/>: <c>authinfo</c> (AUTHINFO and other non-SASL paths),
        /// <c>sasl_scram</c>, or <c>sasl_cram</c>.
        /// </param>
        /// <remarks>
        /// <para>
        /// Recorded once per password or SASL finalize path in <see cref="Credentials.MySqlNntpCredentialValidator"/>,
        /// including expected credential rejections. Operation cancellation does not increment this counter.
        /// </para>
        /// <para>Never throws. Tag strings are part of the external metrics contract — change only with dashboard migration.</para>
        /// </remarks>
        internal void RecordValidate(string outcome, string mechanism)
        {
            _validate.Add(
                1,
                new KeyValuePair<string, object?>("outcome", outcome),
                new KeyValuePair<string, object?>("mechanism", mechanism));
        }
    }
}
