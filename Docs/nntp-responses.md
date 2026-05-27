# NNTP response code matrix

This document summarizes NNTP status codes emitted by `Vector.NNTP.Sockets` handlers. It complements [`session-state.md`](session-state.md) and [`nntp-security-ordering.md`](nntp-security-ordering.md).

## Connection

| Code | Text (prefix) | When |
|------|----------------|------|
| 200 | Ready | Initial greeting after accept |
| 205 | Goodbye | Successful QUIT |

## Informational / capabilities

| Code | Text (prefix) | When |
|------|----------------|------|
| 101 | Capability list | CAPABILITIES multi-line start |
| 111 | (date) | DATE |
| 100 | Help text follows | HELP multi-line start |

## Reader (RFC 3977)

| Code | Text (prefix) | When |
|------|----------------|------|
| 211 | (count low high group) | GROUP selected |
| 220 | Article follows | ARTICLE/HEAD/BODY payload |
| 223 | (number id) | STAT |
| 215 | Newsgroups in form | LIST |
| 340 | Send article | POST body prompt |
| 240 | Article posted | POST success |
| 411 | No such newsgroup | Unknown GROUP |
| 412 | No group selected | Article/overview without GROUP |
| 423 | No such article number | Article number out of range |
| 430 | No such article | Missing article |
| 441 | Posting failed | POST rejected by storage |
| 480 | Authentication required | Gate: reader data before auth |
| 480 | Posting not permitted | POST without policy |
| 503 | Service unavailable | Null storage, auth backend down, unsupported NEWNEWS/NEWGROUPS/SLAVE |

## Security (RFC 4642 / 8054)

| Code | Text (prefix) | When |
|------|----------------|------|
| 382 | Continue with TLS | STARTTLS accepted |
| 206 | Compression active | COMPRESS DEFLATE enabled |
| 483 | Encryption required | AUTHINFO when TLS required |
| 502 | Permission denied | Gate / profile mismatch |
| 502 | STARTTLS not permitted after COMPRESS | Ordering violation |

## Authentication (RFC 4643)

| Code | Text (prefix) | When |
|------|----------------|------|
| 381 | Password required | AUTHINFO USER ok |
| 281 | Authentication accepted | AUTHINFO PASS ok |
| 481 | Authentication failed | Bad credentials |
| 481 | Authentication cancelled | SASL `*` cancel |
| 235 | Authentication succeeded | SASL success |
| 383 | (challenge) | SASL continuation |
| 502 | Already authenticated | AUTHINFO when logged in |
| 503 | Mechanism not supported | Unknown SASL |
| 503 | Temporary authentication failure | Validator transient error |

## Transit (RFC 4644)

| Code | Text (prefix) | When |
|------|----------------|------|
| 438 | No such article | CHECK result (wanted / not wanted text variants) |
| 238 | Article wanted | CHECK accepts transfer |
| 203 | Streaming is OK | MODE STREAM on transit profile |
| 335 | Send article | IHAVE accepted |
| 373 | Send article | TAKETHIS body prompt |
| 235 | Article transferred OK | Store success |
| 435 | Already have it | IHAVE rejected |
| 439 | Transfer failed | Store failure |

## Errors

| Code | Text (prefix) | When |
|------|----------------|------|
| 500 | Unknown command | Unrecognized verb |
| 501 | (detail) | Syntax / argument errors |
| 503 | Program fault | Unhandled session exception |
