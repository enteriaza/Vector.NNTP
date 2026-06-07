// <copyright file="NodeSessionRegistryFields.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Session.Coordination
{
    /// <summary>
    /// Redis HASH field names for <c>session:{sessionId}</c> node registry metadata.
    /// </summary>
    public static class NodeSessionRegistryFields
    {
        /// <summary>Cluster node that owns the lease.</summary>
        public const string Node = "node";

        /// <summary>Lease kind: <c>auth</c> or <c>transit</c>.</summary>
        public const string Kind = "kind";

        /// <summary>Normalized account key (auth only).</summary>
        public const string AccountKey = "accountKey";

        /// <summary>Client IP text (auth only).</summary>
        public const string ClientIp = "clientIp";

        /// <summary>
        /// Configured transit peer name (transit only). Stored under the legacy hash key <c>peerId</c> for compatibility.
        /// </summary>
        public const string PeerId = "peerId";

        /// <summary>Unix milliseconds when the lease was first acquired.</summary>
        public const string Created = "created";

        /// <summary>
        /// Unix milliseconds when the lease was last refreshed (informational only; TTL is authoritative).
        /// </summary>
        public const string LeaseUpdated = "leaseUpdated";

        /// <summary>Auth session kind value.</summary>
        public const string KindAuth = "auth";

        /// <summary>Transit session kind value.</summary>
        public const string KindTransit = "transit";
    }
}
