---
inclusion: fileMatch
fileMatchPattern: "csharp/**/*.cs"
---

# Modbus TCP facts for GhostWire code

Sources: Modbus Application Protocol Specification V1.1b3 and Modbus Messaging on TCP/IP Implementation Guide V1.0b. Write code from these facts, not from memory of other libraries.

## Byte order

Every multi-byte field is big-endian: MBAP fields, addresses, quantities and register values (high byte first). Use `BinaryPrimitives.ReadUInt16BigEndian` / `WriteUInt16BigEndian`. A 32-bit value spans two registers, and the word order is device-specific. The spec does not define it, so take it from the scenario documentation.

## ADU = MBAP header (7 bytes) + PDU

| Offset | Size | Field | Rule |
|---|---|---|---|
| 0 | 2 | Transaction ID | Chosen by the client and echoed by the server. Match responses on it. |
| 2 | 2 | Protocol ID | Always `0x0000` for Modbus. Reject anything else. |
| 4 | 2 | Length | Number of bytes that follow: unit ID + PDU, so `1 + PDU length`. Valid range 2–254. |
| 6 | 1 | Unit ID | Routes through a gateway to a serial device. Echoed in the response. |
| 7 | n | PDU | Function code (1 byte) + data. Max 253 bytes, so max ADU is 260. |

## PDU layouts

FC03 Read Holding Registers / FC04 Read Input Registers:

- Request (5 bytes): `fc`, start address (2), quantity (2). Quantity 1–125 (`0x007D`).
- Response: `fc`, byte count (1) = `2 × quantity`, then `quantity` registers of 2 bytes each.
- Example: `00 01 00 00 00 06 07 03 00 0A 00 01` reads 1 holding register at offset 10 from unit 7. The reply `00 01 00 00 00 05 07 03 02 00 27` returns value 39.

FC06 Write Single Register:

- Request (5 bytes): `0x06`, register address (2), value (2). Any value from `0x0000` to `0xFFFF`.
- Normal response: an exact echo of the request PDU.
- In GhostWire, FC06 only appears inside the simulator (Phantom Writer script or recorded log). The learner-facing API never sends it.

## Exception responses

- PDU = `function | 0x80`, then one exception code byte (2 bytes in total). Example: `0x84 0x02`.
- `01` Illegal Function: function not supported.
- `02` Illegal Data Address: start address, or address + quantity, outside the device's map.
- `03` Illegal Data Value: bad request structure, for example quantity outside 1–125 or a wrong PDU length.
- A real gateway may return `0A` (path unavailable) or `0B` (target device failed to respond) instead of staying silent. The GhostWire simulator stays silent on a unit mismatch. This is one reason a timeout never proves a single cause.

## Addressing

- PDU addresses are zero-based offsets 0–65535.
- Documentation labels such as `4xxxx` (holding registers, FC03/FC06) and `3xxxx` (input registers, FC04) are one-based: `40001` is offset 0 and `30021` is offset 20. Six-digit forms (`400001`) follow the same rule.
- Vendors are inconsistent about this. Use what the scenario documentation states. GhostWire documentation uses zero-based offsets.
- Check `address + quantity <= 65536` before building a request.

## Frame handling in this repo

- Read the 7-byte header first, validate protocol ID and length, then read exactly `length - 1` PDU bytes.
- Validate the response before trusting it: transaction ID, protocol ID, unit ID, function code (or `fc | 0x80`), byte count, and total length.
- Every read path has a deadline. A timeout is reported as `status: "timeout"`, never as a diagnosis.
