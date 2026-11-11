# Implementation Plan: ot-security-lab

Run the harness after every task from the repository root: `tools/dotnet run --project csharp/GhostWire.Tests/GhostWire.Tests.csproj`. A task is done only when the harness prints `All GhostWire protocol and HTTP checks passed.` No NuGet packages. No commits unless the owner asks.

- [x] 1. Add the pure Modbus codec
  - Add `ModbusCodec`, `DecodedFrame`, `FrameField`, `ByteRange`, `Mbap` and `ParsedAdu` to `csharp/GhostWire/Modbus.cs`.
  - Implement encoders for FC03/FC04 requests and responses, FC06 request and echo response, and exception responses, with argument validation.
  - Implement `Decode` / `TryParse` that never throw, walk fields in byte order, name register fields by absolute offset when the request address is known, and set `decode_error` at the first failure.
  - Implement `Hex` (uppercase, space-separated).
  - Add example checks: the Giant Pressure request and response bytes from `modbus-protocol.md` decode to the expected fields; `84 02` decodes as exception 02.
  - _Requirements: 1.4, 1.6, 1.7_

- [x] 2. Add the property-based test runner and codec properties
- [x] 2.1 Add the seeded generator and runner to the harness
  - Implement SplitMix64 `Rng` with `NextInt`, `NextBytes`, `Pick`.
  - Implement `Property(name, cases, body)`: fixed default seed, `GHOSTWIRE_PBT_SEED` override, `PBT seed: <seed>` printed at start, per-property seed `baseSeed ^ FNV1a(name)`, failure message with property name, seed, case index and input, elapsed time per property.
  - _Requirements: 4.1, 4.2, 4.3_
- [x] 2.2 Property P-a: codec round-trip for FC03, FC04 and FC06 requests, responses and exceptions (≥200 cases)
  - _Requirements: 4.3, 4.4, 1.6_
- [x] 2.3 Property P-b: decoder never throws on random byte arrays of length 0–300, in both directions (≥200 cases)
  - _Requirements: 4.3, 4.5, 1.7_

- [x] 3. Capture wire bytes on the client and the simulator
- [x] 3.1 Return exchanged bytes from `ModbusClient`
  - Add `ModbusExchange(Sent, Received)` to `ModbusResult`; set `Sent` only after the write completes; accumulate `Received` from the header and PDU reads.
  - Build the request with `ModbusCodec.EncodeReadRequest`.
  - Throw `ArgumentOutOfRangeException` before opening a socket when the function is not 3 or 4.
  - Add checks: a function-6 call throws and the simulator sees no exchange.
  - _Requirements: 1.3, 1.5, 5.3_
- [x] 3.2 Add `IExchangeRecorder` and the `allowWrites` option to `ModbusSimulator`
  - Record `(RemotePort, Received, Sent)` once per handled request; swallow recorder exceptions.
  - With `allowWrites: false` (default), FC06 still returns exception 01. With `allowWrites: true`, FC06 writes an existing holding register under a lock and echoes the request PDU; a missing address returns exception 02.
  - Keep the existing raw TCP check "writes must be rejected" unchanged.
  - _Requirements: 5.2, 5.4, 2.2_

- [x] 4. Add `frames` to `/api/test` results
  - Build frames from `ModbusResult.Exchange` with `ModbusCodec.Decode`: request plus response when bytes were received, request only on timeout, empty when nothing was sent.
  - Add `frames` to the result before calling `ExplainAsync`.
  - Add checks: Giant Pressure request and response hex match the documented example; Silent Sensor initial read has exactly one request frame; Wrong Register response decodes exception 02.
  - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 5.6, 7.2_
- [x] 4.1 Property P-e: frames hex equals the bytes the simulator recorded as received and sent (≥200 cases)
  - Use a harness simulator with a recorder and the same frame-building function as `TrainingService`.
  - _Requirements: 4.3, 4.8, 1.5_
- [x] 4.2 Property P-c: for random valid reads, status is exactly one of response, exception or timeout, and response register count equals quantity (≥200 cases, batches of 8)
  - _Requirements: 4.3, 4.6_

- [x] 5. Extract the diagnosis rules into a pure `Diagnosis` class
  - Move observations, causes, hints, next steps and `verified` from `TrainingService.TestAsync` into `Diagnosis.ForRead` with identical text for the three existing cases.
  - Confirm all existing case checks still pass with unchanged `status`, `raw_registers`, `engineering_value`, `unit` and `verified`.
  - _Requirements: 7.2, 7.3, 7.4_
- [x] 5.1 Property P-d: a timeout diagnosis never claims a single certain cause, for all scenarios and random requests (≥200 cases)
  - _Requirements: 4.3, 4.7_

- [x] 6. Add category and difficulty to scenarios
  - Extend `Scenario` with `category`, `difficulty` and `engineeringUnit`; return `category` and `difficulty` from `GET /api/scenarios`.
  - Assign `troubleshooting` to the three existing cases and the difficulties from the design.
  - Add checks for each entry's category and difficulty and that existing fields are still present.
  - _Requirements: 3.1, 3.2, 7.2_

