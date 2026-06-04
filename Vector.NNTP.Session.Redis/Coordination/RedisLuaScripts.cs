// <copyright file="RedisLuaScripts.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
namespace Vector.NNTP.Session.Redis.Coordination
{
    /// <summary>
    /// Embedded Lua scripts for Redis session coordination.
    /// </summary>
    internal static class RedisLuaScripts
    {
        /// <summary>Lua script: acquire authenticated session slot (v1).</summary>
        internal const string AuthSessionAcquireV1 = """
-- AUTH_SESSION_ACQUIRE_V1
local session_key = KEYS[4]
local sessions_key = KEYS[1]
local ips_key = KEYS[2]
local ip_sessions_key = KEYS[3]
local session_id = ARGV[1]
local ip = ARGV[2]
local max_sessions = tonumber(ARGV[3])
local ip_limit = tonumber(ARGV[4])
local ttl = tonumber(ARGV[5])
if redis.call("EXISTS", session_key) == 1 then
    redis.call("EXPIRE", session_key, ttl)
    redis.call("EXPIRE", ip_sessions_key, ttl)
    return 0
end
local current_sessions = tonumber(redis.call("GET", sessions_key) or "0")
if current_sessions >= max_sessions then
    return 1
end
local ip_exists = redis.call("SISMEMBER", ips_key, ip)
if ip_exists == 0 then
    local ip_count = redis.call("SCARD", ips_key)
    if ip_count >= ip_limit then
        return 2
    end
end
redis.call("SET", session_key, ip, "EX", ttl)
redis.call("INCR", sessions_key)
redis.call("EXPIRE", sessions_key, ttl)
redis.call("SADD", ips_key, ip)
redis.call("EXPIRE", ips_key, ttl)
redis.call("SADD", ip_sessions_key, session_id)
redis.call("EXPIRE", ip_sessions_key, ttl)
return 0
""";

        /// <summary>Lua script: release authenticated session slot (v1).</summary>
        internal const string AuthSessionReleaseV1 = """
-- AUTH_SESSION_RELEASE_V1
local sessions_key = KEYS[1]
local ips_key = KEYS[2]
local ip_sessions_key = KEYS[3]
local session_key = KEYS[4]
local session_id = ARGV[1]
local ip = ARGV[2]
if redis.call("EXISTS", session_key) == 0 then
    return 0
end
redis.call("DEL", session_key)
local current = tonumber(redis.call("GET", sessions_key) or "0")
if current > 0 then
    redis.call("DECR", sessions_key)
end
redis.call("SREM", ip_sessions_key, session_id)
local remaining = redis.call("SCARD", ip_sessions_key)
if remaining == 0 then
    redis.call("DEL", ip_sessions_key)
    redis.call("SREM", ips_key, ip)
end
return 0
""";

        /// <summary>Lua script: refresh session lease TTL (v1).</summary>
        internal const string SessionHeartbeatV1 = """
local ttl = tonumber(ARGV[1])
redis.call("EXPIRE", KEYS[1], ttl)
redis.call("EXPIRE", KEYS[2], ttl)
redis.call("EXPIRE", KEYS[3], ttl)
redis.call("EXPIRE", KEYS[4], ttl)
return 0
""";

        /// <summary>Lua script: remove session anchors not present in the live set (v1).</summary>
        internal const string AuthSessionPurgeOrphansV1 = """
-- AUTH_SESSION_PURGE_ORPHANS_V1
local sessions_key = KEYS[1]
local ips_key = KEYS[2]
local live_set = KEYS[3]
local anchor_pattern = ARGV[1]
local anchor_prefix_len = tonumber(ARGV[2])
local ip_pattern = ARGV[3]
local ip_prefix_len = tonumber(ARGV[4])
local max_calls = tonumber(ARGV[5])
local scan_count = tonumber(ARGV[6])
local max_keys = tonumber(ARGV[7])
local purged = 0
local cursor = "0"
for i = 1, max_calls do
    local res = redis.call("SCAN", cursor, "MATCH", anchor_pattern, "COUNT", scan_count)
    cursor = res[1]
    local keys = res[2]
    for j = 1, #keys do
        local anchor_key = keys[j]
        local sid = string.sub(anchor_key, anchor_prefix_len + 1)
        if redis.call("SISMEMBER", live_set, sid) == 0 then
            if redis.call("DEL", anchor_key) == 1 then
                purged = purged + 1
                local current = tonumber(redis.call("GET", sessions_key) or "0")
                if current > 0 then
                    redis.call("DECR", sessions_key)
                end
            end
        end
        if max_keys > 0 and purged >= max_keys then
            cursor = "0"
            break
        end
    end
    if cursor == "0" then
        break
    end
end
cursor = "0"
for i = 1, max_calls do
    local res = redis.call("SCAN", cursor, "MATCH", ip_pattern, "COUNT", scan_count)
    cursor = res[1]
    local keys = res[2]
    for j = 1, #keys do
        local ip_key = keys[j]
        local members = redis.call("SMEMBERS", ip_key)
        for k = 1, #members do
            local sid = members[k]
            if redis.call("SISMEMBER", live_set, sid) == 0 then
                redis.call("SREM", ip_key, sid)
            end
        end
        if redis.call("SCARD", ip_key) == 0 then
            local ip = string.sub(ip_key, ip_prefix_len + 1)
            redis.call("DEL", ip_key)
            redis.call("SREM", ips_key, ip)
        end
    end
    if cursor == "0" then
        break
    end
end
return purged
""";

