// <copyright file="INodeSessionRegistry.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Session.Coordination
{
    /// <summary>
    /// Node-scoped Redis session registry purge and coordination helpers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Registry entries are created atomically in acquire/refresh Lua scripts. <see cref="NodeSessionRegistryFields.LeaseUpdated"/>
    /// is informational; key TTL is authoritative for orphan cleanup.
    /// </para>
    /// </remarks>
    public interface INodeSessionRegistry
    {
        /// <summary>Gets the maximum purge loop iterations (defensive bound).</summary>
        int MaxPurgeIterations { get; }

        /// <summary>Gets the batch size for each purge iteration.</summary>
        int PurgeBatchSize { get; }

        /// <summary>
        /// Releases all distributed leases indexed for <paramref name="nodeName"/> and deletes the node index.
        /// </summary>
        /// <param name="nodeName">Stable node identity.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Purge statistics.</returns>
        ValueTask<NodeSessionPurgeResult> PurgeNodeAsync(string nodeName, CancellationToken cancellationToken);
    }
}
