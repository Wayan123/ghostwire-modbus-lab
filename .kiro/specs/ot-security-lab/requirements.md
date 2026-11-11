# Requirements Document: ot-security-lab

## Introduction

This feature extends GhostWire from a three-case Modbus troubleshooting lab into an OT security and troubleshooting lab. It adds:

- a frame inspector that shows the actual Modbus TCP bytes of every simulated read;
- a defensive incident case, **Phantom Writer**, in which the learner finds an unauthorized FC06 write in a simulated captured traffic log;
- case categories and difficulty;
- property-based tests in the existing dependency-free harness;
- updated judge documentation for all four cases.

The existing cases (The Giant Pressure, The Silent Sensor, The Wrong Register) keep their behavior. Existing API fields keep their names, types and meaning. Every value shown to a learner comes from a simulator run on loopback. Nothing in this feature connects to physical equipment, lets a learner send a Modbus write, or claims anything about a real installation.

The steering files `.kiro/steering/{product,safety,tech,modbus-protocol,accessibility}.md` apply to every requirement below.

## Glossary

- **ADU**: Modbus TCP application data unit. The 7-byte MBAP header followed by the PDU.
- **MBAP header**: Modbus Application Protocol header: transaction ID (2 bytes), protocol ID (2), length (2), unit ID (1).
- **PDU**: Protocol data unit. Function code (1 byte) followed by function-specific data.
- **FC03 / FC04 / FC06**: Read Holding Registers / Read Input Registers / Write Single Register.
- **Frame**: The exact bytes of one ADU sent or received on a loopback TCP connection.
- **Frame inspector**: The "Wire evidence" panel and the `frames` field that expose frames and their decoded fields.
- **Learner read path**: `POST /api/test` and the `ModbusClient` code it calls.
- **Incident generator**: Server-side startup code that performs the scripted Phantom Writer exchanges against its own private simulator and records them.
- **Traffic log**: The simulated captured traffic produced by the incident generator.
- **Client label**: A simulated name (for example `HMI-01`) that the incident generator assigns to each of its own TCP connections. Modbus TCP frames carry no client identity; the label is capture metadata.
- **Evidence categories**: `observations`, `possible_causes`, `training_hints`, `next_steps`, `verified`.
- **Property**: A statement that must hold for every generated input in a property-based test.

## Requirements

### Requirement 1: Frame inspector

**User Story:** As a learner, I want to see the exact Modbus TCP bytes of each simulated read and what each byte means, so that I can check my request and the device's reply from wire evidence instead of from a summary.

#### Acceptance Criteria

1. WHEN `POST /api/test` completes with status `response`, `exception` or `error`, THE SYSTEM SHALL include a `frames` array in the result containing one request frame and, if any response bytes were received, one response frame.
2. WHEN `POST /api/test` completes with status `timeout`, THE SYSTEM SHALL include only the request frame in `frames`.
3. IF the request bytes were not fully written to the socket (for example the loopback connection failed), THEN THE SYSTEM SHALL return an empty `frames` array and SHALL NOT show a request frame that was never transmitted.
4. THE SYSTEM SHALL represent each frame as `{ "direction": "request" | "response", "hex": string, "fields": [ { "name", "value", "bytes" } ], "decode_error": string | null }`, where `hex` is the uppercase, space-separated hexadecimal form of the bytes and `bytes` is `{ "offset": int, "length": int }` into that frame.
5. THE SYSTEM SHALL build `hex` from the byte arrays actually written to and read from the loopback socket, not by re-encoding the request settings.
6. WHEN a frame is decoded, THE SYSTEM SHALL list these fields in byte order: transaction ID, protocol ID, length, unit ID, function code, then for an FC03/FC04 request the start address and quantity; for an FC03/FC04 response the byte count and one field per register value; for an FC06 request or response the register address and register value; for an exception response the exception code.
7. IF a response frame cannot be fully decoded (wrong protocol ID, length outside 2–254, byte count mismatch or truncated data), THEN THE SYSTEM SHALL return the fields decoded so far and a non-null `decode_error` describing the first failure, and SHALL NOT throw.
8. WHEN a result is rendered, THE SYSTEM SHALL show the frames in a collapsible panel titled "Wire evidence", collapsed by default, labeled as simulated.
9. THE SYSTEM SHALL render each frame as a `<table>` with header cells for field name, bytes (hex, monospace) and decoded value, and SHALL define MBAP, PDU, unit ID and function code in plain language the first time the panel shows them.
10. WHEN a learner hovers over or moves keyboard focus to a field row, THE SYSTEM SHALL highlight the matching bytes in the frame's hex display and SHALL indicate the link in text (byte offset and length), not by color alone.
11. THE SYSTEM SHALL make the panel and every field row reachable and operable with the keyboard alone, and SHALL respect `prefers-reduced-motion`.

### Requirement 2: Phantom Writer incident case

**User Story:** As a learner, I want to investigate a simulated unauthorized setpoint change from a captured traffic log, so that I can practice identifying a Modbus write from evidence and learn which real-world controls reduce that risk.

