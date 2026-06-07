# NNTP host configuration (`NntpServer` section)

NNRPD and NNTPD bind a single JSON section named `NntpServer` to **socket**, **session idle**, and **encryption** option types:

| Property | Subsystem | Purpose |
|----------|-----------|---------|
| `NodeName` | `Vector.NNTP.Encryption` | Stable node id for ACME/cluster logging (required). |
| `Port` | `Vector.NNTP.Sockets` | Cleartext NNTP listener (default `119`). |
| `TlsPort` | `Vector.NNTP.Sockets` | Implicit TLS (NNTPS) listener; `0` disables (default `0`). |
| `BindAddress` | Sockets | Bind address (`0.0.0.0` or `*` for all interfaces). |
| `IdleTimeout` | Sockets + Session | Per-read idle timeout (ISO 8601 duration). |
| `IdleTimeoutSeconds` | Sockets + Session | Optional idle timeout in seconds; **wins over `IdleTimeout`** when set. |
| `MaxConnections` | Sockets | Concurrent connection cap (`0` = unlimited). |
| `ServerIdentification` | Sockets | Banner and CAPABILITIES `IMPLEMENTATION` (defaults to host assembly name). |
| `EnableStartTls` | Sockets | Advertise and accept `STARTTLS` when a certificate is available. |
| `EnableCompressDeflate` | Sockets | Advertise `COMPRESS DEFLATE` (wire compression not yet implemented). |
| `RequireTlsForAuthInfo` | Sockets | Reject AUTHINFO/SASL until TLS is active. |
| `MaxArtSize` | Sockets | Maximum decoded dot-stuffed article body in bytes for POST/TAKETHIS/IHAVE (`0` disables; default `1048576`). Excess returns `439`/`441` without tearing down the session. |
| `PipeReadBufferBytes` | Sockets | `StreamPipeReader` buffer size for socket sessions (default `65536`, minimum `4096`). Larger values reduce per-article `ReadAsync` calls during streaming transfers. |

Distributed session admission, byte quota, and heartbeats use a separate **`Redis`** section (see [Session management](session-management.md)).

### Article body ingestion (POST, TAKETHIS, IHAVE)

`MaxArtSize` enforces the maximum **decoded** dot-stuffed article body size while reading from the session pipe. The default is **1 MiB** (`1048576`), matching typical `NNTPD.json` deployments. When a peer exceeds the limit, transit commands return **`439`** (TAKETHIS/IHAVE) or **`441`** (POST) and the session stays up; set **`0`** to disable the check. `PipeReadBufferBytes` sizes the socket `StreamPipeReader` buffer (default **65536**, minimum **4096**). Larger buffers reduce `ReadAsync` churn during RFC 4644 streaming at the cost of slightly more memory per connection.

## Transit peers (NNTPD)

**NNTPD only.** `TransitPeers` defines trusted upstream providers that may use RFC 4644 streaming (`CHECK`, `TAKETHIS`, `IHAVE`) **without AUTH**, with cluster-wide connection caps enforced in Redis.

| Key | Purpose |
|-----|---------|
| `RefreshIntervalMinutes` | DNS snapshot rebuild interval for hostname `AcceptFrom` entries (default `10`). |
| `Peers[].Name` | Peer name for logs, metrics labels, and Redis coordination keys (e.g. `Giganews`). |
| `Peers[].MaxConnections` | Cluster cap via Redis ZSET (`0` = unlimited, default `10`). |
| `Peers[].AcceptFrom` | Literal IP, CIDR, or DNS hostname entries. |

Startup **fails** if any two peers have overlapping address ranges. Hostname entries are resolved at startup and on each refresh; failed refresh keeps the previous snapshot.

Stale Redis ZSET members are purged using roughly **three heartbeat intervals** (not socket idle timeout), so crashed sessions release capacity within minutes. On startup the refresh service reconciles counts immediately. If peering stays at capacity after a restart, inspect `nntp.transitpeer.current_capacity` or flush the Redis key `{prefix}transitpeer:{peerId}:sessions`.

```json
"TransitPeers": {
  "RefreshIntervalMinutes": 10,
  "Peers": [
    {
      "Name": "Giganews",
      "MaxConnections": 10,
      "AcceptFrom": ["news-out.nntp.giganews.com"]
    }
  ]
}
```

OpenTelemetry: `nntp.transitpeer.*` (including `current_capacity` / `max_connections` per `peer` label).

## Example fragment (do not commit secrets)

```json
{
  "NntpServer": {
    "NodeName": "nnrpd01",
    "Port": 119,
    "TlsPort": 563,
    "BindAddress": "0.0.0.0",
    "ServerIdentification": "Vector.NNTP.NNRPD",
    "EnableStartTls": true,
    "RequireTlsForAuthInfo": false
  },
  "Redis": {
    "Hosts": ["redis01a.example.net", "redis01b.example.net"],
    "Port": 6379,
    "Retry": 3,
    "TimeoutSeconds": 3,
    "KeyPrefix": "nntp:session:",
    "HeartbeatIntervalSeconds": 60,
    "ReconciliationIntervalSeconds": 300
  },
  "LetsEncrypt": {
    "Enabled": true,
    "CertDir": "certs",
    "AccountKeyPem": "-----BEGIN EC PRIVATE KEY-----..."
  }
}
```

