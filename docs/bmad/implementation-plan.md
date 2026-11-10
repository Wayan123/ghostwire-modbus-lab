# GhostWire implementation plan

## Product and boundaries
Interactive, simulation-only Modbus troubleshooting: observe → inspect evidence → change one setting → retest. Three scenarios: pressure scaling, silent unit ID and register addressing. Distinguish actual protocol outcomes, hypotheses, training hints and verification. No physical-device access or Modbus writes.

## Architecture
A Python API owns deterministic scenario definitions and starts a loopback-only Modbus TCP simulator; each read targets that controlled simulator, never a user-supplied network destination. A browser UI calls same-origin endpoints, presents settings and case history, and labels every measurement simulated. Optional server-side OpenAI-compatible explanation receives structured evidence; unavailable provider yields a labeled deterministic explanation. Secrets remain server-side.

## Contract between implementation slices
- GET `/api/scenarios` returns `{scenarios:[{id,title,summary,initial,documentation}]}`; initial has unit_id, function_code, address, quantity, scale.
- POST `/api/test` accepts `{scenario_id,unit_id,function_code,address,quantity,scale}`; returns `{status,raw_registers,engineering_value,observations,possible_causes,training_hints,next_steps,verified,explanation,explanation_source,request}`. String lists identify evidence categories; `request` echoes validated configuration. `status` is `response`, `timeout`, `exception`, or `error`.
- Browser holds current case history locally; selecting a scenario resets it. No external addresses in API input.
- Server serves frontend from project-local frontend directory.

## Milestones
1. Implement real loopback Modbus server/client and tests for FC03/FC04, invalid address, unit timeout, and 39 × 0.01 = 0.39.
2. Implement case workspace and history against the contract; ensure keyboard and mobile usability.
3. Integrate optional AI and deterministic fallback; test unavailable provider and source labeling.
4. Document reproducible setup, Kiro use actually performed, pre-existing work, contest evidence, Devpost story and narrated video production.
5. Exercise all three paths against actual UI and protocol server; record observed results only.

## Readiness and constraints
- Original plan PDF stays untouched. No copied code from existing industrial projects.
- Official rules require genuine Kiro use, public GitHub with root `.kiro`, a working application/test-build URL and a public YouTube demo. No external publication without explicit authorization.
- Submission form lists Indonesia among excluded jurisdictions, while official rules retrieved 2026-11-10 do not list it explicitly. Organizer clarification required; do not assert eligibility.
- Use existing installed dependencies or perform the mandatory sysinv ownership checks before installing any package, runtime, or environment. Never expose credentials.
- Only claim tested versions and checks actually observed. No claims of physical validation or safety certification.
