# NNTP host configuration (`NntpServer` section)

NNRPD and NNTPD bind a single JSON section named `NntpServer` to **socket**, **session idle**, and **encryption** option types:

| Property | Subsystem | Purpose |
|----------|-----------|---------|
| `NodeName` | `Vector.NNTP.Encryption` | Stable node id for ACME/cluster logging (required). |
| `DomainName` | Sockets | DNS domain suffix combined with `NodeName` for SpamAssassin scan header synthesis (for example `usenetninja.net` → `transit1.usenetninja.net`). Optional; when empty, `NodeName` alone is used. |
| `Port` | `Vector.NNTP.Sockets` | Cleartext NNTP listener (default `119`). |
| `TlsPort` | `Vector.NNTP.Sockets` | Implicit TLS (NNTPS) listener; `0` disables (default `0`). |
| `BindAddress` | Sockets | IPv4 bind address (`0.0.0.0` or `*` for all IPv4 interfaces). |
| `BindAddress6` | Sockets | IPv6 bind address; empty disables the IPv6 listener; `*` or `::` for all IPv6 interfaces. When set, a separate listener is started on this address for each configured port. |
| `IdleTimeoutSeconds` | Sockets + Session | Per-read idle timeout in seconds (default `600`). |
| `MaxConnections` | Sockets | Concurrent connection cap (`0` = unlimited). |
| `ServerIdentification` | Sockets | Banner and CAPABILITIES `IMPLEMENTATION` (defaults to host assembly name). |
| `EnableStartTls` | Sockets | Advertise and accept `STARTTLS` when a certificate is available. |
| `EnableCompressDeflate` | Sockets | Advertise `COMPRESS DEFLATE` (wire compression not yet implemented). |
| `RequireTlsForAuthInfo` | Sockets | Reject AUTHINFO/SASL until TLS is active. |
| `MaxArtSize` | Sockets | Maximum decoded dot-stuffed article body in bytes for POST/TAKETHIS/IHAVE (`0` disables; default `1048576`). Excess returns `439`/`441` without tearing down the session. |
| `PipeReadBufferBytes` | Sockets | `StreamPipeReader` buffer size for socket sessions (default `65536`, minimum `4096`). Larger values reduce per-article `ReadAsync` calls during streaming transfers. |
| `CpuRejectEnabled` | Sockets | Master switch for CPU overload connection rejection (default `true`). |
| `CpuRejectThresholdPercent` | Sockets | Effective CPU EWMA percent at or above which the gate enters rejecting (default `80`). |
| `CpuResumeThresholdPercent` | Sockets | Effective CPU EWMA percent at or below which accepting resumes; must be less than reject threshold (default `75`). |
| `CpuSamplingIntervalSeconds` | Sockets | Background CPU sample period in seconds (default `1`). |
| `CpuRejectUseProcess` | Sockets | Include process CPU vs logical processors in the gate (default `true`). |
| `CpuRejectUseHost` | Sockets | Include host-wide CPU on Linux (`/proc/stat`) or Windows (`GetSystemTimes`) (default `true`). |
| `CpuRejectUseCgroup` | Sockets | Include Linux cgroup quota-relative CPU when a finite quota exists (default `true`). |