        /// <summary>Lua script: decrement byte quota (v1).</summary>
        internal const string QuotaDecrV1 = """
local remaining = redis.call("DECRBY", KEYS[1], ARGV[1])
return remaining
""";

        /// <summary>Lua script: reconcile session count (v1).</summary>
        internal const string SessionReconcileV1 = """
local sessions_key = KEYS[1]
local pattern = ARGV[1]
local max_calls = tonumber(ARGV[2])
local scan_count = tonumber(ARGV[3])
local max_keys = tonumber(ARGV[4])
local cursor = "0"
local seen = 0
for i = 1, max_calls do
    local res = redis.call("SCAN", cursor, "MATCH", pattern, "COUNT", scan_count)
    cursor = res[1]
    local keys = res[2]
    local n = #keys
    if n > 0 then
        seen = seen + n
        if max_keys > 0 and seen >= max_keys then
            break
        end
    end
    if cursor == "0" then
        break
    end
end
redis.call("SET", sessions_key, tostring(seen))
return seen
""";

        /// <summary>Lua script: reconcile distinct IP set (v1).</summary>
        internal const string IpReconcileV1 = """
local ips_key = KEYS[1]
local pattern = ARGV[1]
local prefix = ARGV[2]
local max_calls = tonumber(ARGV[3])
local scan_count = tonumber(ARGV[4])
local max_keys = tonumber(ARGV[5])
local cursor = "0"
local seen = 0
for i = 1, max_calls do
    local res = redis.call("SCAN", cursor, "MATCH", pattern, "COUNT", scan_count)
    cursor = res[1]
    local keys = res[2]
    for j = 1, #keys do
        local ip_sessions_key = keys[j]
        local ip = string.sub(ip_sessions_key, string.len(prefix) + 1)
        local scard = redis.call("SCARD", ip_sessions_key)
        if scard > 0 then
            redis.call("SADD", ips_key, ip)
        else
            redis.call("DEL", ip_sessions_key)
            redis.call("SREM", ips_key, ip)
        end
        seen = seen + 1
        if max_keys > 0 and seen >= max_keys then
            cursor = "0"
            break
        end
    end
    if cursor == "0" then
        break
    end
end
return seen
""";

        /// <summary>Lua script: acquire transit peer session in ZSET (v1). Returns 0 success, 1 at capacity.</summary>
        internal const string TransitPeerAcquireV1 = """
-- TRANSIT_PEER_ACQUIRE_V1
local sessions_key = KEYS[1]
local now = tonumber(ARGV[1])
local lease = tonumber(ARGV[2])
local max = tonumber(ARGV[3])
local session_id = ARGV[4]
local cutoff = now - lease
redis.call('ZREMRANGEBYSCORE', sessions_key, '-inf', cutoff)
if redis.call('ZSCORE', sessions_key, session_id) then
    redis.call('ZADD', sessions_key, now, session_id)
    return 0
end
if max > 0 and redis.call('ZCARD', sessions_key) >= max then
    return 1
end
redis.call('ZADD', sessions_key, now, session_id)
return 0
""";

        /// <summary>Lua script: release transit peer session from ZSET (v1).</summary>
        internal const string TransitPeerReleaseV1 = """
-- TRANSIT_PEER_RELEASE_V1
local sessions_key = KEYS[1]
local session_id = ARGV[1]
redis.call('ZREM', sessions_key, session_id)
return 0
""";

