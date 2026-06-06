// <copyright file="AuthMySqlMetrics.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: OpenTelemetry-style metrics for MySQL-backed NNTP authentication.

using System.Collections.Generic;
using System.Diagnostics.Metrics;

namespace Vector.NNTP.Auth.MySql.Telemetry
{
    /// <summary>
    /// OpenTelemetry-style metrics for MySQL-backed user lookups and credential validation in this assembly.
    /// </summary>
    /// <remarks>
    /// <para><b>Cardinality:</b> Labels are bounded to fixed outcome and mechanism strings only.</para>
    /// </remarks>
    internal sealed class AuthMySqlMetrics
    {
        /// <summary>
        /// Shared metrics meter for the Auth.MySql assembly.
        /// </summary>
        private static readonly Meter Meter = new("Vector.NNTP.Auth.MySql", "1.0.0");

        /// <summary>
        /// User-record lookup counter.
        /// </summary>
        private readonly Counter<long> _lookup;

        /// <summary>
        /// Credential validation counter.
        /// </summary>
        private readonly Counter<long> _validate;

        /// <summary>
        /// User-record lookup duration histogram in milliseconds.
        /// </summary>
        private readonly Histogram<double> _lookupDurationMs;

        /// <summary>
        /// Initializes metric instruments for the Auth.MySql assembly.
        /// </summary>
        internal AuthMySqlMetrics()
        {
            _lookup = Meter.CreateCounter<long>("auth.mysql.lookup");
            _validate = Meter.CreateCounter<long>("auth.mysql.validate");
            _lookupDurationMs = Meter.CreateHistogram<double>("auth.mysql.lookup.duration_ms");
        }

        /// <summary>
        /// Records a user lookup outcome.
        /// </summary>
        /// <param name="outcome">Bounded outcome label: found, not_found, transient_failure, or cache_hit.</param>
        internal void RecordLookup(string outcome)
        {
            _lookup.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
        }

        /// <summary>
        /// Records lookup duration in milliseconds.
        /// </summary>
        /// <param name="durationMs">Elapsed lookup time.</param>
        internal void RecordLookupDuration(double durationMs)
        {
            _lookupDurationMs.Record(durationMs);
        }

        /// <summary>
        /// Records a credential validation outcome.
        /// </summary>
        /// <param name="outcome">Bounded outcome label: success, invalid_credentials, or transient_failure.</param>
        /// <param name="mechanism">Bounded mechanism label: authinfo, sasl_scram, or sasl_cram.</param>
        internal void RecordValidate(string outcome, string mechanism)
        {
            _validate.Add(
                1,
                new KeyValuePair<string, object?>("outcome", outcome),
                new KeyValuePair<string, object?>("mechanism", mechanism));
        }
    }
}
