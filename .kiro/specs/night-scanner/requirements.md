# Requirements Document: night-scanner

## Introduction

This feature adds a second security incident case, **The Night Scanner**, to GhostWire. A simulated plant gateway routes the boiler controller at unit 5. At server start, the incident generator captures a simulated traffic log from real loopback exchanges: normal HMI polling of unit 5, interleaved with a reconnaissance sweep from a different client that reads one register at address 0 from unit IDs 1, 2, 3, … N in quick succession. Most sweep requests time out or receive an exception, and one undocumented unit answers. The learner identifies the scanning client label and the first transaction ID of the sweep.

The feature reuses the Phantom Writer infrastructure from `.kiro/specs/ot-security-lab`: `IncidentStep`, `IncidentLogGenerator`, `TrafficLog`, `GET /api/incident/{scenario_id}/log`, `POST /api/incident/answer`, the traffic log table and the answer form. It generalises them only where the new case needs it (multi-unit simulator, timeout and exception entries, per-case answer rules, mitigations and hints).

The steering files `.kiro/steering/{product,safety,tech,modbus-protocol,accessibility}.md` apply to every requirement. Existing behavior of the four current cases is unchanged unless a criterion below says otherwise.

## Glossary

- **Gateway simulator**: A `ModbusSimulator` that serves several unit IDs on one loopback port and stays silent for unit IDs with no device.
- **Scan** (or **sweep**): The sequence of FC03 reads from one client label to increasing unit IDs.
- **Scanning client**: The client label that sent the scan.
- **Answer entry**: The traffic log entry whose transaction ID and client label form the correct answer for a security case.
- **Plan**: The parameters of a Night Scanner capture (scan length, responding unit, timing, transaction IDs, client label). Production uses one fixed default plan; tests derive plans from seeds.
- **Probe deadline**: The bounded time the generator waits for a reply to one Night Scanner request.
- Other terms (MBAP, PDU, FC03, FC06, frame, traffic log, client label, evidence categories, property) are defined in `.kiro/specs/ot-security-lab/requirements.md`.

## Requirements

### Requirement 1: Night Scanner scenario

**User Story:** As a learner, I want a security case where the process value looks normal but the network capture shows reconnaissance, so that I learn to look for evidence beyond the process value.

#### Acceptance Criteria

1. THE SYSTEM SHALL provide a scenario with id `night-scanner`, title "The Night Scanner", category `security` and difficulty `intermediate`.
2. THE SYSTEM SHALL document the simulated gateway as routing the boiler controller at unit 5, holding register offset 100, 0.1 °C per count, documented normal value 650 (65.0 °C), polled by client label `HMI-01`.
3. WHEN a learner reads unit 5, FC03, address 100, quantity 1, scale 0.1 through `POST /api/test` for `night-scanner`, THE SYSTEM SHALL return the value read from a learner-facing, read-only gateway simulator, which is raw 650 and engineering value 65.0 °C for the default plan.
4. THE SYSTEM SHALL serve learner reads for `night-scanner` from a read-only gateway simulator with the same device map as the capture, so a learner read of any unit ID gives the same status as the scan entry for that unit ID.
5. WHEN a `night-scanner` read result is returned, THE SYSTEM SHALL include a training hint stating that a normal read does not show which other clients queried the gateway, and pointing to the simulated traffic log.

### Requirement 2: Captured scan traffic

**User Story:** As a learner, I want the scan in the log to come from real exchanges, including real timeouts, so that the evidence I investigate is what a simulator actually did.

#### Acceptance Criteria

