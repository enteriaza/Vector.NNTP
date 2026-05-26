// <copyright file="ClusterJsonContext.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Text.Json.Serialization;

namespace Vector.NNTP.Encryption.Cluster
{
    /// <summary>
    /// Source-generated JSON context for cluster certificate fanout messages.
    /// </summary>
    [JsonSerializable(typeof(ClusterBusEnvelope))]
    [JsonSerializable(typeof(ClusterCertificatePayload))]
    internal partial class ClusterJsonContext : JsonSerializerContext
    {
    }
}
