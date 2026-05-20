# RFC-driven NNTP session state (`Session`) checklist

This document describes **what must be tracked per TCP connection** to implement RFC-compliant NNTP session semantics, aligned with the current Stage 1 constraints of **Vector.NNRPD**.

Primary RFCs:
- **RFC 3977** (core NNTP session, `CAPABILITIES`, `MODE READER`, selected group/current article semantics)
- **RFC 4642** (`STARTTLS`)
- **RFC 4643** (NNTP over TLS guidance + `AUTHINFO` rules)
- **RFC 8054** (`COMPRESS DEFLATE`)

Project notes:
- RFC index: `docs/rfc-index.md`
- Security/ordering note: `docs/rfc-nntp-security-ordering.md`
- Response mapping note: `docs/nntp-responses.md`

## Stage 1 constraints (source of truth for this repo)

These constraints apply even where RFCs would allow broader behavior:

- A session is created automatically when a TCP connection is established:
  - unauthenticated by default
  - reader semantics are the default (see “Default mode semantics” below)
- `MODE` only supports **`READER`** and **`STREAM`**.
- While **unauthenticated**, only these commands are available:
  - `CAPABILITIES`, `MODE`, `QUIT`, `DATE`, `HELP`
  - everything else must be denied consistently until authentication succeeds
- Track session TX/RX bytes **over the socket**, including framing bytes like `\r\n`.

## Session state field inventory (what to store per connection)

The names below are conceptual. They map to existing state owners today (see “Current implementation mapping”), and to the missing fields required for RFC 3977 group/article semantics.

### A) Identity, accounting, and policy

- **SessionId**: stable identifier for logs/metrics correlation.
- **ClientRemoteEndPoint**: effective client endpoint (post-PROXY if enabled).
- **ProxyHopEndPoint**: TCP peer endpoint (proxy hop or direct client).
- **SessionStartedUtc**.
- **RxBytesTotal / TxBytesTotal**:
  - must include protocol framing bytes (notably CRLF on command lines and response lines).
- **Authenticated state**:
  - `IsAuthenticated` (bool)
  - `AuthenticatedUsername` (string?)
  - `AuthenticatedReaderPolicy` (permissions + limits, e.g. posting allowed, rate limits)
- **Authentication handshake pending**:
  - `AuthInfoUserPending` (string?) for the RFC 4643 `AUTHINFO USER` → `AUTHINFO PASS` continuation.

### B) Transport negotiation state (stream stack)

- **Current transport stack**:
  - reference to the current duplex stream (`Stream`), which may be wrapped.
- **IsTlsActive**: derived from `Stream is SslStream` (or explicit flag).
- **IsCompressionActive**: whether RFC 8054 compression wrapper is active.
- **CanAdvertiseStartTls** (derived): only when cleartext, TLS material exists, and compression is not active (RFC 4642 + RFC 8054 ordering).
- **CanAdvertiseAuthInfoUser** (derived): RFC 4643 + deployment rules (e.g. do not advertise on cleartext when `RequireTlsForAuthInfo` is true).
- **CanAdvertiseCompress** (derived): RFC 8054 + deployment rules (often TLS-first).

### C) Protocol “mode” and capability gating

- **Mode**: one of:
  - `Reader` (MODE READER accepted / reader semantics active)
  - `Stream` (MODE STREAM accepted / streaming semantics active)
- **Default mode semantics** (Stage 1):
  - connection begins in “reader semantics” even before explicit `MODE READER` (see command gating).
- **Unauthenticated command gate** (Stage 1):
  - enforce an allow-list while `IsAuthenticated == false`.

### D) RFC 3977 selected group + current article state (required for compliance)

RFC 3977 requires stateful behavior for:
- `GROUP`
- article-number addressing for `STAT`/`ARTICLE`/`HEAD`/`BODY`
- `NEXT` / `LAST`
- `LISTGROUP`

Track at minimum:

- **SelectedGroup**:
  - `SelectedGroupName` (string?)
  - `SelectedGroupLowWatermark` (long?)  — the lowest article number currently present
  - `SelectedGroupHighWatermark` (long?) — the highest article number currently present
  - `SelectedGroupEstimatedCount` (long?) — count returned by `GROUP`
  - optional `SelectedGroupIsValid` if you distinguish “never selected” vs “invalid group”.

