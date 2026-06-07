// <copyright file="ClusterJsonContext.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Text.Json.Serialization;

namespace Vector.NNTP.Encryption.Cluster
{
    /// <summary>
    /// Source-generated JSON context for cluster certificate fanout messages.
    /// </summary>
    /// <remarks>
    /// Reflection-disabled serializer context for <see cref="ClusterBusEnvelope"/> and
    /// <see cref="ClusterCertificatePayload"/> used by <see cref="CertificateClusterSync"/> publish/consume paths.
    /// </remarks>
    [JsonSerializable(typeof(ClusterBusEnvelope))]
    [JsonSerializable(typeof(ClusterCertificatePayload))]
    internal partial class ClusterJsonContext : JsonSerializerContext
    {
    }
}
