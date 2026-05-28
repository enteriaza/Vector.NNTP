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
```

`idleTimeoutSeconds` under `NntpServer` wins over ISO `IdleTimeout` when both are set. Heartbeat interval defaults from `Redis:HeartbeatIntervalSeconds`.

## Redis connectivity

Production hosts bind the top-level `Redis` section (`Hosts`, `Port`, `Retry`, `TimeoutSeconds`, optional `MinConnections` / `MaxConnections`). A multiplexer pool starts with at least one live connection and may scale up under load (same pattern as MessageBus). Each pool entry connects with all configured hosts as StackExchange.Redis endpoints for failover.

## Byte quota

Byte-limited accounts decrement cluster-wide quota in Redis after each command. When remaining ≤ 0, the session is deauthorized and subsequent commands receive **480 Authentication required**.

## Observability

Structured `EventName` values include `SessionAdmissionDenied`, `AccountRateRebalanced`, `QuotaExceeded`, `ForcedDeauth`, `RedisReconciliationCompleted`, and `RedisOperationSlow`. Filter logs by `AccountKey` (BLAKE3 hex), not raw usernames, for Redis-heavy investigations.
