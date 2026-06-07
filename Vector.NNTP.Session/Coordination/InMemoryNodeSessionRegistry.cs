// <copyright file="InMemoryNodeSessionRegistry.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Session.Coordination
{
    /// <summary>
    /// No-op node session registry for hosts and tests without Redis.
    /// </summary>
    /// <remarks>
    /// <para>Returns zero purge statistics immediately; preserves the same defensive iteration constants as the Redis
    /// implementation for callers that log or compare limits.</para>
    /// </remarks>
    public sealed class InMemoryNodeSessionRegistry : INodeSessionRegistry
    {
        /// <summary>
        /// Gets the defensive upper bound on purge loop iterations (matches production registry default).
        /// </summary>
        public int MaxPurgeIterations => 100_000;

        /// <summary>
        /// Gets the batch size used by production purge loops when reporting progress (not exercised in-memory).
        /// </summary>
        public int PurgeBatchSize => 500;

        /// <summary>
        /// Returns an empty purge result without touching Redis.
        /// </summary>
        /// <param name="nodeName">Stable node identity (validated but unused).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Zeroed <see cref="NodeSessionPurgeResult"/>.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="nodeName"/> is null or empty.</exception>
        /// <exception cref="OperationCanceledException">Propagated when <paramref name="cancellationToken"/> is canceled.</exception>
        public ValueTask<NodeSessionPurgeResult> PurgeNodeAsync(string nodeName, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(nodeName);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new NodeSessionPurgeResult(0, 0, 0, false, 0));
        }
    }
}
