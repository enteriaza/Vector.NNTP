# NNTP host configuration (`NntpServer` section)

NNRPD and NNTPD bind a single JSON section named `NntpServer` to **two** option types:

| Property | Subsystem | Purpose |
|----------|-----------|---------|
| `NodeName` | `Vector.NNTP.Encryption` | Stable node id for ACME/cluster logging (required). |
| `Port` | `Vector.NNTP.Sockets` | Cleartext NNTP listener (default `119`). |
| `TlsPort` | `Vector.NNTP.Sockets` | Implicit TLS (NNTPS) listener; `0` disables (default `0`). |
| `BindAddress` | Sockets | Bind address (`0.0.0.0` or `*` for all interfaces). |
| `IdleTimeout` | Sockets | Per-read idle timeout (ISO 8601 duration). |
| `MaxConnections` | Sockets | Concurrent connection cap (`0` = unlimited). |
| `ServerIdentification` | Sockets | Banner and CAPABILITIES `IMPLEMENTATION` (defaults to host assembly name). |
| `EnableStartTls` | Sockets | Advertise and accept `STARTTLS` when a certificate is available. |
| `EnableCompressDeflate` | Sockets | Advertise `COMPRESS DEFLATE` (wire compression not yet implemented). |
| `RequireTlsForAuthInfo` | Sockets | Reject AUTHINFO/SASL until TLS is active. |

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
  "LetsEncrypt": {
    "Enabled": true,
    "CertDir": "certs",
    "AccountKeyPem": "-----BEGIN EC PRIVATE KEY-----..."
  }
}
```

For local development without ACME, set `"LetsEncrypt": { "Enabled": false }` (a cached `certificate.pfx` under `CertDir` is still loaded for TLS). If `Enabled` is true but `AccountKeyPem` is omitted, the host reads `{CertDir}/letsencrypt.pem` when present; in Development only, incomplete ACME settings disable renewal instead of failing startup.

Legacy keys in existing host JSON files are ignored by the new binders unless they map to a property on one of the option types above.

## TLS startup

- **Implicit TLS** (`TlsPort` > 0): handshakes use the current certificate from `CertificateRenewalService` (disk cache first, then ACME).
- **STARTTLS**: offered in `CAPABILITIES` only when `EnableStartTls` is true and a certificate is present.
- Connections accepted before a certificate is ready on the TLS port are closed with a debug log until renewal supplies a cert.
