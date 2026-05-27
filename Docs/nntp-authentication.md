# NNTP authentication (Vector.NNTP.Sockets)

`Vector.NNTP.Sockets` implements **wire framing and state** for AUTHINFO and SASL. Credential verification is delegated to host-supplied stores — there is **no** RADIUS or legacy `Vector.NNRPD.Auth` dependency in this assembly.

## Contracts

| Interface | Purpose |
|-----------|---------|
| `INntpCredentialValidator` | AUTHINFO PASS, SASL PLAIN, SASL LOGIN passwords |
| `IScramCredentialStore` | SCRAM-SHA-256 and SCRAM-SHA-1 stored keys |
| `ICramMd5CredentialStore` | CRAM-MD5 shared secrets |

Successful validation returns `NntpAuthResult.Success` with `NntpSessionPolicy` (username, `AllowPosting`, account limits, and customer metadata).

## MySQL `nntpusers` (NNRPD and NNTPD)

When a database connection is configured, **NNRPD** and **NNTPD** register `Vector.NNTP.Auth.MySql` via `AddNntpMySqlAuthFromHostConfiguration`:

1. `ConnectionStrings:MainDB` (common in host JSON such as `NNRPD.json` / `NNTPD.json`).

When neither is present, development credential stubs remain active and log a warning on authentication attempts.

| Service | Role |
|---------|------|
| `MySqlNntpCredentialValidator` | `INntpCredentialValidator` — AES-decrypted password compare |
| `MySqlCramMd5CredentialStore` | CRAM-MD5 shared secret from the same row |
| `NntpSessionAdmissionTracker` | Enforces `account_session_limit` and `account_srcip_limit` |

Password lookup uses:

```sql
SELECT CAST(AES_DECRYPT(account_pass, UNHEX(SHA2(@account_name, 256))) AS CHAR) AS account_pass,
       account_type, account_rate_limit, account_byte_limit,
       account_session_limit, account_srcip_limit, is_enabled, customer_id
FROM nntpusers
WHERE account_name = @account_name;
```

Disabled accounts (`is_enabled = 'N'`) receive **481**. Admission limit violations during login receive **503** (transient authentication failure).

**RADIUS is not used** for reader authentication in this repository.

## AUTHINFO USER / PASS

1. Client: `AUTHINFO USER name` → **381** Password required  
2. Client: `AUTHINFO PASS secret` → **281** or **481**  
3. **502** if already authenticated  
4. **483** when `NntpServerOptions.RequireTlsForAuthInfo` is true and the connection is cleartext

## SASL mechanisms

| Mechanism | Notes |
|-----------|--------|
| PLAIN | Initial or 383 continuation; cancel with `*` → **481** |
| LOGIN | Base64 prompts via **334** |
| SCRAM-SHA-256 / SCRAM-SHA-1 | Multi-step **383**; requires `IScramCredentialStore` |
| CRAM-MD5 | Server challenge **334**; HMAC-MD5 verify via `ICramMd5CredentialStore` |

Unsupported mechanisms → **503**.

## SCRAM stored credential format

Hosts supply `ScramStoredCredential` per user:

- **Salt** — random bytes used at provisioning  
- **IterationCount** — PBKDF2 iterations (SCRAM-SHA-256 typically ≥ 4096)  
- **StoredKey** — `H(ClientKey)` from RFC 5802 derivation  
- **ServerKey** — used to verify client proof and build server-final

Provision keys offline; do not send cleartext passwords to the NNTP process.

## TLS gating

When `RequireTlsForAuthInfo` is enabled:

- `AUTHINFO USER` and SASL are omitted from CAPABILITIES on cleartext  
- Attempts on cleartext receive **483**

## Logging

`NntpCommandDispatcher` redacts lines containing `PASS` or `AUTHINFO` in debug logs. Hosts must not log SASL payloads or passwords at information level.

## Related docs

- [`nntp-security-ordering.md`](nntp-security-ordering.md) — STARTTLS / COMPRESS / AUTH ordering  
- [`session-state.md`](session-state.md) — `AuthenticationState`, pending user, SASL state  
- [`nntp-responses.md`](nntp-responses.md) — status code matrix  
