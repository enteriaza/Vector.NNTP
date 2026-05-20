# AUTHINFO, STARTTLS, and COMPRESS on reader servers

This note summarizes **deployment guidance** aligned with **RFC 4642** (STARTTLS), **RFC 4643** (NNTP over TLS), **RFC 3977** / **RFC 4643** (AUTHINFO), and **RFC 8054** (COMPRESS DEFLATE). It is not a substitute for the RFCs.

## Password confidentiality (RFC 4642 / 4643)

- **`AUTHINFO PASS`** sends a password on the wire. On **cleartext** NNTP, operators should assume the password can be observed unless the path is fully trusted.
- **RFC 4642** defines `STARTTLS` so the client can upgrade to TLS **before** sending sensitive credentials.
- **RFC 4643** covers **implicit TLS** (e.g. port 563): the entire NNTP session is already inside TLS, so `AUTHINFO PASS` is not sent in the clear on that path.

**Recommendation:** On reader hosts, prefer **implicit TLS** or **`STARTTLS` before `AUTHINFO`**. The `Plain:RequireTlsForAuthInfo` option in `nnrpd.json` (when `true`) rejects **`AUTHINFO USER`** and **`AUTHINFO PASS`** on cleartext (**483**), omits **`AUTHINFO USER`** from **`CAPABILITIES`** on cleartext (RFC 4643), omits **`COMPRESS DEFLATE`** until TLS is active, and rejects **`COMPRESS DEFLATE`** on cleartext with **502** so compression is negotiated only on top of TLS. Per-user posting and bandwidth limits are not part of that flag; they come from **`SessionPolicy`** after successful authentication.

## COMPRESS DEFLATE (RFC 8054)

- Compression negotiates on the **current** byte stream. Typical order is: establish transport (plain or TLS), optionally authenticate, then negotiate **COMPRESS DEFLATE** if desired.
- **Do not** send passwords **after** compression unless you understand the security model; in practice **TLS first**, then compression **on top of TLS**, is the common stack.
- **`CAPABILITIES`** lists **`STARTTLS`** (when available) before **`AUTHINFO USER`**, then extension lines, then **`COMPRESS DEFLATE`** when permitted. **`STARTTLS`** is not offered while compression is already active; the server responds with **502** if the client sends **`STARTTLS`** after **`COMPRESS DEFLATE`**.

## AUTHINFO flow (RFC 4643 / 3977)

- **`AUTHINFO USER`** then **`AUTHINFO PASS`** is the common sequence; the server responds with continuation (e.g. **381**) after `USER`, then **281** on success or **481** on failure.
- **`Vector.NNRPD`** implements a **minimal stub**: any non-empty `USER` followed by any non-empty `PASS` succeeds until a pluggable authenticator exists. Production deployments must replace this with real validation.

## Reader command gating (this codebase)

On **Reader** role hosts, **data retrieval** verbs are denied with **480 Authentication required** until the session is marked authenticated (`NntpConnectionContext.IsAuthenticated`). See `NntpCommandGate.TryGetReaderAuthenticationDenial` and [nntp-responses.md](nntp-responses.md).
