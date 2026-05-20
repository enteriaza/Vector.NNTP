# Article storage binary protocol (version 0)

This document describes the wire format implemented by `Vector.NNRPD` (`Core/Network/StorageProtocol.cs`).

## Frame layout

All multi-byte integers are **big-endian** (network byte order).

First byte: `0bVVOOOOOO` — two-bit **version** (must be `00` for v0) and six-bit **opcode**.

| Field | Size | Notes |
|--------|------|--------|
| Versioned opcode | 1 | Version in high two bits; opcode in low six bits |
| Request id | 4 | `uint32`, client-chosen correlation id |
| Message id | 16 | Opaque binary key (NNTPStorage uses MD5 of ASCII Message-ID, 16 bytes) |

### Header-only frames (21 bytes total)

Used by **GetRequest** (`0x01`), **GetResponseNotFound** (`0x03`), and the fixed prefix before length on payload frames.

No CRC field; `StorageFrame.PayloadCrcStatus` is **NotApplicable**.

### Payload frames (29-byte header + N bytes)

Used by **GetResponseFound** (`0x02`) and **SubmitRequest** (`0x04`).

After the 16-byte message id:

| Field | Size | Notes |
|--------|------|--------|
| Payload length | 4 | `uint32`; must not exceed **10 485 760** (10 MiB) |
| CRC-32 | 4 | `uint32`, CRC-32/ISO-HDLC over **payload bytes only** (same as `System.IO.Hashing.Crc32`) |
| Payload | N | Raw article octets |

If the declared length exceeds the maximum, parsers report **ProtocolViolation** (`0xFF` sentinel opcode) and must not advance the receive buffer (same as unknown opcode).

### SubmitResponse (22 bytes)

Opcode `0x05`, request id, message id, then one **status** byte:

| Value | Meaning |
|--------|---------|
| `0x00` | Success |
| `0x01` | Failure |

## Protocol violation (sentinel)

`TryParseFrame` sets `StorageOpcode.ProtocolViolation` when:

- The wire **version** is not v0 (buffer is **not** advanced).
- The declared **payload length** is greater than 10 MiB (buffer **not** advanced).
- The six-bit **opcode** is not one of the defined v0 values (buffer **not** advanced).

The host should close the connection on this opcode. The value `0xFF` is reserved as this sentinel in the API; it is not a normal wire opcode for v0 clients.

## CRC semantics

- CRC covers **payload only**, not the header.
- On **CRC mismatch**, a full frame is still consumed (buffer advanced); `StorageFrame.PayloadCrcStatus` is **Mismatch** and the payload must not be trusted.

## API surface

Use `Core/Network/StorageProtocol.cs`: `TryParseFrame`, `ComputePayloadCrc`, and writers (`WriteGetResponseNotFound`, `WriteGetResponseFoundHeader`, `WritePayload`, `WriteSubmitResponse`) accepting `IBufferWriter<byte>` or `PipeWriter`.

## Security and transport

The protocol has **no authentication or encryption**. Use TLS (for example mTLS) or a private network boundary. CRC-32 detects accidental corruption, not intentional tampering.
