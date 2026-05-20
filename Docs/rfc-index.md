# NNTP and Usenet RFC index

Canonical specifications referenced by **Vector.NNRPD** (transport, session, and future article handling). Use the Datatracker or RFC Editor links below.

| RFC | Title (short) | Typical use in this project |
|-----|----------------|-----------------------------|
| [RFC 977](https://www.rfc-editor.org/rfc/rfc977) | Network News Transfer Protocol (original NNTP) | Legacy command surface; historical context |
| [RFC 1036](https://www.rfc-editor.org/rfc/rfc1036) | Standard for interchange of USENET messages | Article format; headers; message-id context |
| [RFC 2980](https://www.rfc-editor.org/rfc/rfc2980) | Common NNTP extensions | `LIST` variants, `OVER`, `XOVER`, etc. |
| [RFC 3977](https://www.rfc-editor.org/rfc/rfc3977) | Network News Transfer Protocol (NNTP) revision | Core NNTP, responses, `CAPABILITIES`, reader commands |
| [RFC 4642](https://www.rfc-editor.org/rfc/rfc4642) | Using Transport Layer Security (TLS) with NNTP | `STARTTLS` on cleartext port |
| [RFC 4643](https://www.rfc-editor.org/rfc/rfc4643) | Network News Access Protocol (NNTP) over TLS | Implicit TLS (NNTPS), TLS-first guidance |
| [RFC 4644](https://www.rfc-editor.org/rfc/rfc4644) | NNTP Streaming Feeds | `CHECK`, `IHAVE`, `TAKETHIS`, streaming |
| [RFC 5536](https://www.rfc-editor.org/rfc/rfc5536) | Netnews Architecture and Protocols | Message-ids, newsgroup naming |
| [RFC 5537](https://www.rfc-editor.org/rfc/rfc5537) | Netnews Architecture and Protocols (archived) | Archived article metadata |
| [RFC 8054](https://www.rfc-editor.org/rfc/rfc8054) | NNTP Extension for Compression | `COMPRESS DEFLATE` |

See also [rfc-nntp-security-ordering.md](rfc-nntp-security-ordering.md) for **AUTHINFO**, **STARTTLS**, and **COMPRESS** ordering on reader deployments.
