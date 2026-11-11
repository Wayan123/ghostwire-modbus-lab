# Implementation Plan: night-scanner

Run the harness after every task from the repository root: `tools/dotnet run --project csharp/GhostWire.Tests/GhostWire.Tests.csproj`. A task is done only when the harness prints `All GhostWire protocol and HTTP checks passed.` No NuGet packages. No commits.

- [x] 1. Multi-unit gateway simulator
  - Add the `devices` constructor to `ModbusSimulator`; the existing constructor delegates to it. Unknown unit IDs stay silent and are recorded with `Sent = null`.
  - Keep FC06 → exception 01 unless `allowWrites` is true.
  - _Requirements: 2.3, 5.2, 5.3_

- [x] 2. Generalise the incident capture
- [x] 2.1 Extract `CaptureAsync` from `IncidentLogGenerator.GenerateAsync`
  - Add optional `Unit` to `IncidentStep` and additive `exception_code` to `TrafficLogEntry`.
  - Match recorder exchanges to steps by remote port; record `timeout` only when the client deadline expired and the recorder confirms nothing was sent; record `exception` with its code.
  - Phantom Writer keeps its script, 300 ms step deadline and validation; its existing checks still pass.
  - _Requirements: 2.4, 2.6, 2.10, 3.7_
- [x] 2.2 Add `NightScannerPlan`, `NightScannerScript` and `GenerateNightScannerAsync`
  - Default plan and `FromSeed`, plan validation, `Build` (interleaved by simulated time, HMI first on ties), `Devices`.
  - Private read-only gateway, 100 ms probe deadline, validation against the plan, disposal before return.
  - _Requirements: 2.1, 2.2, 2.3, 2.5, 2.7, 2.8, 2.9, 2.11, 5.3_

- [x] 3. Per-case answer rules
  - Add `Diagnosis.AnswerEntry` (PW: FC06 entry; NS: first entry of the only client addressing ≥ 3 unit IDs) and the Night Scanner branch of `ForAnswer`.
  - Add the four Night Scanner mitigations; choose mitigations per case; drive the existing redaction from `AnswerEntry`.
  - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 5.5_

- [x] 4. Wire the scenario into `TrainingService`
  - Add the `night-scanner` case and its documentation; store logs per case; generate both logs under one 5 s deadline; create the learner-facing gateway simulator.
  - Add the `incident` object (task, hints, flag) to `GET /api/scenarios`; add the Night Scanner read hint to `Diagnosis.ForRead`.
  - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 4.6, 5.1, 5.4_

- [x] 5. Harness checks
- [x] 5.1 Example checks for the scenario, learner read, FC06 rejection, default log, determinism, answers and 404s; update the scenario count to 5.
  - _Requirements: 6.1, 1.3, 1.4, 2.8, 3.3, 3.6_
- [x] 5.2 Property P-f: Night Scanner capture over seeded plans (≥ 48 cases, batches of 8)
  - _Requirements: 6.2, 6.3, 6.4_

- [x] 6. Frontend
  - Status column with text for response, timeout and exception (decoded code and name); "None" for empty values; frames row spans 10 columns.
  - Case-specific task text, hints and flag label from `scenario.incident`.
  - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.7_

- [x] 7. Documentation
  - README case table to five cases; `docs/judge-quick-start.md` Night Scanner steps; `csharp/README.md` API and generation notes; `docs/verification.md` with observed harness values.
  - _Requirements: 7.1, 7.2, 7.3_

- [x] 8. Browser check
  - Headless Windows Chrome via a temporary script outside the repository (deleted afterwards): picker group, Status column, hint 1 after three wrong answers, verified answer and flag, no console errors, no overflow at 360 px. Record results in `docs/verification.md`.
  - _Requirements: 4.1, 4.2, 4.4, 4.5, 4.7, 7.3_

- [x] 9. Fix the session 10 security review findings
  - Answer oracle: every unverified answer in both security cases returns one identical reply (observation "The submitted pair does not match the event this case asks for.", case-level cause and next step, no hint, no mitigations, `log_entry: null`). Remove the redaction and the "client label differs" branch. Progressive hints stay client-side.
  - Harness: for every log entry of both cases, (its transaction, a fake client) and (a fake transaction, its client) serialize to the same JSON as a reference unverified reply once `answer` is removed; plus one HTTP pair per case.
  - `CaptureAsync`: per-step deadline (300 ms for steps expected to reply, 100 ms for probes of absent units); a step deadline during connect or write is rethrown as `InvalidOperationException` naming the step.
  - Night Scanner read hint: probing many unit IDs yourself is the behavior this case teaches learners to detect.
  - _Requirements: 2.5, 3.6_
