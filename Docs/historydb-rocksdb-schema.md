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

Expose knobs under `HistoryDb:RocksDb` (per-CF block cache, Bloom filters, write buffer, background jobs). **Initial code defaults are starting points subject to benchmark validation** on deployment hardware (CHECK rate, sweep volume, retention, SSD, RAM). Do not treat any single ADR example size as immutable.

### Per-column-family block cache

| CF | Config key | Typical role |
|----|------------|--------------|
| `by_digest` | `DigestBlockCacheBytes` (JSON alias `BlockCacheBytes`) | Hot point lookups during persist and compaction |
| `by_expiration` | `ExpirationBlockCacheBytes` (default 8 MB) | Sweep and rebuild iterators; smaller working set |

Digest and expiration caches are **independent LRU instances**. Tuning them separately avoids giving iterator-heavy maintenance work the same RAM budget as digest negative lookups.

### Bloom filters

| CF | Config key | Default |
|----|------------|---------|
| `by_digest` | `DigestBloomBitsPerKey` | 10 (BuiltinBloom, whole-key) |
| `by_expiration` | `ExpirationBloomBitsPerKey` | 0 (disabled) |

CHECK does not consult RocksDB; Bloom accelerates cold-path `by_digest` existence checks and compaction.

### Native library upgrade (RocksDB 10.x)

HistoryDB uses the unified **RocksDB** NuGet package (curiosity-ai, tracks upstream 10.x). RocksDB 10.x is expected to **open databases written by the prior 6.2.2 bindings** without data loss.

**Before production upgrade:**

1. Back up `DbDir` (SSTs + WAL).
2. Open the copy with the new host build; confirm startup log reports RocksDB 10.x (`rocksdb.version`), Bloom on `by_digest`, and per-CF cache capacities.
3. Verify sweep still deletes expired pairs (`num_deletions` in native LOG).

**Rollback:** Downgrading the native library after opening with a newer RocksDB version may be unsafe. Restore from the pre-upgrade backup rather than downgrading in place.

### Statistics and LOG dumps

With `EnableStatistics: true` and `StatsDumpPeriodSec` (default 600), RocksDB **10.4.x** reliably emits periodic statistics to **`DbDir/LOG`**:

```
------- DUMPING STATS -------
...
------- PERSISTING STATS -------
```

The prior **RocksDbSharp 6.2.2** runtime did not consistently produce these periodic dumps even when `stats_dump_period_sec` was set.

| Sink | Mechanism | Default on 10.x |
|------|-----------|-----------------|
| `DbDir/LOG` | Native `stats_dump_period_sec` | **On** when `EnableStatistics` + non-zero period |
| NNTPD host logger | `HistoryRocksStatsLogHostedService` | **Off** (`MirrorStatsToHostLogger: false`) |

Enable `MirrorStatsToHostLogger` only when operators want duplicate `rocksdb.stats` / ticker snapshots in the centralized host log pipeline instead of tailing `DbDir/LOG`.

## Non-functional: memory-hit zero allocations

**Duplicate → memory hit → return must incur zero heap allocations** on `CheckAsync` and callees on that branch. Enforced by build-blocking test.

## CHECK tier observability

CHECK path is **memory → Redis** (no Bloom tier in this release). Export tier counters before adding complexity.

| Instrument | When emitted |
|------------|--------------|
| `history.check.total` | Terminal Duplicate or Wanted only (successfully processed CHECK) |
| `history.check.memory_hit` | Memory `TryGetDuplicate` true |
| `history.check.memory_miss` | Memory miss before Redis |
| `history.check.redis_probe` | Every `CheckRedisProbeAsync` |
| `history.check.redis_duplicate` | Redis Lua returns duplicate |
| `history.check.redis_wanted` | Redis Lua returns wanted |
| `history.check.redis_ms` | Redis Lua latency histogram |

**Excluded from `history.check.total`:** not operational, malformed message-id, Redis failure/timeout, cancellation before terminal outcome.

**Dashboard rates:**

| Rate | Formula |
|------|---------|
| `memory_hit_rate` | `history.check.memory_hit / history.check.total` |
| `redis_probe_rate` | `history.check.redis_probe / history.check.total` |
| `redis_duplicate_rate` | `history.check.redis_duplicate / history.check.redis_probe` |

**Invariant:** `history.check.total` = memory hits + completed Redis probes = `history.check.redis_duplicate` + `history.check.redis_wanted`.

### Deferred: CHECK Bloom tier

Not implemented in this release. Gather ~1 week of tier metrics first. Revisit only if Redis probe rate, `history.check.redis_ms` tail, or memory hit rate trends justify a non-counting Bloom between memory and Redis. Bloom positives always confirm via Redis before 438; negatives skip Redis only when Bloom completeness is maintained on record/preload. No counting Bloom or `Bloom.Remove` on memory eviction.

## Metrics (maintenance and record)

- `history.rocks.sweep.deleted`, `history.rocks.sweep.duration_ms`
- `history.rocks.persist.total`, `history.rocks.persist_failures`
- `history.rebuild.duration_ms`, `history.rebuild.batch.duration_ms`, `history.rebuild.keys_processed` (gauge)
- `history.memory.entries`, `history.memory.bytes`, `history.memory.evictions`
- `history.operational`, `history.queue.depth`, `history.queue.dropped`
- `history.record.recorded`, `history.record.duplicate`, `history.record.redis_ms`
- `history.generation.io_errors`, `history.redis.slow_calls`

## Schema version

`SchemaVersion` in Rocks property / `history:meta` — mismatch triggers rebuild.
