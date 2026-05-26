// <copyright file="ClusterBusEnvelope.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Encryption.Cluster
{
    /// <summary>
    /// JSON envelope for cluster certificate fanout messages published to RabbitMQ.
    /// </summary>
    internal sealed class ClusterBusEnvelope
    {
        /// <summary>
        /// Wire schema version for forward-compatible deserialization.
        /// </summary>
        public int SchemaVersion { get; set; } = 1;

        /// <summary>
        /// Logical payload type identifier (for example <c>vector.nntp.certificate.cluster.v1</c>).
        /// </summary>
        public string PayloadType { get; set; } = string.Empty;

        /// <summary>
        /// Cluster certificate payload body.
        /// </summary>
        public ClusterCertificatePayload? Payload { get; set; }
    }
}