- **CurrentArticle** (within selected group):
  - `CurrentArticleNumber` (long?) — undefined until `GROUP` success or a successful article-number command sets it
  - optional `CurrentArticleMessageId` (string?) if you want to correlate message-id lookups with current pointer behavior.

Derived/behavioral rules:
- `GROUP` success sets `SelectedGroup*` and typically sets `CurrentArticleNumber` to the first article in the group (or a defined default), per RFC semantics you implement.
- `GROUP` change resets or rebinds `CurrentArticle*` to the new group’s numbering.
- `NEXT`/`LAST` update `CurrentArticleNumber` on success.

### E) Multi-line input mode (future but must be tracked when implemented)

For commands that switch the session into a mode where subsequent client lines are treated as data (dot-terminated):
- `POST`
- `IHAVE` (when implemented)

Track:
- `PendingMultiLineCommand` (None/Post/IHave/…)
- `PendingMultiLineContext` (message-id, size counters, etc.)

## Command-by-command state transitions (Stage 1)

This section is the compliance-oriented “matrix”: which commands read/modify which state.

Legend:
- **R**: reads state
- **W**: writes state
- **G**: gated by state (denied unless conditions true)

### Common gating (applies to most commands)

- **Unauthenticated allow-list (Stage 1)**:
  - If `IsAuthenticated == false` then only allow:
    - `CAPABILITIES`, `MODE`, `QUIT`, `DATE`, `HELP`
  - All other verbs must return a consistent denial (exact code/text is a separate decision; see gap analysis).

- **Host profile gating**:
  - Reader vs transit/streaming is a separate axis (profile/role).

### `CAPABILITIES` (RFC 3977 / 4642 / 4643 / 8054)

- **R**: `Mode`, `IsAuthenticated`, `IsTlsActive`, `IsCompressionActive`, `RequireTlsForAuthInfo`, listener TLS availability
- **W**: none
- **Must reflect current state**:
  - before authentication: advertise `MODE-READER` and possibly `AUTHINFO USER` (subject to TLS rules)
  - after authentication: omit `MODE-READER` and `AUTHINFO USER` (RFC 4643)
  - offer `STARTTLS` only if not already TLS and not compressed (RFC 4642 + RFC 8054)
  - offer `COMPRESS DEFLATE` only if permitted and not already compressed (RFC 8054)

### `MODE READER` / `MODE STREAM` (RFC 3977; additional RFC 4643 constraint)

- **R**: `IsAuthenticated` (RFC 4643 forbids MODE READER after AUTHINFO completion), host profile
- **W**: `Mode`
- **Stage 1**:
  - only keywords `READER` and `STREAM` are accepted

### `STARTTLS` (RFC 4642 / 4643)

- **G**: must be rejected if `IsTlsActive == true` or `IsCompressionActive == true`
- **W**: upgrades `Stream` to TLS; `IsTlsActive` becomes true
- **R/W**: affects subsequent `CAPABILITIES` advertisement and credential gating

### `COMPRESS DEFLATE` (RFC 8054)

- **G**: must be rejected if `IsCompressionActive == true`; may be rejected unless TLS is active (deployment policy)
- **W**: wraps `Stream` with compression; `IsCompressionActive` becomes true
- **R/W**: once compressed, `STARTTLS` must not be permitted

### `AUTHINFO USER` / `AUTHINFO PASS` (RFC 4643)

- **G**: may require TLS first (deployment via `RequireTlsForAuthInfo`)
- **W**:
  - `AuthInfoUserPending` set by USER, cleared/consumed by PASS
  - `IsAuthenticated` + `AuthenticatedUsername` + `AuthenticatedReaderPolicy` set on success
- **Post-auth**:
  - further AUTHINFO attempts must be rejected (RFC 4643)

### `HELP`, `DATE`, `QUIT` (RFC 3977)

- **Help/Date**: stateless (no required session tracking beyond Rx/Tx byte counters).
- **QUIT**: ends the session (transport close).

### `GROUP`, `LISTGROUP`, `STAT`, `ARTICLE`, `HEAD`, `BODY`, `NEXT`, `LAST` (RFC 3977)

These commands require the state in section D:

- **G**:
  - denied until authenticated (Stage 1 rule)
  - `GROUP` must validate group name and bind selected group state
  - commands using article numbers require selected group
- **W**:
  - `GROUP` writes `SelectedGroup*` and resets/binds `CurrentArticle*`
  - `NEXT`/`LAST` write `CurrentArticleNumber` on success
  - successful number-based `STAT`/`ARTICLE`/`HEAD`/`BODY` may update `CurrentArticleNumber` (depending on semantics chosen)

## Current implementation mapping (what we track today)

### `Core/Sockets/Session/NntpConnectionContext.cs`

Currently holds:
- Session identity (`SessionId`, endpoints, started time)
- Authentication state (`IsAuthenticated`, `AuthenticatedUsername`, `AuthenticatedReaderPolicy`)
- Accounting counters:
  - `TotalBytesReceived` / `TotalBytesSent` (bytes added by the session loop, including CRLF for command lines; response bytes added from `WriteAsciiAsync`)
  - article request/delivery counters (for future storage wiring)

### `Core/Sockets/Transport/NntpSessionRunner.cs` (nested `NntpSessionState`)

Currently holds:
- `Stream` (current duplex stream)
- `ReaderModeActive` (bool) — whether MODE READER was accepted
- `CompressionActive` (bool)
- `AuthInfoUserPending` (string?)

### Command gating today

Today’s reader gating is implemented as “deny reader data verbs until authenticated” (480) via `Core/Sockets/Transport/NntpCommandGate.cs`.

## Gap analysis (required by plan)

### Missing RFC 3977 state

Not currently tracked / not implemented:
- `SelectedGroup*` (selected newsgroup name + low/high/count snapshot)
- `CurrentArticleNumber` (current article pointer)
- navigation state needed for `NEXT`/`LAST` and number-based article commands

### Stage 1 unauthenticated allow-list vs current behavior

Stage 1 requirement:
- While unauthenticated, allow only: `CAPABILITIES`, `MODE`, `QUIT`, `DATE`, `HELP`

Current behavior (as implemented today):
- Some additional commands are effectively reachable pre-auth depending on verb classification; the existing gate focuses on “reader data retrieval verbs” rather than an explicit allow-list.

Action:
- Implement an explicit allow-list gate (in the session loop) or refactor `NntpCommandGate` to support this stricter policy.

### Default MODE READER semantics

Stage 1 requirement:
- “Create a session automatically… unauthenticated, MODE READER by default”

Current implementation:
- A session exists immediately at accept, but `ReaderModeActive` starts false.

Action:
- Decide whether “default MODE READER” means:
  - (a) treat the session as reader semantics for gating/capabilities without having accepted the MODE command, or
  - (b) actually set `ReaderModeActive = true` at session start (but still respect RFC 4643 constraints around MODE usage).

### Denial codes/text for unauthenticated commands

RFCs and deployments commonly use `480 Authentication required` for reader data commands, but the Stage 1 allow-list introduces a broader “deny-all except …”.

Action:
- Specify:
  - whether all denied verbs return `480` vs `502` vs `500` (and when to record parse errors)
  - ensure `CAPABILITIES` and `HELP` remain available and informative.

## Follow-on implementation outline (no code changes in this doc)

Smallest safe path to reach RFC-compliant session tracking:

1. **Introduce a single `SessionState` model** (internal) that includes:
   - transport flags, auth handshake, and the missing RFC 3977 fields (`SelectedGroup*`, `CurrentArticleNumber`)
2. **Add strict unauthenticated allow-list gating** in the command dispatch loop.
3. Implement `GROUP` first, then `STAT`/`NEXT`/`LAST` semantics against a fake/group catalog interface (so behavior is testable before storage wiring).
4. Add transcript-style tests covering:
   - no group selected errors
   - group selection sets pointer, NEXT/LAST updates it
   - unauthenticated allow-list enforcement
   - capability advertisement changes across TLS/auth/compress transitions

## Appendix: Streaming/transit (RFC 4644) state (future)

When streaming is implemented, add per-peer/session state for:
- feed/peer authentication and permissions
- in-flight `CHECK` / `TAKETHIS` correlation tracking
- duplicate suppression windows and acceptance bookkeeping