        /// <summary>Lua script: refresh transit peer session score (v1).</summary>
        internal const string TransitPeerRefreshV1 = """
-- TRANSIT_PEER_REFRESH_V1
local sessions_key = KEYS[1]
local now = tonumber(ARGV[1])
local session_id = ARGV[2]
if redis.call('ZSCORE', sessions_key, session_id) then
    redis.call('ZADD', sessions_key, now, session_id)
end
return 0
""";

        /// <summary>Lua script: purge stale transit peer sessions and return ZCARD (v1).</summary>
        internal const string TransitPeerReconcileV1 = """
-- TRANSIT_PEER_RECONCILE_V1
local sessions_key = KEYS[1]
local now = tonumber(ARGV[1])
local lease = tonumber(ARGV[2])
local cutoff = now - lease
redis.call('ZREMRANGEBYSCORE', sessions_key, '-inf', cutoff)
return redis.call('ZCARD', sessions_key)
""";

        /// <summary>
        /// Lua script: acquire auth session with node registry (v2). KEYS[5]=session meta, KEYS[6]=node sessions SET.
        /// ARGV[7]=node, [8]=accountKey, [9]=nowMs, [10]=metaTtl. Returns same codes as v1.
        /// </summary>
        internal const string AuthSessionAcquireV2 = """
-- AUTH_SESSION_ACQUIRE_V2
local session_key = KEYS[4]
local sessions_key = KEYS[1]
local ips_key = KEYS[2]
local ip_sessions_key = KEYS[3]
local meta_key = KEYS[5]
local node_set = KEYS[6]
local session_id = ARGV[1]
local ip = ARGV[2]
local max_sessions = tonumber(ARGV[3])
local ip_limit = tonumber(ARGV[4])
local ttl = tonumber(ARGV[5])
local node_name = ARGV[7]
local account_key = ARGV[8]
local now_ms = ARGV[9]
local meta_ttl = tonumber(ARGV[10])
local function refresh_meta()
  if redis.call('EXISTS', meta_key) == 0 then
    redis.call('HSET', meta_key, 'node', node_name, 'kind', 'auth', 'accountKey', account_key, 'clientIp', ip, 'created', now_ms, 'leaseUpdated', now_ms)
  else
    redis.call('HSET', meta_key, 'leaseUpdated', now_ms)
  end
  redis.call('SADD', node_set, session_id)
  redis.call('EXPIRE', meta_key, meta_ttl)
  redis.call('EXPIRE', node_set, meta_ttl)
end
if redis.call('EXISTS', session_key) == 1 then
  redis.call('EXPIRE', session_key, ttl)
  redis.call('EXPIRE', ip_sessions_key, ttl)
  refresh_meta()
  return 0
end
local current_sessions = tonumber(redis.call('GET', sessions_key) or '0')
if current_sessions >= max_sessions then
  return 1
end
local ip_exists = redis.call('SISMEMBER', ips_key, ip)
if ip_exists == 0 then
  local ip_count = redis.call('SCARD', ips_key)
  if ip_count >= ip_limit then
    return 2
  end
end
redis.call('SET', session_key, ip, 'EX', ttl)
redis.call('INCR', sessions_key)
redis.call('EXPIRE', sessions_key, ttl)
redis.call('SADD', ips_key, ip)
redis.call('EXPIRE', ips_key, ttl)
redis.call('SADD', ip_sessions_key, session_id)
redis.call('EXPIRE', ip_sessions_key, ttl)
refresh_meta()
return 0
""";

        /// <summary>Lua script: release auth session and node registry (v2).</summary>
        internal const string AuthSessionReleaseV2 = """
-- AUTH_SESSION_RELEASE_V2
local sessions_key = KEYS[1]
local ips_key = KEYS[2]
local ip_sessions_key = KEYS[3]
local session_key = KEYS[4]
local meta_key = KEYS[5]
local node_set = KEYS[6]
local session_id = ARGV[1]
local ip = ARGV[2]
if redis.call('EXISTS', session_key) == 0 then
  redis.call('DEL', meta_key)
  redis.call('SREM', node_set, session_id)
  return 0
end
redis.call('DEL', session_key)
local current = tonumber(redis.call('GET', sessions_key) or '0')
if current > 0 then
  redis.call('DECR', sessions_key)
end
redis.call('SREM', ip_sessions_key, session_id)
local remaining = redis.call('SCARD', ip_sessions_key)
if remaining == 0 then
  redis.call('DEL', ip_sessions_key)
  redis.call('SREM', ips_key, ip)
end
redis.call('DEL', meta_key)
redis.call('SREM', node_set, session_id)
return 0
""";

