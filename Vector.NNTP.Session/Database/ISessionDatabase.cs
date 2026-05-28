// <copyright file="ISessionDatabase.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Session.Database
{
    /// <summary>
    /// Node-local registry of connection sessions for the lifetime of each TCP connection.
    /// </summary>
    public interface ISessionDatabase
    {
        /// <summary>
        /// Registers a new connection session at TCP accept.
        /// </summary>
        /// <param name="session">Session row in <see cref="AuthenticationState.Unauthenticated"/>.</param>
        /// <returns><see langword="true"/> when inserted.</returns>
        public bool TryAdd(SessionContext session);

        /// <summary>
        /// Gets a session by identifier.
        /// </summary>
        /// <param name="sessionId">Session identifier.</param>
        /// <param name="session">Located session when found.</param>
        /// <returns><see langword="true"/> when found.</returns>
        public bool TryGet(string sessionId, out SessionContext session);

        /// <summary>
        /// Removes a session row on connection teardown (idempotent-safe).
        /// </summary>
        /// <param name="sessionId">Session identifier.</param>
        /// <param name="removed">Removed row when present.</param>
        /// <returns><see langword="true"/> when a row was removed.</returns>
        public bool TryRemove(string sessionId, out SessionContext? removed);

        /// <summary>
        /// Returns a point-in-time snapshot of authenticated sessions for heartbeat and local rate allocation.
        /// </summary>
        /// <returns>Authenticated rows only (idle connections count).</returns>
        public IReadOnlyCollection<SessionContext> SnapshotAuthenticated();

        /// <summary>
        /// Counts authenticated sessions sharing an account key on this node.
        /// </summary>
        /// <param name="accountKey">Normalized account key.</param>
        /// <returns>Count of live authenticated TCP sessions for fair-share divisor.</returns>
        public int CountAuthenticatedByAccountKey(string accountKey);

        /// <summary>
        /// Returns session identifiers on this node that hold or are acquiring a Redis slot for the account.
        /// </summary>
        /// <param name="accountKey">Normalized account key.</param>
        /// <returns>Live connection session ids used to purge orphaned Redis anchors.</returns>
        public IReadOnlyCollection<string> SnapshotSessionIdsForAccount(string accountKey);

        /// <summary>
        /// Returns distinct account keys for connections on this node that are authenticated or mid-authentication.
        /// </summary>
        /// <returns>Account keys eligible for periodic Redis reconciliation sweeps.</returns>
        public IReadOnlyCollection<string> SnapshotDistinctAccountKeys();
    }
}
