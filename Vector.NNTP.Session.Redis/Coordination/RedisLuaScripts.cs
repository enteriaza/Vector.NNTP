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
    }
}
