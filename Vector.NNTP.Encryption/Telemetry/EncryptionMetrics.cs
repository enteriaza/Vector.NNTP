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
        /// Counter <c>encryption.renewal.check</c> labelled by bounded <c>outcome</c> values from <see cref="RecordRenewalCheck"/>.
        /// </summary>
        private readonly Counter<long> _renewalCheck;

        /// <summary>
        /// Counter <c>encryption.certificate.issue</c> labelled by bounded <c>outcome</c> values from <see cref="RecordCertificateIssue"/>.
        /// </summary>
        private readonly Counter<long> _certificateIssue;

        /// <summary>
        /// Histogram <c>encryption.certificate.issue.duration_ms</c> for end-to-end ACME issuance latency.
        /// </summary>
        private readonly Histogram<double> _certificateIssueDurationMs;

        /// <summary>
        /// Histogram <c>encryption.dns.propagation.duration_ms</c> for authoritative TXT quorum polling.
        /// </summary>
        private readonly Histogram<double> _dnsPropagationDurationMs;

        /// <summary>
        /// Counter <c>encryption.cluster.message</c> labelled by bounded <c>outcome</c> values from <see cref="RecordClusterMessage"/>.
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
        /// <param name="outcome">Bounded outcome: <c>skipped</c>, <c>renewed</c>, <c>failed</c>, or <c>no_cert</c>.</param>
        /// <remarks>Labels are fixed strings to keep metric cardinality bounded.</remarks>
        internal void RecordRenewalCheck(string outcome)
        {
            _renewalCheck.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
        }

        /// <summary>
        /// Records a certificate issuance outcome.
        /// </summary>
        /// <param name="outcome">Bounded outcome: <c>success</c>, <c>transient_failure</c>, or <c>cancelled</c>.</param>
        /// <remarks>Labels are fixed strings to keep metric cardinality bounded.</remarks>
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
        /// <param name="outcome">Bounded outcome: <c>published</c>, <c>accepted</c>, <c>rejected</c>, or <c>invalid_hmac</c>.</param>
        /// <remarks>Labels are fixed strings to keep metric cardinality bounded.</remarks>
        internal void RecordClusterMessage(string outcome)
        {
            _clusterMessage.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
        }
    }
}