When the hysteresis gate is **rejecting**, the server responds with `400 Service temporarily unavailable` and immediately closes the TCP connection ([RFC 3977](https://datatracker.ietf.org/doc/html/rfc3977) §5.1.1 at accept, §3.2.1 on the next command). Gating is **best-effort** (lock-free `Volatile` reads on the accept and dispatch hot paths).

OpenTelemetry: `nntp.server.cpu_utilization_ewma_percent` (effective), `nntp.server.cpu_utilization_ewma_percent_{process,host,cgroup}`, `nntp.server.cpu_gate_state`, threshold gauges, and `nntp.server.connections_rejected_cpu_{accept,command}_total`.

Distributed session admission, byte quota, and heartbeats use a separate **`Redis`** section (see [Session management](session-management.md)).

## Logging

Serilog rolling file sinks read the top-level **`Logging`** section from `NNTPD.json` / `NNRPD.json`:

| Key | Purpose |
|-----|---------|
| `Logging.LogDir` | Directory for Serilog rolling files (`NNTPD-.log` / `NNRPD-.log`). Optional; defaults to `{AppContext.BaseDirectory}/logs`. Relative paths resolve under `AppContext.BaseDirectory`. |

```json
"Logging": {
  "LogDir": "c:\\logs\\nntpd"
}
```

Console output and log levels continue to follow the `Serilog` configuration section when present.

### INN `news` log (NNTPD transit spool)

When `AddNntpArticlesTransitSpool(configuration)` is registered, Vector.NNTP.Articles writes an additional Serilog file at **`{Logging.LogDir}/news-{yyyyMMdd}.log`** (daily rolling via the `news-.log` path template, same retention/flush parameters as `NNTPD-.log`, **file sink only** — no console). Each line is an INN-style accept/reject entry emitted at **event time** (`DateTimeOffset.Now` when logged):

| Code | Meaning |
|------|---------|
| `+` | Article committed to spool after successful `AtomicWriteAsync` (not on wire `239`/`235`). |
| `-` | Rejected in the Articles pipeline (preprocess, postprocess, enqueue size/queue, write failure) with reason. |
| `c` | Cancel control message committed; second line after `+` for the same Message-ID. |
| `j` | Reserved for future accepted-to-junk-newsgroup filing (formatter/API present; not emitted in v1). |

Examples:

```
Jun 07 21:55:01.102 + Giganews <text@example.com> 842 ?
Jun 07 21:55:02.001 c Giganews <cancel.4066@foo.com> Cancelling <m070725@foo.com>
Jun 07 21:55:10.500 - Giganews <binary@example.com> yEnc section CRC validation failed.
```

Feed resolution order: `local` (reader POST) → transit peer name → first `Path:` hop before `!` (skipping `not-for-mail`) → peer hostname → `?`. Downstream sites on `+`/`j` lines are `?` until newsfeeds routing exists. Write failures log **exception type name only** in `news`; full detail remains in `NNTPD-.log`.

Every minute, the host application log (`NNTPD-.log`) also receives **single-line spool throughput summaries** (not written to `news-{date}.log`): one global line plus one line per active feed, for example `Spool throughput (60s): processed=18452/min accepted=18213 rejected=239 header=91 crc=12 crosspost=136 other=0` and `Spool throughput (60s) feed=Giganews: ...`. Rejection buckets are **header**, **crc**, **crosspost**, and **other** (spam, size, queue full, write failures). OpenTelemetry counters `nntp.spool.article.accepted` and `nntp.spool.article.rejected` expose the same outcomes for external dashboards.

### Article body ingestion (POST, TAKETHIS, IHAVE)

`MaxArtSize` enforces the maximum **decoded** dot-stuffed article body size while reading from the session pipe. The default is **1 MiB** (`1048576`), matching typical `NNTPD.json` deployments. When a peer exceeds the limit, transit commands return **`439`** (TAKETHIS/IHAVE) or **`441`** (POST) and the session stays up; set **`0`** to disable the check. `PipeReadBufferBytes` sizes the socket `StreamPipeReader` buffer (default **65536**, minimum **4096**). Larger buffers reduce `ReadAsync` churn during RFC 4644 streaming at the cost of slightly more memory per connection.

### Transit spool (NNTPD)

**NNTPD only.** Accepted TAKETHIS/IHAVE articles are enqueued in memory and written asynchronously to `{SpoolDir}/Incoming/{aa}/{bb}/{blake3-hex}`. Socket threads never block on disk I/O or header validation.

| Key | Purpose |
|-----|---------|
| `SpoolDir` | Spool root directory. Empty → `{AppContext.BaseDirectory}/Spool`. |
| `SpoolQueueCapacity` | Maximum in-flight queued articles (default `1024`). |
| `MaxQueuedBytes` | Maximum sum of queued article payload bytes (default `1073741824` / 1 GiB). |
| `PathAppend` | Hop token prepended to `Path:` during writer preprocessing (empty skips mutation). |

Enqueue rejects with **`437 Article rejected`** (IHAVE) or **`439 Transfer failed`** (TAKETHIS) when **either** limit is exceeded. Writer count scales automatically from absolute queue depth in fixed tiers (`ProcessorQueueSpoolWriterScalingPolicy.BacklogPerWriter`, default compile-time constant `64`) up to `min(ProcessorCount, 24)`; `SpoolQueueCapacity` is a safety/memory limit and does not change scaling aggressiveness. There is no fixed writer-count JSON knob — tune the backlog tier constant with host benchmarks if queue depth or throughput warrants it.

#### Memory warning

Each queued item holds a full `byte[]` copy of the article. Worst-case RAM is approximately **`min(SpoolQueueCapacity × MaxArtSize, MaxQueuedBytes)`** plus object overhead — not merely the item count.

| SpoolQueueCapacity | MaxQueuedBytes | MaxArtSize | Effective binding |
|--------------------|----------------|------------|-------------------|
| 1024 | 1 GiB | 4 MiB | Bytes (~256 max-sized articles) |
| 1024 | 4 GiB | 4 MiB | Item count (1024) |
| 256 | 512 MiB | 10 MiB | Bytes (~51 max-sized articles) |

```json
"SpoolDir": "",
"SpoolQueueCapacity": 1024,
"MaxQueuedBytes": 1073741824,
"PathAppend": "nntpd01.usenet.ninja!nntpspool.opticnetworks.net"
```

#### Spool observability

OpenTelemetry counter **`article_type_total`** (meter `Vector.NNTP.Articles`) is incremented once per successfully postprocessed article for each classification tag present (`type` label). Examples: `yenc`, `archive`, `video`, `text`, `default`. Multiple flags on one article increment multiple counters (for example yEnc binaries emit both `yenc` and `binary`).

Example PromQL for a five-minute rate by type:

```promql
sum by (type) (rate(article_type_total[5m]))
```

Other spool instruments: `nntp.spool.queue.*`, `nntp.spool.write.*`, `nntp.spool.preprocess.failure`, `nntp.spool.postprocess.failure`, `nntp.spool.payload.bytes_written`, `nntp.spool.history.commit_failure`, `nntp.spool.history.release_failure`, `nntp.spool.spamd.fail_open` (tagged `reason`), `nntp.spool.writers.scale_total` (tagged `direction=up|down`), `nntp.spool.queue.saturation_log`.

Latency histograms (milliseconds): `nntp.spool.preprocess.duration_ms`, `nntp.spool.postprocess.duration_ms`, `nntp.spool.write.duration_ms`, `nntp.spool.spamd.duration_ms`.

Optional OpenTelemetry tracing: register activity source `Vector.NNTP.Articles` (`ArticlesSpoolTelemetry.SourceName`) to collect spans `nntp.spool.preprocess`, `nntp.spool.postprocess`, `nntp.spool.spamd.check`, `nntp.spool.write`, `nntp.spool.history.release`, and `nntp.spool.history.commit`.

## Transit peers (NNTPD)

**NNTPD only.** `TransitPeers` defines trusted upstream providers that may use RFC 4644 streaming (`CHECK`, `TAKETHIS`, `IHAVE`) **without AUTH**, with cluster-wide connection caps enforced in Redis.

| Key | Purpose |
|-----|---------|
| `RefreshIntervalMinutes` | DNS snapshot rebuild interval for hostname `AcceptFrom` entries (default `10`). |
| `Peers[].Name` | Peer name for logs, metrics labels, and Redis coordination keys (e.g. `Giganews`). |
| `Peers[].MaxConnections` | Cluster cap via Redis ZSET (`0` = unlimited, default `10`). |
| `Peers[].AcceptFrom` | Literal IP, CIDR, or DNS hostname entries. |

Startup **fails** if any two peers have overlapping address ranges, if `Peers[].Name` values are duplicated (for example two entries named `Giganews`), or if required fields are invalid. Each `Name` must be unique because it keys Redis capacity coordination and metrics labels.

**Runtime reload:** `NNTPD.json` is loaded with `reloadOnChange: true`. Valid edits to `NntpServer` (including `TransitPeers`) take effect without restarting the process; the transit peer matcher rebuilds immediately on successful reload. Invalid edits are logged at Error and **ignored** until corrected—the server keeps the last-known-good configuration and continues accepting connections. Fix validation errors (duplicate names, overlapping-CIDR, malformed `AcceptFrom`, and so on) and save again to apply the change.

Hostname entries are resolved at startup and on each refresh; failed DNS snapshot rebuild keeps the previous matcher snapshot.

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
- **TAKETHIS** and **IHAVE** (before `335`) call `TryRecordAsync` (`SET NX`) before article storage as an in-flight reservation.
- **Articles spool commit** calls `TryRecordAsync` again after successful `AtomicWriteAsync` so history aligns with news-log **`+`** lines (not wire `239`/`235` alone). Spool preprocess/postprocess/write failures call `TryReleaseAsync`; TAKETHIS/IHAVE `439`/`437` paths after a successful record also release.

| Key | Purpose |
|-----|---------|
| `DbDir` | RocksDB directory (required for transit). |
| `RememberDays` | Retention window for duplicate suppression. |
| `MemoryLimitBytes` | Hot in-memory cache budget (default 1 GiB). |
| `MemoryShardCount` | Digest-key shard count for parallel CHECK/TAKETHIS (power of two; default 64). Per-shard budget is `MemoryLimitBytes / MemoryShardCount`. |
| `QueueCapacity` | Bounded backfill queue after Redis record on accept. |
| `KeyPrefix` | Optional Redis key prefix prepended to `history:{digest}` keys (defaults to empty; **not** the session `Redis:KeyPrefix`). |
| `RebuildCheckpointInterval` | Redis `history:rebuild_state` checkpoint interval during rebuild. |
| `RebuildRedisBatchSize` | Pipeline batch size for Rocks→Redis rebuild. |
| `EnableMemoryPreloadOnStartup` | Reverse-iterate `by_expiration` into memory after rebuild. |
| `RocksDb` | Optional Rocks tuning overrides. `DigestBlockCacheBytes` (JSON alias `BlockCacheBytes`) and `ExpirationBlockCacheBytes` (default 8 MB) size independent per-CF LRU caches. `DigestBloomBitsPerKey` (default 10) enables BuiltinBloom on `by_digest`. `EnableStatistics` (default true) and `StatsDumpPeriodSec` (default 600) drive native periodic `DUMPING STATS` / `PERSISTING STATS` sections in `DbDir/LOG` on RocksDB 10.x. `MirrorStatsToHostLogger` (default false) optionally duplicates snapshots into the NNTPD host logger via `HistoryRocksStatsLogHostedService` (legacy workaround for 6.x). Startup logs include `rocksdb.version`. See [historydb-rocksdb-schema.md](historydb-rocksdb-schema.md) for upgrade/rollback and stats notes. |

**Operational notes:**

- On **every process start**, NNTPD runs a **full Rocks→Redis rebuild** before CHECK and record paths are operational (`503` until complete).
- At billions of keys, rebuild may take hours; target throughput tiers are documented in the ADR (10k / 50k / 100k keys/s).
- Plan **hundreds of GB** on `DbDir` at multi-billion entry scale (`estimatedBytes ≈ liveEntryCount × 81 × overhead`).
- **RocksDB writes are asynchronous:** only successful **history record** (`TryRecordAsync`, including the post-spool commit on news-log **`+`**) enqueues a digest for the background worker to `PutReservation` in Rocks. CHECK `238` does not write Redis or Rocks. Opening the DB or finishing rebuild may show **0 writes** until accept-path traffic is persisted; SST files appear after memtable flush/compaction.
- **Digest keys, not Message-IDs:** all tiers store a **32-byte BLAKE3 digest** of the UTF-8 Message-ID string. Redis keys are `{HistoryDb.KeyPrefix}history:{digest-bytes}` (binary digest suffix, not hex). To inspect a committed article, derive the digest with `HistoryKeyEncoder.EncodeHexLower(messageId)` for Rocks/spool paths, or scan Redis with the configured `KeyPrefix` — do not expect human-readable Message-IDs in key names.
- **Release paths:** Articles spool failures (`-` in `news-{date}.log`) release in-flight reservations. TAKETHIS/IHAVE `439`/`437` after a successful record also release. Wire **`239`/`235`** alone does not guarantee durable history until spool commit (`+`).
- **Metrics correlation:** compare `history.record.recorded` (HistoryDB) with `nntp.spool.article.accepted` (Articles) over the same window. They should track within normal in-flight lag. If `recorded ≈ 0` while `accepted > 0`, the history commit path is failing — check `nntp.spool.history.commit_failure` and NNTPD logs (EventId 6/7). If `recorded >> accepted`, broad spool-layer releases may be clearing reservations (`nntp.spool.history.release_failure`, EventId 3/4). `history.memory.entries` reflects the in-memory hot cache only.

Register `AddNntpHistoryDatabase` after `AddNntpSessionRedis` and before `AddNntpSocketsTransit`.

## SpamAssassin (transit spool and reader hosts)

NNTPD binds `SpamAssassin` and `PostFilter` when `AddNntpArticlesTransitSpool(configuration)` is registered. NNRPD registers `AddSpamAssassin` for future reader POST filtering.

| Key | Purpose |
|-----|---------|
| `Hosts` | Round-robin spamd hostnames or IPs (one host per `CHECK`; required in practice). |
| `Port` | spamd TCP port (default `783`). |
| `SpamdProtocolVersion` | spamc protocol version (default `1.5`). |
| `ConnectTimeoutMilliseconds` | TCP connect timeout (default `5000`). |
| `OperationTimeoutMilliseconds` | End-to-end CHECK timeout (default `120000`). |

Transit spool postprocessing scans **non-yEnc** articles under **128 KiB** only. Spamd connectivity and protocol errors **fail open** (article accepted). yEnc articles run `YEncSectionCrc.Validate` instead; CRC failure rejects the article.

Synthetic spamd scan headers (`Received:`, `To:`, `X-Usenet-Newsgroups:`) are built from peer origin metadata and `NntpServer:NodeName` + `NntpServer:DomainName` — not from static scan-host JSON keys. The `Received:` `by` clause includes `NntpServer:ServerIdentification` in parentheses (for example `by nntpd01.usenet.ninja (Vector.NNTPD)`), and an `id` token carries the validated article Message-ID (for example `id <msgid@host>;`), giving SpamAssassin and human analysts full per-hop tracing context.

Example:

```json
"SpamAssassin": {
  "Hosts": ["198.18.0.70"],
  "Port": 783,
  "SpamdProtocolVersion": "1.5",
  "ConnectTimeoutMilliseconds": 5000,
  "OperationTimeoutMilliseconds": 120000
}
```

## TLS startup

- **Implicit TLS** (`TlsPort` > 0): handshakes use the current certificate from `CertificateRenewalService` (disk cache first, then ACME).
- **STARTTLS**: offered in `CAPABILITIES` only when `EnableStartTls` is true and a certificate is present.
- Connections accepted before a certificate is ready on the TLS port are closed with a debug log until renewal supplies a cert.