#### Acceptance Criteria

1. THE SYSTEM SHALL provide a scenario with id `phantom-writer`, title "Phantom Writer" and category `security`, whose simulated device is a boiler controller at unit ID 5 with holding register offset 100 holding the temperature setpoint at 0.1 °C per count, documented normal value 650 (65.0 °C).
2. WHEN the server starts, THE SYSTEM SHALL generate the traffic log by performing real Modbus TCP exchanges over loopback between the incident generator and a private, write-enabled simulator that no HTTP endpoint can reach.
3. THE SYSTEM SHALL produce a traffic log of 12 transactions: 11 FC03 reads of holding register 100 from client label `HMI-01`, and one FC06 write of value 950 to register 100 from a different client label, at a fixed position in the sequence.
4. THE SYSTEM SHALL make the traffic log deterministic: client labels, transaction IDs, simulated timestamps, request bytes and response bytes SHALL be identical on every server start.
5. THE SYSTEM SHALL record each traffic log entry's request and response bytes from what the private simulator actually received and sent, and SHALL decode them with the same decoder as the frame inspector.
6. IF the captured exchanges differ from the incident script (missing transaction, unexpected status, or final register value other than 950), THEN THE SYSTEM SHALL fail startup with an error rather than serve a partial or invented log.
7. WHEN traffic log generation finishes, THE SYSTEM SHALL dispose the private write-enabled simulator and SHALL serve learner reads for `phantom-writer` from a separate read-only simulator whose register 100 holds the final value observed in the captured log.
8. WHEN a learner reads unit 5, FC03, address 100, quantity 1 with scale 0.1 through `POST /api/test`, THE SYSTEM SHALL return raw register 950 and engineering value 95.0 with `unit: "°C"`.
9. WHEN the learner opens the traffic log, THE SYSTEM SHALL return it from `GET /api/incident/phantom-writer/log` with, per entry: sequence number, simulated time, client label, transaction ID, unit ID, function code and name, decoded address and value(s), and the request and response frames in the Requirement 1 format.
10. THE SYSTEM SHALL label the traffic log as a simulated capture and SHALL state that Modbus TCP frames carry no client identity and the client label is capture metadata.
11. WHEN `POST /api/incident/answer` receives exactly `scenario_id`, `transaction_id` and `client`, THE SYSTEM SHALL return `verified: true` only if both the transaction ID and the client label match the unauthorized write.
12. WHEN the answer is verified, THE SYSTEM SHALL return observations that cite the evidence from the captured log (function code 06, unit ID, register address 100, old value 650, new value 950, simulated time and client label) and a `mitigations` list labeled as real-world guidance not verified by this simulation, covering: network segmentation into IEC 62443 zones and conduits, allow-listing write function codes on a gateway or industrial firewall, Modbus/TCP Security (TLS with certificate-based roles), and change logging with review.
13. IF the answer is not verified, THEN THE SYSTEM SHALL return `verified: false`, an empty `mitigations` list and an observation describing what the captured log shows for the submitted transaction ID (or that no such transaction exists), and SHALL NOT reveal the correct pair.
14. IF the answer payload has missing, extra or duplicate fields, a `scenario_id` that is not a security case, a `transaction_id` outside 0–65535, or a `client` that is not 1–32 characters of `[A-Za-z0-9._-]`, THEN THE SYSTEM SHALL return HTTP 400 with `{ "error": "..." }`.
15. WHEN a learner views the traffic log before a verified answer, THE SYSTEM SHALL NOT mark any row as suspicious; WHEN the answer is verified, THE SYSTEM SHALL mark the offending row with the text "Unauthorized write" and an icon in addition to color, and the row SHALL be reachable by keyboard.

### Requirement 3: Categories, difficulty and case picker

**User Story:** As a learner, I want cases grouped by type and marked with difficulty, so that I can tell troubleshooting exercises from security investigations and choose where to start.

#### Acceptance Criteria

1. THE SYSTEM SHALL include `category` (`"troubleshooting"` or `"security"`) and `difficulty` (`"beginner"` or `"intermediate"`) in every entry returned by `GET /api/scenarios`.
2. THE SYSTEM SHALL assign category `troubleshooting` to `pressure-scaling`, `unit-id` and `register-addressing`, and category `security` to `phantom-writer`.
3. WHEN the case picker is rendered, THE SYSTEM SHALL group scenarios by category using labeled groups ("Troubleshooting", "Security incidents") and SHALL show each case's difficulty as text.
4. WHEN a `security` case is selected, THE SYSTEM SHALL show the traffic log panel and the answer form; WHEN a `troubleshooting` case is selected, THE SYSTEM SHALL hide them.

### Requirement 4: Property-based tests

**User Story:** As a maintainer, I want property-based tests for the codec, the transport and the diagnosis rules, so that edge cases are checked across many generated inputs and any failure can be reproduced.

#### Acceptance Criteria

