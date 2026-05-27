// <copyright file="NntpSessionAdmissionTracker.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: in-memory implementation of NNTP session admission tracking.

using System.Collections.Concurrent;
using System.Net;
using Vector.NNTP.Sockets.Authentication;

namespace Vector.NNTP.Auth.MySql
{
    /// <summary>
    /// Default in-memory implementation of <see cref="INntpSessionAdmissionTracker"/> using per-account and per-source-IP counters.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Thread safety:</b> This implementation is safe for concurrent use by multiple threads. It uses
    /// <see cref="ConcurrentDictionary{TKey,TValue}"/> and <see cref="Interlocked"/> operations to maintain counters.
    /// </para>
    /// <para>
    /// <b>Lifetime:</b> The tracker is intended to be registered as a singleton in DI. Counters are purely in-memory and
    /// reset when the host process restarts.
    /// </para>
    /// </remarks>
    public sealed class NntpSessionAdmissionTracker : INntpSessionAdmissionTracker
    {
        private readonly ConcurrentDictionary<string, int> _accountCounts = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, int> _accountIpCounts = new(StringComparer.Ordinal);

        /// <inheritdoc />
        public bool TryEnter(NntpSessionPolicy policy, IPAddress clientIp)
        {
            ArgumentNullException.ThrowIfNull(policy);
            ArgumentNullException.ThrowIfNull(clientIp);

            string username = policy.Username;
            int sessionLimit = policy.SessionLimit;
            int srcIpLimit = policy.SrcIpLimit;

            if (sessionLimit <= 0 && srcIpLimit <= 0)
            {
                return true;
            }

            if (!TryIncrement(username, sessionLimit, _accountCounts))
            {
                return false;
            }

            string ipKey = CreateAccountIpKey(username, clientIp);
            if (!TryIncrement(ipKey, srcIpLimit, _accountIpCounts))
            {
                Decrement(username, _accountCounts);
                return false;
            }

            return true;
        }

        /// <inheritdoc />
        public void Leave(NntpSessionPolicy policy, IPAddress clientIp)
        {
            ArgumentNullException.ThrowIfNull(policy);
            ArgumentNullException.ThrowIfNull(clientIp);

            string username = policy.Username;
            Decrement(username, _accountCounts);

            string ipKey = CreateAccountIpKey(username, clientIp);
            Decrement(ipKey, _accountIpCounts);
        }

        /// <summary>
        /// Attempts to increment a counter for the given key without exceeding the configured limit.
        /// </summary>
        /// <param name="key">Counter key.</param>
        /// <param name="limit">Maximum allowed value for the counter; <c>0</c> disables enforcement.</param>
        /// <param name="counters">Dictionary containing counters.</param>
        /// <returns><see langword="true"/> when incremented successfully or limits are disabled.</returns>
        private static bool TryIncrement(string key, int limit, ConcurrentDictionary<string, int> counters)
        {
            if (limit <= 0)
            {
                return true;
            }

            while (true)
            {
                int existing = counters.TryGetValue(key, out int current) ? current : 0;
                if (existing >= limit)
                {
                    return false;
                }

                int next = existing + 1;
                if (counters.TryUpdate(key, next, existing))
                {
                    return true;
                }

                if (existing == 0 && counters.TryAdd(key, next))
                {
                    return true;
                }
            }
        }

        /// <summary>
        /// Decrements the counter for the given key, removing the entry when it reaches zero.
        /// </summary>
        /// <param name="key">Counter key.</param>
        /// <param name="counters">Dictionary containing counters.</param>
        private void Decrement(string key, ConcurrentDictionary<string, int> counters)
        {
            bool removed = false;
            while (!removed && counters.TryGetValue(key, out int current))
            {
                int next = current - 1;
                removed = next <= 0 ? counters.TryRemove(key, out int _) : counters.TryUpdate(key, next, current);
            }
        }

        /// <summary>
        /// Creates a stable compound key for per-account and per-IP counters.
        /// </summary>
        /// <param name="username">Account name.</param>
        /// <param name="clientIp">Client IP address.</param>
        /// <returns>Composite key string.</returns>
        private string CreateAccountIpKey(string username, IPAddress clientIp)
        {
            return string.Create(
                username.Length + 1 + clientIp.ToString().Length,
                (username, clientIp),
                static (span, state) =>
                {
                    state.username.AsSpan().CopyTo(span);
                    span[state.username.Length] = '|';
                    ReadOnlySpan<char> ipSpan = state.clientIp.ToString().AsSpan();
                    ipSpan.CopyTo(span[(state.username.Length + 1)..]);
                });
        }
    }
}
