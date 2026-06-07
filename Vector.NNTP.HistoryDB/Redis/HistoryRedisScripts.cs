// <copyright file="HistoryRedisScripts.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.HistoryDB.Redis
{
    /// <summary>
    /// Embedded Lua scripts for history CHECK probe, record, and coordination.
    /// </summary>
    internal static class HistoryRedisScripts
    {
        /// <summary>
        /// Read-only CHECK probe: 0 wanted, 1 duplicate.
        /// </summary>
        internal const string HistoryCheckV1 = """
-- HISTORY_CHECK_V1
local v = redis.call('GET', KEYS[1])
if v and tonumber(v) > tonumber(ARGV[1]) then
  return 1
end
return 0
""";

        /// <summary>
        /// Atomic record on accept: 0 recorded, 1 duplicate, 2 error (reserved).
        /// </summary>
        internal const string HistoryRecordV1 = """
-- HISTORY_RECORD_V1
local v = redis.call('GET', KEYS[1])
if v then
  if tonumber(v) > tonumber(ARGV[1]) then
    return 1
  end
  redis.call('DEL', KEYS[1])
end
if redis.call('SET', KEYS[1], ARGV[2], 'NX', 'EX', tonumber(ARGV[3])) then
  return 0
end
local v2 = redis.call('GET', KEYS[1])
if v2 and tonumber(v2) > tonumber(ARGV[1]) then
  return 1
end
return 0
""";

        /// <summary>
        /// Release on spool failure: 0 released, 1 not found.
        /// </summary>
        internal const string HistoryReleaseV1 = """
-- HISTORY_RELEASE_V1
if redis.call('DEL', KEYS[1]) == 1 then
  return 0
end
return 1
""";
    }
}