1. THE SYSTEM SHALL implement property-based tests inside `csharp/GhostWire.Tests/Program.cs` (or files in that project) with no NuGet package, using a small seeded pseudo-random generator implemented in the harness.
2. THE SYSTEM SHALL print the base seed before running properties, SHALL accept an override through the `GHOSTWIRE_PBT_SEED` environment variable, and WHEN a property fails SHALL print the property name, the seed, the case index and the failing input before exiting non-zero.
3. THE SYSTEM SHALL run each property for at least 200 generated cases.
4. THE SYSTEM SHALL check property (a): for random valid inputs, encoding then decoding FC03, FC04 and FC06 requests and responses (including exception responses) returns the original field values.
5. THE SYSTEM SHALL check property (b): for random byte arrays of length 0–300, the decoder returns a result with either no `decode_error` or a non-null `decode_error`, and never throws.
6. THE SYSTEM SHALL check property (c): for random valid read requests against a loopback simulator, the status is exactly one of `response`, `exception` or `timeout`, and for `response` the number of returned registers equals the requested quantity.
7. THE SYSTEM SHALL check property (d): for every scenario and random request with status `timeout`, the diagnosis returns `verified: false`, at least two possible causes, at least one cause that does not mention the unit ID, and no statement asserting a single certain cause.
8. THE SYSTEM SHALL check property (e): for random valid read requests, the `frames` hex equals the bytes the simulator recorded as received (request) and sent (response).
9. WHEN all checks pass, THE SYSTEM SHALL keep printing `All GhostWire protocol and HTTP checks passed.`.

### Requirement 5: Safety boundaries

**User Story:** As the project owner, I want the new features to keep every existing safety boundary, so that the lab stays a loopback-only, read-only training tool.

#### Acceptance Criteria

1. THE SYSTEM SHALL NOT accept a host, port or URL in any request field, query string or path segment of any endpoint.
2. THE SYSTEM SHALL bind the HTTP server and every simulator, including the private incident simulator, to IPv4 loopback only.
3. THE SYSTEM SHALL keep `POST /api/test` limited to function codes 3 and 4, and `ModbusClient` SHALL refuse to build or send any other function code.
4. IF FC06 is sent to any learner-reachable simulator, THEN THE SYSTEM SHALL reply with exception code 01 and leave registers unchanged.
5. THE SYSTEM SHALL perform FC06 only in the incident generator at startup, only against its private simulator, and only with values from the fixed incident script.
6. WHEN an AI provider is configured, THE SYSTEM SHALL send it the structured evidence including `frames` (and, for answers, the evidence of the matched log entry), and THE SYSTEM SHALL compute `status`, `raw_registers`, `frames`, `verified` and `mitigations` before and independently of the provider call.
7. THE SYSTEM SHALL keep the existing limits: 16 KiB request bodies, 120 requests per minute, 8 concurrent tests (shared by `POST /api/test` and `POST /api/incident/answer`), 300 ms Modbus deadline per exchange, and 4 s and 64 KiB for the AI provider.
8. THE SYSTEM SHALL apply the existing Host, Origin, loopback-peer, content-type and size checks to the new endpoints.

### Requirement 6: Documentation

**User Story:** As a judge, I want a quick start that walks through all four cases, so that I can evaluate the lab in a few minutes without hardware or an AI key.

#### Acceptance Criteria

1. THE SYSTEM SHALL update the judge quick start in `README.md` and `docs/judge-quick-start.md` to cover The Giant Pressure, The Silent Sensor, The Wrong Register and Phantom Writer, including how to open Wire evidence.
2. THE SYSTEM SHALL document for Phantom Writer the initial read, opening the traffic log, the information the learner must identify, and the expected verified outcome, using only values produced by a simulator run.
3. THE SYSTEM SHALL update `csharp/README.md` with the `frames` field, `category`, `difficulty`, the incident log endpoint and the answer endpoint, including their validation rules.
4. THE SYSTEM SHALL state in the docs that the FC06 write happens only inside the server's private simulator at startup, that learners cannot send writes, and that mitigations are guidance not verified by the simulation.
5. THE SYSTEM SHALL update `docs/verification.md` with checks actually observed in a harness run, including the printed property seed.

### Requirement 7: Compatibility with existing cases

**User Story:** As an existing user of the three troubleshooting cases, I want them to behave as before, so that the current lessons, tests and screenshots stay valid.

#### Acceptance Criteria

1. THE SYSTEM SHALL keep the request schema of `POST /api/test` unchanged: exactly `scenario_id`, `unit_id`, `function_code`, `address`, `quantity` and `scale`, with the existing ranges.
2. THE SYSTEM SHALL keep every existing response field of `POST /api/test` and `GET /api/scenarios` with the same name, type and meaning; new fields SHALL be additive.
3. WHEN the existing initial and corrected requests for the three troubleshooting cases are sent, THE SYSTEM SHALL return the same `status`, `raw_registers`, `engineering_value`, `unit` and `verified` values as before this feature.
4. THE SYSTEM SHALL keep the existing harness checks passing, updating only the scenario count check from 3 to 4.
