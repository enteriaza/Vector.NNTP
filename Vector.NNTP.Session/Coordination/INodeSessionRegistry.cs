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
        /// <summary>Gets the defensive upper bound on purge loop iterations to prevent unbounded Redis scans.</summary>
        int MaxPurgeIterations { get; }

        /// <summary>Gets the number of lease entries processed per purge batch for progress logging.</summary>
        int PurgeBatchSize { get; }

        /// <summary>
        /// Releases all distributed leases indexed for <paramref name="nodeName"/> and deletes the node index.
        /// </summary>
        /// <param name="nodeName">Stable node identity.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Purge statistics.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="nodeName"/> is null or empty.</exception>
        /// <exception cref="OperationCanceledException">Propagated when <paramref name="cancellationToken"/> is canceled.</exception>
        ValueTask<NodeSessionPurgeResult> PurgeNodeAsync(string nodeName, CancellationToken cancellationToken);
    }
}