1. WHEN the server starts, THE SYSTEM SHALL generate the Night Scanner traffic log by performing real Modbus TCP exchanges over loopback against a private gateway simulator that no HTTP endpoint can reach, and SHALL dispose that simulator before the server accepts HTTP requests.
2. THE SYSTEM SHALL generate only FC03 reads for the Night Scanner capture: polls of unit 5, register 100, quantity 1 from `HMI-01`, and scan reads of address 0, quantity 1 to unit IDs 1 to N from one other client label, where 5 ≤ N ≤ 12.
3. THE SYSTEM SHALL configure the private gateway with exactly two devices: unit 5 (register 100 only) and one responding unit U in 1–N other than 5 (register 0 only), so that during the scan unit U returns a response, unit 5 returns exception 02 and every other unit ID gets no reply.
4. WHEN a request gets no reply within the probe deadline, THE SYSTEM SHALL record the entry with status `timeout`, only the request frame, and an empty `values` array; THE SYSTEM SHALL record it as `timeout` only if the generator's deadline expired and the simulator's recorder confirms that it sent nothing.
5. THE SYSTEM SHALL bound every Night Scanner exchange that is expected to get a reply with the 300 ms Modbus deadline, and every probe of a unit ID with no device with a probe deadline of 100 ms, and SHALL keep the existing 5 s overall startup deadline for all incident generation.
6. WHEN a request receives an exception reply, THE SYSTEM SHALL record the entry with status `exception`, the decoded `exception_code`, both frames and an empty `values` array.
7. THE SYSTEM SHALL interleave HMI polls (every 5 simulated seconds) with scan requests (every 1 simulated second) by simulated time, with HMI polls first when two entries share a second.
8. THE SYSTEM SHALL make the production capture deterministic: the default plan yields identical log JSON on every start.
9. IF the captured exchanges differ from the plan (missing entry, status other than expected, value other than configured, client-observed status disagreeing with the simulator recorder, or a scan that is not detected as exactly one scanning client), THEN THE SYSTEM SHALL fail startup with an error.
10. THE SYSTEM SHALL serve the log from `GET /api/incident/night-scanner/log` in the existing log format, with an additive `exception_code` field (integer or null) on every entry of every log.
11. THE SYSTEM SHALL state in the capture note that the capture is simulated, that Modbus TCP frames carry no client identity, and that requests with no reply within 100 ms are logged as timeouts.

### Requirement 3: Answer verification

**User Story:** As a learner, I want to submit the scanning client and the first transaction of the scan and get evidence-based feedback, so that I practice identifying reconnaissance from a capture.

#### Acceptance Criteria

1. WHEN `POST /api/incident/answer` receives `scenario_id: "night-scanner"`, THE SYSTEM SHALL use the existing request shape and validation (`scenario_id`, `transaction_id`, `client`).
2. THE SYSTEM SHALL determine the answer entry from the captured log alone: the scanning client is the only client label whose entries address at least 3 distinct unit IDs, and the answer entry is that client's first entry.
3. THE SYSTEM SHALL return `verified: true` only when the submitted transaction ID equals the answer entry's transaction ID and the submitted client label matches its label (ordinal, case-insensitive, trimmed).
4. WHEN the answer is verified, THE SYSTEM SHALL return observations computed from the log that cite: the scanning client label, the number of scan requests, the first and last unit ID in increasing order, the first and last transaction ID and time, the shortest and longest interval between its requests, the count of timeouts, exceptions and responses, the unit ID that responded, and the unit IDs other clients addressed.
5. WHEN the answer is verified, THE SYSTEM SHALL return four `mitigations`, each starting "Real-world guidance, not verified by this simulation", covering: asset inventory and a baseline of which clients poll which units; IDS rules that alert on Modbus unit-ID sweeps; restricting which hosts can reach TCP port 502; and logging at the gateway.
6. IF the answer is not verified, THEN THE SYSTEM SHALL return `verified: false`, an empty `mitigations` list, an observation about what the log shows for the submitted transaction ID (or that it does not exist), and SHALL NOT reveal the correct pair; WHEN the submitted transaction is the answer entry but the client is wrong, `log_entry.client` SHALL be redacted as for Phantom Writer.
7. THE SYSTEM SHALL keep the Phantom Writer answer rules, observations and mitigations unchanged.

