# GhostWire C# migration plan

## Decision
Retain the web experience requested in the original brief; replace the Python backend with ASP.NET Core on .NET 8. A native Windows desktop application is not implied by C# and is not the selected target. Use the installed Windows SDK 8.0.427, no extra installations. Retain the established `/api/scenarios` and `/api/test` JSON contract, with same-origin UI.

## Invariants
- Three existing scenarios and documented initial/corrected settings remain reproducible. Real local Modbus TCP request/response; FC03 and FC04 only; mismatched Unit ID silently times out in this simulator; no physical hardware or arbitrary client-selected destination.
- Evidence separates observed request/response, possibilities, simulator-specific hints, recommended action and verified result. Preserve raw values; scale 39 × 0.01 = 0.39 bar. Timeout alone is never proof.
- Provider API key stays server-side; optional OpenAI-compatible explanation and labeled deterministic fallback. Bound inputs, timeouts, and requests. No public deployment by default.
- Frontend remains responsive and keyboard-operable, with accessible empty/loading/error states, clear step-by-step guidance, case history and report.
- Update setup and test commands; remove superseded Python backend and obsolete claims only after the C# implementation is verified. No new dependency downloads without sysinv policy. Kiro and contest eligibility remain unverified.

## Interface direction
The owner's Haunted Factory Lab brief calls for an industrial dark surface, orange investigative actions, green verified status, red errors and text labels. The design uses a clear setup-to-evidence layout for beginners, restrained system typography for readable technical data, and minimal motion so diagnostic state remains the focus. ENERGY 2 / RHYTHM 2 / MOTION 1.

## Work and verification
1. C# simulator, API, deterministic diagnostics, provider adapter and consumer-visible protocol/HTTP tests using installed SDK, without external dependencies.
2. Improve interface hierarchy, case guidance and error handling without changing API contract.
3. Build and run C# program; exercise three cases, invalid requests, origin restrictions, provider fallback and a browser interaction at desktop and mobile widths. Document actual checks and limitations.
4. Migrate documentation and ignore generated build files; preserve original attribution and historic footage as historical, not current evidence.
