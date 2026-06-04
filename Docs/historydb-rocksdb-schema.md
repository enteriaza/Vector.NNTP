# HistoryDB RocksDB schema (ADR)

## Status

Accepted — v1 schema for transit CHECK deduplication (`Vector.NNTP.HistoryDB`).

## Context

Transit duplicate filtering must sustain high CHECK rates (500–5000+ CHECK/s). **CHECK** is read-only (memory → Redis `GET` Lua, no writes). **TAKETHIS** and **IHAVE accept** atomically record via Redis `SET NX` (memory → record Lua). RocksDB is durable storage and the source for **full Rocks→Redis rebuild on every NNTPD process start**.

## Decision: dual column family (digest + expiration index)

### Rejected: digest-only layout

| Problem | Impact |
|---------|--------|
| Newest-first memory preload | Digest key order is random in time |
| Window filters without index | Full table scan at billions of keys |
| Expired delete | O(entire DB) scan |

### Chosen layout

| CF | Key | Value |
|----|-----|-------|
| `by_digest` | 32-byte BLAKE3 digest | 8-byte `expirationEpochSeconds` (LE) |
| `by_expiration` | `[expirationEpoch:8 BE][digest:32]` (40 bytes) | 1-byte tombstone |

**Invariant:** For every live `by_digest` key there is exactly one `by_expiration` key (same digest; expiration in digest value matches BE prefix in exp key).

All keys use `HistoryRocksKeyEncoding` — no manual endian manipulation elsewhere.

## Sweep

Forward-iterate `by_expiration` from smallest key while `expirationEpoch <= now`. Batch-delete paired `by_expiration` + `by_digest` entries. Cost is O(expired keys in batch), not O(live keys).

## Startup: full Rocks→Redis rebuild every process start

1. Open Rocks + Redis.
2. **503** on CHECK and TAKETHIS/IHAVE record until rebuild completes.
3. Resumable via `{prefix}history:rebuild_state` (crash mid-rebuild resumes; new process start runs fresh full rebuild).
4. Pipeline `SET` from `by_expiration` forward (unexpired keys only).
5. Optional memory preload: reverse `by_expiration` until `MemoryLimitBytes`.

### Rebuild throughput reference (anti-regression)

| Tier | Keys/sec |
|------|----------|
| Minimum acceptable | 10,000 |
| Target | 50,000+ |
| Excellent | 100,000+ |

**Forbidden:** per-key `await StringSetAsync` in a loop instead of batched/pipelined writes (~100× regression).

## Deliberate durability relaxation

```
CHECK → Redis probe (HISTORY_CHECK_V1, read-only) → 238 / 438
TAKETHIS / IHAVE accept → Redis record (HISTORY_RECORD_V1) → queue TryWrite → Rocks WriteBatch
```

If the process crashes after Redis record but before Rocks persist: duplicate suppression survives until **Redis TTL** expires; then the message-id may be accepted again. This is the only intentionally relaxed durability path (same class as queue-full after a successful record).

**Orphan history:** If `TryRecordAsync` succeeds but article storage rejects the body (`439`), the message-id **remains** recorded (no rollback). Operators accept transient “history without article” until TTL expires.

**CHECK vs record tradeoff:** Multiple peers may receive `238` for the same message-id until the first TAKETHIS/IHAVE records it.

## Storage growth (operators)

Rough logical bytes per live entry: ~81 B (40 B `by_digest` + 41 B `by_expiration`) before Rocks overhead.

`estimatedBytes ≈ liveEntryCount × 81 × rocksOverheadFactor` (use 1.3–2.0 until measured).

At ~2.5B entries plan for **hundreds of GB** on `DbDir`.

## RocksDB tuning

Expose knobs under `HistoryDb:RocksDb` (block cache, write buffer, compression, background jobs). **Initial code defaults are starting points subject to benchmark validation** on deployment hardware (CHECK rate, sweep volume, retention, SSD, RAM). Do not treat any single ADR example size as immutable.

## Non-functional: memory-hit zero allocations

**Duplicate → memory hit → return must incur zero heap allocations** on `CheckAsync` and callees on that branch. Enforced by build-blocking test.

## Metrics

- `history.rocks.sweep.keys_deleted`, `history.rocks.sweep.duration_ms`
- `history.rocks.by_digest.count`, `history.rocks.by_expiration.count`
- `history.rebuild.keys_per_second`, `history.rebuild.batch.duration_ms`
- `history.memory.entries`, `history.memory.bytes`, `history.memory.hit_rate`
- `history.check.memory_hit`, `history.check.memory_miss`
- `history.record.recorded`, `history.record.duplicate`, `history.record.redis_ms`

## Schema version

`SchemaVersion` in Rocks property / `history:meta` — mismatch triggers rebuild.