        /// <summary>Lua script: refresh auth lease and node registry TTL (v2).</summary>
        internal const string SessionHeartbeatV2 = """
-- SESSION_HEARTBEAT_V2
local meta_key = KEYS[5]
local node_set = KEYS[6]
local ttl = tonumber(ARGV[1])
local now_ms = ARGV[2]
local meta_ttl = tonumber(ARGV[3])
redis.call('EXPIRE', KEYS[1], ttl)
redis.call('EXPIRE', KEYS[2], ttl)
redis.call('EXPIRE', KEYS[3], ttl)
redis.call('EXPIRE', KEYS[4], ttl)
if redis.call('EXISTS', meta_key) == 1 then
  redis.call('HSET', meta_key, 'leaseUpdated', now_ms)
  redis.call('EXPIRE', meta_key, meta_ttl)
  redis.call('EXPIRE', node_set, meta_ttl)
end
return 0
""";

        /// <summary>Lua script: acquire transit peer with node registry (v2).</summary>
        internal const string TransitPeerAcquireV2 = """
-- TRANSIT_PEER_ACQUIRE_V2
local sessions_key = KEYS[1]
local meta_key = KEYS[2]
local node_set = KEYS[3]
local now = tonumber(ARGV[1])
local lease = tonumber(ARGV[2])
local max = tonumber(ARGV[3])
local session_id = ARGV[4]
local node_name = ARGV[5]
local peer_id = ARGV[6]
local now_ms = ARGV[7]
local meta_ttl = tonumber(ARGV[8])
local cutoff = now - lease
local function refresh_meta()
  if redis.call('EXISTS', meta_key) == 0 then
    redis.call('HSET', meta_key, 'node', node_name, 'kind', 'transit', 'peerId', peer_id, 'created', now_ms, 'leaseUpdated', now_ms)
  else
    redis.call('HSET', meta_key, 'leaseUpdated', now_ms)
  end
  redis.call('SADD', node_set, session_id)
  redis.call('EXPIRE', meta_key, meta_ttl)
  redis.call('EXPIRE', node_set, meta_ttl)
end
redis.call('ZREMRANGEBYSCORE', sessions_key, '-inf', cutoff)
if redis.call('ZSCORE', sessions_key, session_id) then
  redis.call('ZADD', sessions_key, now, session_id)
  refresh_meta()
  return 0
end
if max > 0 and redis.call('ZCARD', sessions_key) >= max then
  return 1
end
redis.call('ZADD', sessions_key, now, session_id)
refresh_meta()
return 0
""";

        /// <summary>Lua script: release transit peer and node registry (v2).</summary>
        internal const string TransitPeerReleaseV2 = """
-- TRANSIT_PEER_RELEASE_V2
local sessions_key = KEYS[1]
local meta_key = KEYS[2]
local node_set = KEYS[3]
local session_id = ARGV[1]
redis.call('ZREM', sessions_key, session_id)
redis.call('DEL', meta_key)
redis.call('SREM', node_set, session_id)
return 0
""";

        /// <summary>Lua script: refresh transit peer and node registry (v2).</summary>
        internal const string TransitPeerRefreshV2 = """
-- TRANSIT_PEER_REFRESH_V2
local sessions_key = KEYS[1]
local meta_key = KEYS[2]
local node_set = KEYS[3]
local now = tonumber(ARGV[1])
local session_id = ARGV[2]
local now_ms = ARGV[3]
local meta_ttl = tonumber(ARGV[4])
if redis.call('ZSCORE', sessions_key, session_id) then
  redis.call('ZADD', sessions_key, now, session_id)
end
if redis.call('EXISTS', meta_key) == 1 then
  redis.call('HSET', meta_key, 'leaseUpdated', now_ms)
  redis.call('EXPIRE', meta_key, meta_ttl)
  redis.call('EXPIRE', node_set, meta_ttl)
end
return 0
""";

        // NODE_PURGE_CHUNK_V1: reserved for future server-side batched purge when orphan counts exceed ~100k.
    }
}
