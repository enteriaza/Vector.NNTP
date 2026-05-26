# Vector.NNTP.Utilities

Shared utility library for Vector NNTP components (MessageBus, Encryption, NNRPD, NNTPD).

**Contribution standards:** see [CONTRIBUTING.md — Vector.NNTP.Utilities library standards](../CONTRIBUTING.md#vectornntputilities-library-standards) for hot/cold path rules, allocation policy, throw helpers, and Internal folder policy.

## Namespaces

| Namespace | Responsibility |
|-----------|----------------|
| `Vector.NNTP.Utilities.Async` | Fire-and-forget task observation, cancellable delays |
| `Vector.NNTP.Utilities.Diagnostics` | Log formatting, assembly/environment metadata |
| `Vector.NNTP.Utilities.Disposal` | Best-effort `IDisposable` / `IAsyncDisposable` helpers |
| `Vector.NNTP.Utilities.Dns` | RFC 1035 wire-format helpers and query builder |
| `Vector.NNTP.Utilities.Encoding` | ASCII encode/decode/validate (SIMD via BCL) |
| `Vector.NNTP.Utilities.IO` | Atomic file I/O, response size-limited streams |
| `Vector.NNTP.Utilities.Metrics` | EWMA blend and atomic bit-pattern helpers |
| `Vector.NNTP.Utilities.Networking` | Host parsing, IP classification |
| `Vector.NNTP.Utilities.Parsing` | Zero-allocation ASCII span helpers |
| `Vector.NNTP.Utilities.Retry` | Exponential backoff calculation and delay |
| `Vector.NNTP.Utilities.Security` | Credential redaction, secure buffer zeroing |
| `Vector.NNTP.Utilities.Validation` | Startup validation (DNS, credential placeholders) |
| `Vector.NNTP.Utilities.Internal` | Internal throw, span, guard, and pooling helpers (not public API) |

Domain-specific cryptography (Certes / ACME CSR/PFX) lives in `Vector.NNTP.Encryption.Certificates.Acme`, not in this assembly.

## Naming conventions

| Prefix | Semantics |
|--------|-----------|
| `Try*` | Expected failures return `false` (and optional `out` values); do not throw |
| `Validate*` | May throw on invalid *arguments*; bool-returning validation prefers `Try*` names |
| `Is*` / `Has*` | Pure predicates; no throws for normal inputs |
| `*Async` | Asynchronous operations; document cancellation (propagate vs return `false`) |

Overload order: **Span/Memory → string → async**, optional parameters last.

## Exception policy

- Use BCL throw helpers: `ArgumentNullException.ThrowIfNull`, `ArgumentException.ThrowIfNullOrEmpty`, `ArgumentOutOfRangeException.ThrowIfNegative`.
- Document every thrown type on public APIs with `<exception cref="..."/>`.
- Helpers that swallow exceptions (`DisposalUtilities`, `TryReadFileAsync`, `DelayOrCancelledAsync`) return the exception or map cancellation to `false` — they do not log.

## Dependencies

This project references **BCL** and **`Microsoft.Extensions.Logging.Abstractions`** only. Do not add RabbitMQ, Certes, or other domain packages here.

## Span/Memory guidance

Add Span overloads on hot paths (parsing, DNS, encoding) when callers already hold spans. Keep string overloads for configuration and logging. Do not introduce extension methods unless a type-specific pattern is insufficient.

## Thread safety

Document per type in XML `<remarks>`:

- Stateless static helpers: safe for concurrent use.
- Instance types (`LengthLimitedReadStream`): document single-reader / not thread-safe contracts.
- `EwmaUtilities.Blend*`: caller synchronises shared EWMA state.

## Static mutable state

| Location | Notes |
|----------|-------|
| `AssemblyInfoUtilities` | Static ctor initialises readonly metadata strings |
| `CredentialPlaceholderDetector.CommonPlaceholders` | Immutable `FrozenSet` |
| `TaskUtilities.ObserveExceptionContinuation` | Immutable delegate |
| `Random.Shared` | Used in `RetryUtilities`, `DnsWireQueryBuilder` (thread-safe) |

## Future expansion

Add a new namespace when a folder would exceed ~5 unrelated types or mixes hot-path binary operations with cold diagnostics. Prefer extending existing namespaces before creating `Memory`, `Concurrency`, or `Collections` unless multiple types justify them.
