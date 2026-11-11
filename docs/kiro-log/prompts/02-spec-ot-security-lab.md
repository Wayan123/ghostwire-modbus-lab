Create a Kiro feature spec named `ot-security-lab` in .kiro/specs/ot-security-lab/ (requirements.md, design.md, tasks.md) using Kiro's spec format: requirements as user stories with EARS acceptance criteria (WHEN/IF ... THE SYSTEM SHALL ...), design with architecture, components, data models, API contract changes, error handling and a testing strategy that names correctness properties for property-based tests, and tasks.md as a numbered checkbox plan where each task references the requirement IDs it satisfies.

Read first: .kiro/steering/*, README.md, csharp/README.md, csharp/GhostWire/*.cs, csharp/GhostWire.Tests/Program.cs, frontend/*, and the old spec .kiro/specs/ghostwire-mvp/*.

Scope of ot-security-lab (keep existing three cases working, unchanged API fields stay compatible):

R1 Frame inspector: every /api/test result includes `frames` = list of {direction: "request"|"response", hex, fields:[{name, value, bytes}]} decoded from the actual bytes sent to and received from the simulator (MBAP transaction id, protocol id, length, unit id, function code, start address, quantity / byte count, register values, or exception code). Timeouts show only the request frame. The UI shows the frames in a collapsible "Wire evidence" panel with byte-to-field highlighting, keyboard accessible.

R2 Phantom Writer incident case (`phantom-writer`, category "security"): a simulated boiler controller (unit 5) exposes holding register 100 = temperature setpoint (×0.1 °C, normal 650 = 65.0 °C). The case starts with a simulated captured traffic log (generated deterministically by the server from real loopback exchanges at startup, not hard-coded text) containing ~12 transactions from an HMI (polling FC03) and one unauthorized FC06 write from a different simulated client id setting register 100 to 950 (95.0 °C). The learner reads the setpoint (sees 950 → 95.0 °C), opens the traffic log, and must identify the offending transaction (transaction id) and the write's source client. Verification: POST /api/incident/answer {scenario_id, transaction_id, client} returns verified true only for the correct pair; the response explains the evidence (function 06, address, value, source) and lists real-world mitigations as guidance only (network segmentation per IEC 62443 zones/conduits, allow-listing write function codes on a gateway, Modbus/TCP Security with TLS, change logging). The learner's read path stays read-only: GhostWire's client never sends FC06; only the server-side incident generator writes to its own private simulator at startup.

R3 Category + guidance: scenarios carry `category` ("troubleshooting"|"security") and `difficulty`; the case picker groups them.

R4 Property-based tests in the existing dependency-free harness (no NuGet): a small seeded random generator; properties: (a) encode→decode round-trip for FC03/FC04/FC06 requests and responses for random valid inputs; (b) decoder never throws on random byte arrays (returns error status instead); (c) for every random valid request against the simulator, status is exactly one of response/exception/timeout and response register count equals requested quantity; (d) diagnosis never lists a single certain cause for a timeout; (e) frames hex equals the bytes actually transmitted. Each property runs ≥200 cases and prints the seed on failure.

R5 Safety unchanged: no user-supplied host/port, loopback only, learner path read-only, AI explanation receives the structured evidence including frames but cannot change results.

R6 Docs: judge quick start covers all four cases.

Do not write implementation code in this task. Do not commit. When done, print the task list.
