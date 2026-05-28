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

Distributed session admission, byte quota, and heartbeats use a separate **`Redis`** section (see [Session management](session-management.md)).

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

## TLS startup

- **Implicit TLS** (`TlsPort` > 0): handshakes use the current certificate from `CertificateRenewalService` (disk cache first, then ACME).
- **STARTTLS**: offered in `CAPABILITIES` only when `EnableStartTls` is true and a certificate is present.
- Connections accepted before a certificate is ready on the TLS port are closed with a debug log until renewal supplies a cert.