For local development without ACME, set `"LetsEncrypt": { "Enabled": false }` (a cached `certificate.pfx` under `CertDir` is still loaded for TLS). If `Enabled` is true but `AccountKeyPem` is omitted, the host reads `{CertDir}/letsencrypt.pem` when present; in Development only, incomplete ACME settings disable renewal instead of failing startup.

### Redis section

| Key | Purpose |
|-----|---------|
| `Hosts` | **Required.** One or more Redis hostnames or IPs (no URI scheme or port suffix). |
| `Port` | TCP port for all hosts (default `6379`). |
| `Retry` | StackExchange.Redis `ConnectRetry` (default `3`). |
| `TimeoutSeconds` | Connect and sync command timeout (default `3`). |
| `MinConnections` / `MaxConnections` | Multiplexer pool bounds (defaults `1` / `4`). |
| `KeyPrefix`, `HeartbeatIntervalSeconds`, … | Coordination tuning (optional; see [Session management](session-management.md)). |

At host startup the multiplexer pool opens at least `MinConnections` live connections; the background scaler may add entries up to `MaxConnections` under load.

Unit tests that do not call `AddNntpSessionRedis` keep in-memory coordinators from `AddNntpSessionCore` only.

## HistoryDb (NNTPD transit CHECK / TAKETHIS)

**NNTPD only.** `NntpServer:HistoryDb` configures transit history via `Vector.NNTP.HistoryDB` (memory → Redis Lua → RocksDB). See [historydb-rocksdb-schema.md](historydb-rocksdb-schema.md).

- **CHECK** is read-only: probes duplicates (`238` / `438`) without recording.
- **TAKETHIS** and **IHAVE** (before `335`) call `TryRecordAsync` (`SET NX`) before article storage.

| Key | Purpose |
|-----|---------|
| `DbDir` | RocksDB directory (required for transit). |
| `RememberDays` | Retention window for duplicate suppression. |
| `MemoryLimitBytes` | Hot in-memory cache budget (default 1 GiB). |
| `MemoryShardCount` | Digest-key shard count for parallel CHECK/TAKETHIS (power of two; default 64). Per-shard budget is `MemoryLimitBytes / MemoryShardCount`. |
| `QueueCapacity` | Bounded backfill queue after Redis record on accept. |
| `RebuildCheckpointInterval` | Redis `history:rebuild_state` checkpoint interval during rebuild. |
| `RebuildRedisBatchSize` | Pipeline batch size for Rocks→Redis rebuild. |
| `EnableMemoryPreloadOnStartup` | Reverse-iterate `by_expiration` into memory after rebuild. |
| `RocksDb` | Optional Rocks tuning overrides. `DigestBlockCacheBytes` (JSON alias `BlockCacheBytes`) and `ExpirationBlockCacheBytes` (default 8 MB) size independent per-CF LRU caches. `DigestBloomBitsPerKey` (default 10) enables BuiltinBloom on `by_digest`. `EnableStatistics` (default true) and `StatsDumpPeriodSec` (default 600) drive native periodic `DUMPING STATS` / `PERSISTING STATS` sections in `DbDir/LOG` on RocksDB 10.x. `MirrorStatsToHostLogger` (default false) optionally duplicates snapshots into the NNTPD host logger via `HistoryRocksStatsLogHostedService` (legacy workaround for 6.x). Startup logs include `rocksdb.version`. See [historydb-rocksdb-schema.md](historydb-rocksdb-schema.md) for upgrade/rollback and stats notes. |

**Operational notes:**

- On **every process start**, NNTPD runs a **full Rocks→Redis rebuild** before CHECK and record paths are operational (`503` until complete).
- At billions of keys, rebuild may take hours; target throughput tiers are documented in the ADR (10k / 50k / 100k keys/s).
- Plan **hundreds of GB** on `DbDir` at multi-billion entry scale (`estimatedBytes ≈ liveEntryCount × 81 × overhead`).
- **RocksDB writes are asynchronous:** only successful **TAKETHIS/IHAVE record** (`TryRecordAsync`) enqueues a digest for the background worker to `PutReservation` in Rocks. CHECK `238` does not write Redis or Rocks. Opening the DB or finishing rebuild may show **0 writes** until accept-path traffic is persisted; SST files appear after memtable flush/compaction.
- **Orphan keys:** if history records a message-id but storage rejects the article (`439`), the id stays in history until TTL (documented in the ADR).

Register `AddNntpHistoryDatabase` after `AddNntpSessionRedis` and before `AddNntpSocketsTransit`.

## TLS startup

- **Implicit TLS** (`TlsPort` > 0): handshakes use the current certificate from `CertificateRenewalService` (disk cache first, then ACME).
- **STARTTLS**: offered in `CAPABILITIES` only when `EnableStartTls` is true and a certificate is present.
- Connections accepted before a certificate is ready on the TLS port are closed with a debug log until renewal supplies a cert.
