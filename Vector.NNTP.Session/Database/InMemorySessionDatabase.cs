// <copyright file="InMemorySessionDatabase.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Vector.NNTP.Session.Database
{
    /// <summary>
    /// ConcurrentDictionary-backed node-local session database.
    /// </summary>
    /// <remarks>
    /// <para>Thread-safe via <see cref="ConcurrentDictionary{TKey,TValue}"/>; suitable for single-node hosts and unit tests
    /// without Redis session coordination.</para>
    /// </remarks>
    /// <param name="logger">Optional logger for registration and teardown diagnostics.</param>
    public sealed partial class InMemorySessionDatabase(ILogger<InMemorySessionDatabase>? logger = null) : ISessionDatabase
    {
        /// <summary>
        /// Logger for session registration, duplicate insert, and removal events.
        /// </summary>
        private readonly ILogger<InMemorySessionDatabase> _logger = logger ?? NullLogger<InMemorySessionDatabase>.Instance;

        /// <summary>
        /// Live connection sessions keyed by <see cref="SessionContext.SessionId"/>.
        /// </summary>
        private readonly ConcurrentDictionary<string, SessionContext> _sessions =
            new(StringComparer.Ordinal);

        /// <summary>
        /// Tries to add a session to the database.
        /// </summary>
        /// <param name="session">Session row to register at TCP accept.</param>
        /// <returns><see langword="true"/> when inserted.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="session"/> is null.</exception>
        public bool TryAdd(SessionContext session)
        {
            ArgumentNullException.ThrowIfNull(session);
            if (!_sessions.TryAdd(session.SessionId, session))
            {
                InMemorySessionDatabaseLog.SessionRegisteredDuplicate(_logger, session.ConnectionLogPrefix, session.SessionId);
                return false;
            }

            InMemorySessionDatabaseLog.SessionRegistered(_logger, session.ConnectionLogPrefix, session.SessionId);
            return true;
        }

        /// <summary>
        /// Tries to get a session from the database.
        /// </summary>
        /// <param name="sessionId">Session identifier.</param>
        /// <param name="session">Located session when found.</param>
        /// <returns><see langword="true"/> when found.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="sessionId"/> is null or empty.</exception>
        public bool TryGet(string sessionId, out SessionContext session)
        {
            ArgumentException.ThrowIfNullOrEmpty(sessionId);
            return _sessions.TryGetValue(sessionId, out session!);
        }

        /// <summary>
        /// Tries to remove a session from the database.
        /// </summary>
        /// <param name="sessionId">Session identifier.</param>
        /// <param name="removed">Removed row when present.</param>
        /// <returns><see langword="true"/> when removed.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="sessionId"/> is null or empty.</exception>
        public bool TryRemove(string sessionId, out SessionContext? removed)
        {
            removed = null;
            ArgumentException.ThrowIfNullOrEmpty(sessionId);
            if (_sessions.TryRemove(sessionId, out SessionContext? v))
            {
                removed = v;
                InMemorySessionDatabaseLog.SessionRemoved(_logger, sessionId, "teardown");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Takes a snapshot of authenticated sessions.
        /// </summary>
        /// <returns>Point-in-time copy of rows in <see cref="AuthenticationState.Authenticated"/>.</returns>
        public IReadOnlyCollection<SessionContext> SnapshotAuthenticated()
        {
            List<SessionContext> list = new(_sessions.Count);
            foreach (SessionContext s in _sessions.Values)
            {
                if (s.AuthenticationState == AuthenticationState.Authenticated)
                {
                    list.Add(s);
                }
            }

            return list;
        }

        /// <summary>
        /// Returns a point-in-time snapshot of every connection session row on this node.
        /// </summary>
        /// <returns>All live TCP sessions regardless of authentication state.</returns>
        public IReadOnlyCollection<SessionContext> SnapshotAll()
        {
            return [.. _sessions.Values];
        }

        /// <summary>
        /// Returns a snapshot of trusted transit peer connections on this node.
        /// </summary>
        /// <returns>Sessions with a non-empty <see cref="SessionContext.TransitPeerName"/>.</returns>
        public IReadOnlyCollection<SessionContext> SnapshotTransitPeers()
        {
            List<SessionContext> list = new(_sessions.Count);
            foreach (SessionContext s in _sessions.Values)
            {
                if (!string.IsNullOrEmpty(s.TransitPeerName))
                {
                    list.Add(s);
                }
            }

            return list;
        }

        /// <summary>
        /// Counts authenticated sessions by account key.
        /// </summary>
        /// <param name="accountKey">Normalized account key.</param>
        /// <returns>Count of live authenticated TCP sessions for fair-share divisor.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="accountKey"/> is null or empty.</exception>
        public int CountAuthenticatedByAccountKey(string accountKey)
        {
            ArgumentException.ThrowIfNullOrEmpty(accountKey);
            int count = 0;
            foreach (SessionContext s in _sessions.Values)
            {
                if (s.AuthenticationState == AuthenticationState.Authenticated &&
                    string.Equals(s.AccountKey, accountKey, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Takes a snapshot of session IDs for an account.
        /// </summary>
        /// <param name="accountKey">Normalized account key.</param>
        /// <returns>Live connection session ids holding or acquiring a slot for the account.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="accountKey"/> is null or empty.</exception>
        public IReadOnlyCollection<string> SnapshotSessionIdsForAccount(string accountKey)
        {
            ArgumentException.ThrowIfNullOrEmpty(accountKey);
            List<string> ids = new(_sessions.Count);
            foreach (SessionContext session in _sessions.Values)
            {
                if (!string.Equals(session.AccountKey, accountKey, StringComparison.Ordinal))
                {
                    continue;
                }

                if (session.AuthenticationState is AuthenticationState.Authenticated or AuthenticationState.Authenticating)
                {
                    ids.Add(session.SessionId);
                }
            }

            return ids;
        }

        /// <summary>
        /// Takes a snapshot of distinct account keys.
        /// </summary>
        /// <returns>Distinct account keys for authenticated or authenticating connections on this node.</returns>
        public IReadOnlyCollection<string> SnapshotDistinctAccountKeys()
        {
            HashSet<string> keys = new(StringComparer.Ordinal);
            foreach (SessionContext session in _sessions.Values)
            {
                if (string.IsNullOrEmpty(session.AccountKey))
                {
                    continue;
                }

                if (session.AuthenticationState is AuthenticationState.Authenticated or AuthenticationState.Authenticating)
                {
                    _ = keys.Add(session.AccountKey);
                }
            }

            return [.. keys];
        }
    }
}
