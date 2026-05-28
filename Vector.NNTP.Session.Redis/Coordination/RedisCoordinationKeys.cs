// <copyright file="RedisCoordinationKeys.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
namespace Vector.NNTP.Session.Redis.Coordination
{
    /// <summary>
    /// Builds Redis key strings for session coordination under a configurable prefix.
    /// </summary>
    internal readonly struct RedisCoordinationKeys
    {
        private readonly string _prefix;

        /// <summary>
        /// Initializes a new instance of the <see cref="RedisCoordinationKeys"/> struct.
        /// </summary>
        /// <param name="keyPrefix">Leading segment for all keys.</param>
        internal RedisCoordinationKeys(string keyPrefix)
        {
            _prefix = string.IsNullOrWhiteSpace(keyPrefix) ? string.Empty : keyPrefix;
        }

        /// <summary>Session count key for an account.</summary>
        /// <param name="accountKey">Normalized account key.</param>
        /// <returns>Redis key.</returns>
        internal string Sessions(string accountKey)
        {
            return _prefix + "acct:" + accountKey + ":sessions";
        }

        /// <summary>Distinct IP set key for an account.</summary>
        /// <param name="accountKey">Normalized account key.</param>
        /// <returns>Redis key.</returns>
        internal string Ips(string accountKey)
        {
            return _prefix + "acct:" + accountKey + ":ips";
        }

        /// <summary>Per-IP session set key.</summary>
        /// <param name="accountKey">Normalized account key.</param>
        /// <param name="ip">Client IP text.</param>
        /// <returns>Redis key.</returns>
        internal string IpSessions(string accountKey, string ip)
        {
            return _prefix + "acct:" + accountKey + ":ip:" + ip;
        }

        /// <summary>Session anchor key.</summary>
        /// <param name="accountKey">Normalized account key.</param>
        /// <param name="sessionId">Session identifier.</param>
        /// <returns>Redis key.</returns>
        internal string SessionAnchor(string accountKey, string sessionId)
        {
            return _prefix + "acct:" + accountKey + ":sess:" + sessionId;
        }

        /// <summary>Prefix through <c>:sess:</c> for parsing anchor key suffixes.</summary>
        /// <param name="accountKey">Normalized account key.</param>
        /// <returns>Redis key prefix.</returns>
        internal string SessionAnchorPrefix(string accountKey)
        {
            return _prefix + "acct:" + accountKey + ":sess:";
        }

        /// <summary>Ephemeral set key listing live session ids during reconciliation.</summary>
        /// <param name="accountKey">Normalized account key.</param>
        /// <returns>Redis key.</returns>
        internal string ReconciliationLiveSet(string accountKey)
        {
            return _prefix + "acct:" + accountKey + ":reconcile:live";
        }

        /// <summary>Byte quota key for an account.</summary>
        /// <param name="accountKey">Normalized account key.</param>
        /// <returns>Redis key.</returns>
        internal string Quota(string accountKey)
        {
            return _prefix + "acct:" + accountKey + ":quota";
        }

        /// <summary>SCAN pattern for session anchors.</summary>
        /// <param name="accountKey">Normalized account key.</param>
        /// <returns>Redis key pattern.</returns>
        internal string SessionAnchorPattern(string accountKey)
        {
            return _prefix + "acct:" + accountKey + ":sess:*";
        }

        /// <summary>SCAN pattern for per-IP session sets.</summary>
        /// <param name="accountKey">Normalized account key.</param>
        /// <returns>Redis key pattern.</returns>
        internal string IpSessionsPattern(string accountKey)
        {
            return _prefix + "acct:" + accountKey + ":ip:*";
        }

        /// <summary>Prefix for per-IP session set keys.</summary>
        /// <param name="accountKey">Normalized account key.</param>
        /// <returns>Redis key prefix.</returns>
        internal string IpSessionsPrefix(string accountKey)
        {
            return _prefix + "acct:" + accountKey + ":ip:";
        }
    }
}
