# Session management

Vector NNTP hosts (NNRPD and NNTPD) share the `Vector.NNTP.Session` and `Vector.NNTP.Session.Redis` libraries for connection lifecycle, distributed admission, fair-share rate limits, and byte quotas.

## Account types

| MySQL `account_type` | Meaning | Active limit |
|----------------------|---------|--------------|
| `R` | Rate-limited | `account_rate_limit` (decimal SI **Mbps**) |
| `B` | Byte-limited | `account_byte_limit` (cluster-wide bytes) |

`R` and `B` denote **rate** vs **byte** billing — not reader vs both.

## Mbps (decimal SI)

`account_rate_limit` is **megabits per second (Mbps)** using decimal SI: 1 Mbps = 1,000,000 bit/s.

```
rateBytesPerSecond = account_rate_limit * 1_000_000 / 8
```

Examples: 10 Mbps → 1,250,000 B/s; 100 Mbps → 12,500,000 B/s. **Mibps is not used.**

## Shared account bandwidth

For rate-limited accounts, `account_rate_limit` is an **aggregate account ceiling** shared across all authenticated TCP sessions cluster-wide. The system never multiplies bandwidth by session count.

Fair share per session:

```
perSessionBudget = accountRateBytesPerSecond / activeAuthenticatedSessionCount
```

## Active authenticated session

Counts every **live authenticated TCP** (idle connections included). Does not count unauthenticated connections or `Authenticating` handshakes.

## Eventual consistency

Session counts and fair-share caps are **eventually consistent** across nodes. Short drift during churn or Redis lag is acceptable; sustained aggregate amplification is not.

## Fair-share stability

Per-session caps refresh on a timer (default **2 s**, `NntpRateAllocationOptions.RateAllocationRefreshInterval`). Session-count reads use a short cache (~100 ms) to reduce Redis and shaper churn.

## Distributed admission

After credential proof, `NntpAuthenticationService` calls `INntpSessionCoordinator.TryAdmitAsync` before marking the connection authenticated.

| Result | NNTP response |
|--------|----------------|
| Success | `281` / `235` |
| Max sessions | `481 Too many sessions` |
| IP limit | `481 Too many source addresses` |
| Redis fault | `503 Temporary authentication failure` |

**502** is not used for quota exhaustion.

## Connection vs Redis slot

- **Connection session** (`ISessionDatabase`): created on TCP accept, removed on disconnect.
- **Redis slot**: acquired only after successful auth + admission; released on teardown.

## Redis leases

TTL on acquire/heartbeat:

```
ttlSeconds = max(300, ceil(idleTimeoutSeconds * 2))
metadataTtlSeconds = ttlSeconds * 2
```

`IdleTimeoutSeconds` under `NntpServer` drives socket idle enforcement and Redis lease TTL sizing. Heartbeat interval defaults from `Redis:HeartbeatIntervalSeconds`.

**Liveness:** Redis `EXPIRE` on `session:{sessionId}` and `node:{NodeName}:sessions` is authoritative for orphan cleanup. The HASH field `leaseUpdated` (Unix milliseconds) is informational only for support and logs — do not implement application-side staleness decisions from it.

## Node-scoped registry

Each host sets stable `NntpServer:NodeName` (required). On TCP accept, `SessionContext.NodeName` and `NntpConnectionContext.NodeName` record that identity.

| Key | Purpose |
|-----|---------|
| `{prefix}session:{sessionId}` | HASH: `node`, `kind` (`auth` \| `transit`), `accountKey`, `clientIp`, `peerId`, `created`, `leaseUpdated` (ms) |
| `{prefix}node:{nodeName}:sessions` | SET of session ids owned by this node |

Acquire and refresh Lua scripts atomically update coordination keys, the HASH, `SADD` the node index, and refresh both TTLs. Release deletes coordination state, the HASH, and `SREM` from the node set.

**Startup:** `NodeSessionLifecycleHostedService` runs `PurgeNode` before heartbeat and socket listeners (informational log: auth/transit counts and duration).

**Shutdown:** Socket accept stops and in-flight sessions drain; survivors are released from `ISessionDatabase.SnapshotAll()`, then `PurgeNode` runs again.

Renaming `NodeName` orphans keys under the old `node:{oldName}:*` prefix until manual Redis cleanup.

## Redis connectivity

Production hosts bind the top-level `Redis` section (`Hosts`, `Port`, `Retry`, `TimeoutSeconds`, optional `MinConnections` / `MaxConnections`). A multiplexer pool starts with at least one live connection and may scale up under load (same pattern as MessageBus). Each pool entry connects with all configured hosts as StackExchange.Redis endpoints for failover.

## Byte quota

Byte-limited accounts decrement cluster-wide quota in Redis after each command. When remaining ≤ 0, the session is deauthorized and subsequent commands receive **480 Authentication required**.

## Observability

Structured `EventName` values include `SessionAdmissionDenied`, `AccountRateRebalanced`, `QuotaExceeded`, `ForcedDeauth`, `RedisReconciliationCompleted`, and `RedisOperationSlow`. Filter logs by `AccountKey` (BLAKE3 hex), not raw usernames, for Redis-heavy investigations.
