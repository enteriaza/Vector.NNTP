// <copyright file="EncryptionMetrics.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: OpenTelemetry-style metrics for certificate renewal and cluster sync.

using System.Diagnostics.Metrics;

namespace Vector.NNTP.Encryption.Telemetry
{
    /// <summary>
    /// OpenTelemetry-style metrics for ACME certificate renewal, DNS propagation, and cluster messaging.
    /// </summary>
    /// <remarks>
    /// <para><b>Cardinality:</b> Labels are bounded to fixed outcome strings only.</para>
    /// </remarks>
    internal sealed class EncryptionMetrics
    {
        /// <summary>
        /// Shared metrics meter for the Encryption assembly.
        /// </summary>
        private static readonly Meter Meter = new("Vector.NNTP.Encryption", "1.0.0");

        /// <summary>
        /// Steady-state renewal check counter.
        /// </summary>
        private readonly Counter<long> _renewalCheck;

        /// <summary>
        /// Certificate issuance counter.
        /// </summary>
        private readonly Counter<long> _certificateIssue;

        /// <summary>
        /// Certificate issuance duration histogram in milliseconds.
        /// </summary>
        private readonly Histogram<double> _certificateIssueDurationMs;

        /// <summary>
        /// DNS TXT propagation duration histogram in milliseconds.
        /// </summary>
        private readonly Histogram<double> _dnsPropagationDurationMs;

        /// <summary>
        /// Cluster message counter.
        /// </summary>
        private readonly Counter<long> _clusterMessage;

        /// <summary>
        /// Initializes metric instruments for the Encryption assembly.
        /// </summary>
        internal EncryptionMetrics()
        {
            _renewalCheck = Meter.CreateCounter<long>("encryption.renewal.check");
            _certificateIssue = Meter.CreateCounter<long>("encryption.certificate.issue");
            _certificateIssueDurationMs = Meter.CreateHistogram<double>("encryption.certificate.issue.duration_ms");
            _dnsPropagationDurationMs = Meter.CreateHistogram<double>("encryption.dns.propagation.duration_ms");
            _clusterMessage = Meter.CreateCounter<long>("encryption.cluster.message");
        }

        /// <summary>
        /// Records a steady-state renewal check outcome.
        /// </summary>
        /// <param name="outcome">Bounded outcome: skipped, renewed, failed, or no_cert.</param>
        internal void RecordRenewalCheck(string outcome)
        {
            _renewalCheck.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
        }

        /// <summary>
        /// Records a certificate issuance outcome.
        /// </summary>
        /// <param name="outcome">Bounded outcome: success, transient_failure, or cancelled.</param>
        internal void RecordCertificateIssue(string outcome)
        {
            _certificateIssue.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
        }

        /// <summary>
        /// Records certificate issuance duration in milliseconds.
        /// </summary>
        /// <param name="durationMs">Elapsed issuance time.</param>
        internal void RecordCertificateIssueDuration(double durationMs)
        {
            _certificateIssueDurationMs.Record(durationMs);
        }

        /// <summary>
        /// Records DNS TXT propagation duration in milliseconds.
        /// </summary>
        /// <param name="durationMs">Elapsed propagation poll time.</param>
        internal void RecordDnsPropagationDuration(double durationMs)
        {
            _dnsPropagationDurationMs.Record(durationMs);
        }

        /// <summary>
        /// Records a cluster message outcome.
        /// </summary>
        /// <param name="outcome">Bounded outcome: published, accepted, rejected, or invalid_hmac.</param>
        internal void RecordClusterMessage(string outcome)
        {
            _clusterMessage.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
        }
    }
}
