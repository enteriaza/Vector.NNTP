# MessageBus connection pool design

Design sign-off document for the Vector.NNTP `MessageBus` library (Phase 4a).

## Workload profile (NNRPD targets)

| Dimension | Initial target | Notes |
|-----------|----------------|-------|
| Publisher throughput | 5,000 publishes/sec peak | Article RPC bursts |
| Consumer count | 64 active subscriptions | Long-lived channels |
| Confirm rate | Matches publish rate | Publisher confirmation tracking enabled |
| RPC pattern | 64 KiB avg payload, 500 ms p99 SLO | Per RPC transaction scope |
| Network | LAN, TLS optional | `EnableSsl` from host config |
| Pool sizing | `MinConnections=1`, `MaxConnections=4`, `ChannelPoolSize=2048` | [NNRPD/NNRPD.json](../NNRPD/NNRPD.json) |

## IPublisherScope semantics (locked)

- **One scope == one AMQP channel == one publisher-confirm lifecycle**
- **One logical RPC transaction → one `IPublisherScope`**
- **Not thread-safe** — serialize `PublishAsync` within a scope
- **Dispose** after all publishes are awaited; confirms complete per publish via RabbitMQ.Client 7 `BasicPublishAsync` with confirmation tracking
- **At-least-once** — callers retry with exponential backoff + full jitter; MessageBus does not retry publishes

## Delivery guarantee

**At-least-once** for publisher and consumer. Duplicates possible after reconnect, confirm timeout, or consumer redelivery. Upstream must dedupe or tolerate duplicates.

## Exception taxonomy

| Type | Retry? |
|------|--------|
| `MessageBusUnavailableException` | Backoff + jitter |
| `MessageBusLeaseTimeoutException` | Backoff + jitter |
| `MessageBusPublishConfirmTimeoutException` | Backoff + jitter (duplicate risk) |
| `MessageBusConnectionFaultException` | Backoff + jitter |
| `MessageBusConfigurationException` | Fix config (fail fast) |

## Health SLO

| Status | Condition |
|--------|-----------|
| Healthy | Faulted connection fraction below `DegradedThreshold` |
| Degraded | Between `DegradedThreshold` and `UnhealthyThreshold` |
| Recovering | Pool starting or replacing TCP |
| Unhealthy | At or above `UnhealthyThreshold` |

## Critical invariants

See plan checklist: non-negative slots, single scope completion, blocked ≠ faulted until stalled, FIFO waiters, TCP-only `ConnectionPool`, no JSON reads in library.

## Flow-control quarantine

`RabbitMqPoolFlowControlMonitor` (hosted service) scans the pool on an interval derived from `ConnectionBlockedTimeout` (clamped to 1–30 seconds):

| State | Meaning | New slots? |
|-------|---------|------------|
| **Blocked** | Broker `connection.blocked` (memory/disk alarm) | No |
| **Stalled** | Blocked longer than `ConnectionBlockedTimeout` | No (quarantined, not faulted) |
| **Faulted** | TCP/session failure; removed from snapshot | No |

Unblocking clears stalled automatically. Quarantine signals slot waiters so traffic routes to other TCP connections.

## Phase 4a benchmark results (CreateChannel contention)

**Harness:** [MessageBus.Benchmarks](../MessageBus.Benchmarks/) — single TCP, publisher confirms enabled, parallel `CreateChannelAsync` + close per wave.

**Run:** 2026-05-26 (post pool refactor) on `CHRIS-PC` against production hostnames from [NNRPD/NNRPD.json](../NNRPD/NNRPD.json).

**Pass criteria (draft):** per-wave **p99 CreateChannel ≤ 500 ms** (RPC SLO proxy); overall cell **Pass** only if worst p99 meets SLO and no collapse (p99 > 4× SLO at concurrency ≥ 256).

### Topology matrix

| Topology | Status | Notes |
|----------|--------|-------|
| Single node (`rabbit01a`) | **Fail** | [message-bus-benchmark-single.json](message-bus-benchmark-single.json) |
| 3-node HA (`rabbit01a/b/c`) | **Fail** | [message-bus-benchmark-ha.json](message-bus-benchmark-ha.json) |
| TLS (`EnableSsl`, port 5671) | **Blocked** | `BrokerUnreachableException` (socket timeout on 5671, 2026-05-26 re-run) — [message-bus-benchmark-tls.json](message-bus-benchmark-tls.json); re-run when AMQPS is reachable |
| Quorum queues + confirms | **Not run** | Requires dedicated queue topology on broker |
| Simulated WAN RTT | **Not run** | Use tc/netem before sign-off |

### HA cluster — concurrency sweep (one TCP)

Connect time: **656 ms**. SLO: **500 ms** p99 per wave.

