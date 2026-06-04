// <copyright file="InMemoryNodeSessionRegistry.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Session.Coordination
{
    /// <summary>
    /// No-op node session registry for hosts and tests without Redis.
    /// </summary>
    public sealed class InMemoryNodeSessionRegistry : INodeSessionRegistry
    {
        /// <inheritdoc />
        public int MaxPurgeIterations => 100_000;

        /// <inheritdoc />
        public int PurgeBatchSize => 500;

        /// <inheritdoc />
        public ValueTask<NodeSessionPurgeResult> PurgeNodeAsync(string nodeName, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(nodeName);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new NodeSessionPurgeResult(0, 0, 0, false, 0));
        }
    }
}
