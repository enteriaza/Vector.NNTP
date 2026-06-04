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
    /// Initializes a new instance of the <see cref="InMemorySessionDatabase"/> class.
    /// </remarks>
    /// <param name="logger">Optional logger.</param>
    public sealed partial class InMemorySessionDatabase(ILogger<InMemorySessionDatabase>? logger = null) : ISessionDatabase
    {
        /// <summary>
        /// Logger.
        /// </summary>
        private readonly ILogger<InMemorySessionDatabase> _logger = logger ?? NullLogger<InMemorySessionDatabase>.Instance;

        /// <summary>
        /// Sessions dictionary.
        /// </summary>
        private readonly ConcurrentDictionary<string, SessionContext> _sessions =
            new(StringComparer.Ordinal);

        /// <summary>
        /// Tries to add a session to the database.
        /// </summary>
        /// <param name="session">The session context.</param>
        /// <returns><see langword="true"/> when inserted.</returns>
        public bool TryAdd(SessionContext session)
        {
            ArgumentNullException.ThrowIfNull(session);
            if (!_sessions.TryAdd(session.SessionId, session))
            {
                InMemorySessionDatabaseLog.SessionRegisteredDuplicate(_logger, session.SessionId);
                return false;
            }

            InMemorySessionDatabaseLog.SessionRegistered(_logger, session.SessionId, session.RemoteIp.ToString());
            return true;
        }

        /// <summary>
        /// Tries to get a session from the database.
        /// </summary>
        /// <param name="sessionId">The session ID.</param>
        /// <param name="session">The session context.</param>
        /// <returns><see langword="true"/> when found.</returns>
        public bool TryGet(string sessionId, out SessionContext session)
        {
            ArgumentException.ThrowIfNullOrEmpty(sessionId);
            return _sessions.TryGetValue(sessionId, out session!);
        }

        /// <summary>
        /// Tries to remove a session from the database.
        /// </summary>
        /// <param name="sessionId">The session ID.</param>
        /// <param name="removed">The removed session context.</param>
        /// <returns><see langword="true"/> when removed.</returns>
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
        /// <returns>The authenticated sessions.</returns>
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

        /// <inheritdoc />
        public IReadOnlyCollection<SessionContext> SnapshotAll()
        {
            return [.. _sessions.Values];
        }

        /// <inheritdoc />
        public IReadOnlyCollection<SessionContext> SnapshotTransitPeers()
        {
            List<SessionContext> list = new(_sessions.Count);
            foreach (SessionContext s in _sessions.Values)
            {
                if (!string.IsNullOrEmpty(s.TransitPeerId))
                {
                    list.Add(s);
                }
            }

            return list;
        }

        /// <summary>
        /// Counts authenticated sessions by account key.
        /// </summary>
        /// <param name="accountKey">The account key.</param>
        /// <returns>The count of authenticated sessions.</returns>
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
        /// <param name="accountKey">The account key.</param>
        /// <returns>The session IDs.</returns>
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
        /// <returns>The distinct account keys.</returns>
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
