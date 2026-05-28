// <copyright file="INntpBlockQuotaCoordinator.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Session.Coordination
{
    /// <summary>
    /// Cluster-wide byte quota initialization and decrement for byte-limited accounts.
    /// </summary>
    public interface INntpBlockQuotaCoordinator
    {
        /// <summary>
        /// Ensures the account quota key exists (idempotent initialize).
        /// </summary>
        /// <param name="accountKey">Normalized account key.</param>
        /// <param name="initialBytes">Initial quota bytes.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns><see langword="true"/> when the key was created or lowered to policy.</returns>
        public ValueTask<bool> TryInitializeQuotaAsync(string accountKey, long initialBytes, CancellationToken cancellationToken);

        /// <summary>
        /// Decrements remaining quota by command bytes.
        /// </summary>
        /// <param name="accountKey">Normalized account key.</param>
        /// <param name="bytes">Bytes to decrement.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Remaining bytes after decrement.</returns>
        public ValueTask<long> DecrementAsync(string accountKey, long bytes, CancellationToken cancellationToken);
    }
}