| Concurrency | p50 (ms) | p99 (ms) | max (ms) | ch/s | Result |
|-------------|----------|----------|----------|------|--------|
| 1 | 380 | 380 | 380 | 2 | Pass |
| 8 | 376 | 377 | 377 | 14 | Pass |
| 64 | 565 | 565 | 565 | 85 | **Fail** |
| 256 | 566 | 566 | 566 | 338 | **Fail** |
| 1024 | 563 | 593 | 600 | 1291 | **Fail** |
| 2048 | 657 | 823 | 828 | 1992 | **Fail** |

### Single node (`rabbit01a`) — concurrency sweep

Connect time: **656 ms**. Same SLO.

| Concurrency | p50 (ms) | p99 (ms) | max (ms) | ch/s | Result |
|-------------|----------|----------|----------|------|--------|
| 1 | 366 | 366 | 366 | 2 | Pass |
| 8 | 363 | 363 | 363 | 15 | Pass |
| 64 | 544 | 544 | 544 | 88 | **Fail** |
| 256 | 544 | 546 | 546 | 351 | **Fail** |
| 1024 | 543 | 559 | 562 | 1358 | **Fail** |
| 2048 | 648 | 807 | 814 | 2032 | **Fail** |

### Interpretation

1. **Baseline channel-open cost is high (~366–380 ms p99)** even at concurrency 1–8 — budget most of the 500 ms RPC SLO before publish work begins.
2. **Contention step at concurrency ≥ 64** — p99 jumps to ~544–565 ms (HA and single node); at 2048 tail reaches **807–823 ms p99**.
3. **Throughput scales** (~2000 channel opens/sec at 2048) but **latency SLO fails** for ephemeral per-scope channels under NNRPD-style concurrency.
4. **Post-refactor re-run (2026-05-26):** results are materially unchanged — pool hot-path improvements do not change broker `channel.open` RPC cost.
5. **Phase 4a sign-off:** ephemeral `IPublisherScope` model remains **not approved** at `ChannelPoolSize=2048` without mitigation — consider multi-TCP pool scaling, channel reuse window, or revised RPC SLO allocation for open+confirm.

### Re-run benchmarks

```bash
cd MessageBus.Benchmarks
dotnet run -c Release -- ../NNRPD/NNRPD.json --topology ha --output ../Docs/message-bus-benchmark-ha.json
dotnet run -c Release -- ../NNRPD/NNRPD.json --topology single --output ../Docs/message-bus-benchmark-single.json
dotnet run -c Release -- ../NNRPD/NNRPD.json --topology tls --output ../Docs/message-bus-benchmark-tls.json
```

## Host configuration ([NNRPD/NNRPD.json](../NNRPD/NNRPD.json))

Pool properties bound by the host (MessageBus does not read JSON):

| Property | NNRPD value | Purpose |
|----------|-------------|---------|
| `MinConnections` / `MaxConnections` | 1 / 4 | TCP pool bounds |
| `ChannelPoolSize` | 2048 | Max concurrent publisher scopes per TCP |
| `RequestedChannelMax` | 2048 | AMQP negotiated ceiling |
| `ChannelLeaseTimeout` | 30s | Max wait for publisher slot |
| `MaxPendingLeaseWaiters` | 4096 | Backpressure |
| `PublishConfirmTimeout` | 30s | Per-scope / shutdown confirm bound |
| `MaximumShutdownDrainTimeout` | 2m | Host stop drain cap |
| `ConnectionBlockedTimeout` | 60s | Prolonged `blocked` → stalled |
| `MinimumConnectionLifetime` | 5m | Scale-down guard |
| `ScaleDownCooldown` | 2m | Scale-down hysteresis |
| `ConnectionScaleDownIdleSeconds` | 600 | Idle scale-down |
| `PoolReconnectBaseDelayMs` / `PoolReconnectMaxDelayMs` | 1000 / 30000 | Full-jitter reconnect |
| `DegradedThreshold` / `UnhealthyThreshold` | 0.25 / 0.75 | Health SLO |
| `MaxConsecutiveRecoveryFailures` | 0 | Disabled (pool owns recovery) |

## Shutdown ordering

1. Stop new publisher slots / consumer registrations
2. Drain FIFO slot waiters
3. Mark connections `Draining`
4. Close consumer subscriptions
5. Flush publisher scopes (per-publish confirms in v7)
6. Pool drain capped by `MaximumShutdownDrainTimeout`
7. Close TCP connections
8. Dispose pool

## Configuration boundary

Hosts (NNTPD/NNRPD) bind `RabbitMQ` from JSON. MessageBus consumes `IOptions<RabbitMQOptions>` only — no `IConfiguration` in `AddMessageBus`.

## Automated tests

[MessageBus.Tests](../MessageBus.Tests/) covers:

- `MaxPendingLeaseWaiters` backpressure (`MessageBusUnavailableException`)
- Blocked connections excluded from slot acquisition (`MessageBusLeaseTimeoutException`)
- `EnforceBlockedQuarantine` / `RabbitMqPoolFlowControlMonitor` stalled quarantine
- `PooledConnection` blocked and stalled slot rules