- [x] 7. Generate the Phantom Writer traffic log at startup
- [x] 7.1 Add `IncidentScript` and `IncidentLogGenerator` in `csharp/GhostWire/Incident.cs`
  - Script: 11 FC03 reads of register 100 by `HMI-01` (transaction IDs 1–11) and one FC06 write of 950 by `MAINT-LAPTOP-07` (transaction ID 1111) at sequence 8, with fixed simulated timestamps.
  - Run each step on a new loopback connection to a private `allowWrites: true` simulator (unit 5, register 100 = 650), map local ports to client labels, record through `IExchangeRecorder`, decode with `ModbusCodec`.
  - Validate the capture against the script (12 entries, 650 before the write, FC06 echo, 950 after) and throw on any mismatch; dispose the private simulator; return the log and the captured final setpoint.
  - _Requirements: 2.2, 2.3, 2.4, 2.5, 2.6, 5.2, 5.5_
- [x] 7.2 Wire the incident into `TrainingService` and the host
  - Add the `phantom-writer` scenario (category `security`, difficulty `intermediate`, unit 5, FC03, address 100, scale 0.1, unit `°C`) with the documentation from the design.
  - Run the generator in the constructor with a 5 s overall deadline; create the read-only `phantom-writer` simulator seeded with the captured final value.
  - Resolve `TrainingService` eagerly in `GhostWireHost.Create` so generator failures stop startup.
  - Add the Phantom Writer training hint to `Diagnosis.ForRead`.
  - Add checks: the read returns raw 950, engineering value 95.0 and unit `°C`; FC06 sent by raw TCP to the learner-facing simulator returns `86 01` and register 100 still reads 950; update the scenario count check to 4.
  - _Requirements: 2.1, 2.7, 2.8, 5.4, 7.1, 7.4_

- [x] 8. Add the incident HTTP endpoints
- [x] 8.1 `GET /api/incident/{scenario_id}/log`
  - Return the log DTO with the capture note; 404 for any id that is not a security case.
  - Add checks: 12 entries, unique transaction IDs, exactly one FC06 entry, two client labels, every frame re-decodes to the entry's fields, identical JSON from two independent `TrainingService` instances, 404 for `unit-id` and for a path-traversal id.
  - _Requirements: 2.4, 2.5, 2.9, 2.10, 5.1, 5.8_
- [x] 8.2 `POST /api/incident/answer`
  - Extract the `/api/test` body reader into a shared helper; reuse content-type, size, JSON and 8-slot concurrency handling.
  - Implement `ValidateAnswer` with the exact field whitelist and ranges, and `Diagnosis.ForAnswer` with the verified, wrong-client, wrong-transaction and missing-transaction branches; attach the four labeled mitigations only when verified; then call `ExplainAsync`.
  - Add checks: the correct pair verifies with four mitigations; each wrong variant returns `verified: false`, empty mitigations and no unrevealed client label; the invalid payloads from the design return 400 or 415.
  - _Requirements: 2.11, 2.12, 2.13, 2.14, 5.1, 5.7, 5.8_
- [x] 8.3 Confirm the AI provider cannot change incident or frame results
  - Extend the local test-provider checks: the provider request body contains `frames`; provider text leaves `verified`, `frames` and `mitigations` unchanged for both `/api/test` and `/api/incident/answer`.
  - _Requirements: 5.6_

- [x] 9. Checkpoint: backend complete
  - Run the build and the harness. Confirm all properties ran ≥200 cases and the seed was printed. Ask the owner before continuing if any question about the API contract came up.
  - _Requirements: 4.2, 4.3, 4.9, 7.4_

- [x] 10. Frontend: grouped case picker
  - Render `<optgroup>` groups "Troubleshooting" and "Security incidents" with difficulty in the option text.
  - Show the traffic log panel and answer form only for security cases.
  - _Requirements: 3.3, 3.4_

- [x] 11. Frontend: Wire evidence panel
  - Add a collapsed `<details>` "Wire evidence (simulated frames)" in the result, with plain-language definitions of MBAP header, PDU, unit ID and function code.
  - Render each frame as monospace hex byte spans plus a `<table>` with header cells; focusable rows that highlight their byte range on hover and focus and expose "bytes N–M" as text; show `decode_error` as text.
  - Use `textContent` only; respect `prefers-reduced-motion`; add print styles.
  - _Requirements: 1.8, 1.9, 1.10, 1.11_

- [x] 12. Frontend: traffic log and answer form
  - Fetch and render the log as a `<table>` with column headers, a per-row "Select" button that fills the answer form, and per-row frames using the Wire evidence renderer; show the capture note.
  - Add the answer form with client-side constraints matching the server, render the answer evidence and the "Next steps · real-world guidance, not verified by this simulation" list, and announce results through the existing `role="status"` region.
  - Mark the offending row with "⚠ Unauthorized write" text and a pattern only after a verified answer; keep it keyboard reachable.
  - Add answer attempts to case history and the print report.
  - _Requirements: 2.9, 2.10, 2.12, 2.13, 2.15_

- [x] 13. Documentation
  - Update the judge quick start in `README.md` and `docs/judge-quick-start.md` for all four cases, including opening Wire evidence and the Phantom Writer steps, using values from an actual harness or server run.
  - Update `csharp/README.md` with `frames`, `category`, `difficulty`, the log endpoint and the answer endpoint with validation rules.
  - State that FC06 happens only in the server's private simulator at startup, that learners cannot send writes, and that mitigations are guidance not verified by the simulation.
  - Update `docs/verification.md` with the checks actually observed, including the printed PBT seed.
  - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5_

- [x] 14. Final verification and safety review
  - Run the build and the harness; record the output.
  - Perform the keyboard-only, reduced-motion and print manual checks from the design and record what was checked.
  - Ask the `ot-security-reviewer` agent to review the diff against `safety.md`; resolve findings.
  - Re-read every acceptance criterion and mark any that were not verified.
  - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7, 5.8, 7.3, 7.4_