### Requirement 4: Learner interface

**User Story:** As a learner, I want to read timeouts and exceptions directly in the log and get hints specific to the case, so that I can find the scan without decoding every frame.

#### Acceptance Criteria

1. WHEN the case picker is rendered, THE SYSTEM SHALL list "The Night Scanner · intermediate" in the "Security incidents" group.
2. THE SYSTEM SHALL add a "Status" column to the traffic log table that shows, as text, "Response", "Timeout (no reply)" or "Exception" followed by the decoded exception code and name from the response frame.
3. THE SYSTEM SHALL show case-specific task text above the log for each security case.
4. WHEN 3 or more answers for a security case are not verified and none is verified, THE SYSTEM SHALL show that case's hint 1; from the 4th, also hint 2. For `night-scanner` hint 1 SHALL be "Look at the Unit ID column: does any client ask many different units?" and hint 2 "Find the first request in that run." Phantom Writer keeps its current hints.
5. WHEN a Night Scanner answer is verified, THE SYSTEM SHALL mark the answer entry's row with the text "⚠ First scan request" and the existing pattern, keep it keyboard reachable, and SHALL NOT mark any row before that.
6. THE SYSTEM SHALL serve the per-case task text, hints and row flag label from `GET /api/scenarios` as an additive `incident` field (null for troubleshooting cases).
7. WHEN the viewport is 360 CSS pixels wide, THE SYSTEM SHALL not overflow the document horizontally; the log scrolls inside its labeled region.

### Requirement 5: Safety boundaries

**User Story:** As the project owner, I want the new case to keep every safety boundary, so that GhostWire stays a loopback-only, read-only training tool.

#### Acceptance Criteria

1. THE SYSTEM SHALL NOT add any request field, query string, path segment or environment variable that selects a host, port or URL.
2. THE SYSTEM SHALL bind the private and learner-facing gateway simulators to IPv4 loopback on ephemeral ports.
3. THE SYSTEM SHALL send no FC06 or other write in the Night Scanner generator, and every learner-reachable simulator SHALL keep answering FC06 with exception 01.
4. THE SYSTEM SHALL keep the existing HTTP limits, Host, Origin, content-type and size checks for all endpoints, and the 300 ms deadline for learner reads.
5. THE SYSTEM SHALL label mitigations as guidance not verified by this simulation, and SHALL NOT claim the capture says anything about a physical installation.

### Requirement 6: Tests

**User Story:** As a maintainer, I want example checks and a seeded property for the new capture, so that the scan log is correct for the default plan and for many generated plans.

#### Acceptance Criteria

1. THE SYSTEM SHALL add example checks for: the scenario entry, the learner read, FC06 rejection by the learner-facing gateway, the default log (counts, statuses, unit order, frames re-decoding, determinism), the correct answer, wrong answers, and the 404 behavior.
2. THE SYSTEM SHALL add a property in the existing harness, with the printed base seed, that for generated plans checks: the scanning client's unit IDs are strictly increasing, the capture has exactly one response among scan requests, every non-responding unit other than 5 is a timeout, and exactly one (transaction ID, client label) pair from the log verifies.
3. THE SYSTEM SHALL run that property for at least 48 cases, in concurrent batches, because each case performs real loopback exchanges including up to 11 timeouts.
4. WHEN all checks pass, THE SYSTEM SHALL keep printing `All GhostWire protocol and HTTP checks passed.`

### Requirement 7: Documentation

**User Story:** As a judge, I want the docs to cover all five cases with observed values.

#### Acceptance Criteria

1. THE SYSTEM SHALL update the case table in `README.md` from four to five cases, and `docs/judge-quick-start.md` with the Night Scanner steps, using only values from a simulator run.
2. THE SYSTEM SHALL update `csharp/README.md` for the new scenario, the `exception_code` and `incident` fields, and the Night Scanner generation.
3. THE SYSTEM SHALL record the observed harness and browser results in `docs/verification.md`.
