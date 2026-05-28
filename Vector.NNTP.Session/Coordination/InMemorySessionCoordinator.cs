// <copyright file="InMemorySessionCoordinator.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Collections.Concurrent;

namespace Vector.NNTP.Session.Coordination
{
    /// <summary>
    /// In-memory admission coordinator for unit tests (no TTL modelling).
    /// </summary>
    public sealed class InMemorySessionCoordinator : INntpSessionCoordinator
    {
        private readonly ConcurrentDictionary<string, byte> _sessions = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _accountIps = new(StringComparer.Ordinal);

        /// <inheritdoc />
        public ValueTask<NntpSessionAdmissionResult> TryAdmitAsync(
            NntpSessionPolicy policy,
            string sessionId,
            string clientIpText,
            int ttlSeconds,
            CancellationToken cancellationToken)
        {
            _ = ttlSeconds;
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(policy);
            ArgumentException.ThrowIfNullOrEmpty(sessionId);
            ArgumentException.ThrowIfNullOrEmpty(clientIpText);

            if (!policy.RequiresDistributedAdmission())
            {
                return ValueTask.FromResult(NntpSessionAdmissionResult.Success);
            }

            int maxSessions = policy.SessionLimit > 0 ? policy.SessionLimit : int.MaxValue;
            int ipLimit = policy.SrcIpLimit > 0 ? policy.SrcIpLimit : int.MaxValue;
            if (maxSessions == int.MaxValue && ipLimit == int.MaxValue)
            {
                return ValueTask.FromResult(NntpSessionAdmissionResult.PolicyInvalid);
            }

            string slotKey = policy.AccountKey + "|" + sessionId;
            if (_sessions.ContainsKey(slotKey))
            {
                return ValueTask.FromResult(NntpSessionAdmissionResult.Success);
            }

            int currentSessions = CountSessionsForAccount(policy.AccountKey);
            if (currentSessions >= maxSessions)
            {
                return ValueTask.FromResult(NntpSessionAdmissionResult.MaxSessionsExceeded);
            }

            if (ipLimit != int.MaxValue)
            {
                ConcurrentDictionary<string, byte> ips = _accountIps.GetOrAdd(policy.AccountKey, _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
                if (!ips.ContainsKey(clientIpText))
                {
                    if (ips.Count >= ipLimit)
                    {
                        return ValueTask.FromResult(NntpSessionAdmissionResult.IpLimitExceeded);
                    }

                    _ = ips.TryAdd(clientIpText, 0);
                }
            }

            _sessions[slotKey] = 0;
            return ValueTask.FromResult(NntpSessionAdmissionResult.Success);
        }

        /// <inheritdoc />
        public ValueTask ReleaseAsync(
            NntpSessionPolicy policy,
            string sessionId,
            string clientIpText,
            CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            ArgumentNullException.ThrowIfNull(policy);
            string slotKey = policy.AccountKey + "|" + sessionId;
            _ = _sessions.TryRemove(slotKey, out _);

            if (_accountIps.TryGetValue(policy.AccountKey, out ConcurrentDictionary<string, byte>? ips))
            {
                bool ipStillUsed = false;
                string prefix = policy.AccountKey + "|";
                foreach (string key in _sessions.Keys)
                {
                    if (key.StartsWith(prefix, StringComparison.Ordinal) &&
                        _accountIps.TryGetValue(policy.AccountKey, out ConcurrentDictionary<string, byte>? ipSet) &&
                        ipSet.ContainsKey(clientIpText))
                    {
                        ipStillUsed = true;
                        break;
                    }
                }

                if (!ipStillUsed)
                {
                    _ = ips.TryRemove(clientIpText, out _);
                }
            }

            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Counts active sessions for an account key.
        /// </summary>
        /// <param name="accountKey">Normalized account key.</param>
        /// <returns>Number of held admission slots.</returns>
        private int CountSessionsForAccount(string accountKey)
        {
            int count = 0;
            string prefix = accountKey + "|";
            foreach (string key in _sessions.Keys)
            {
                if (key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }
    }
}
