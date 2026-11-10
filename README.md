# GhostWire — The AI Detective for Industrial Faults

GhostWire is a **simulation-only** Modbus troubleshooting lab. Inspect actual TCP responses from controlled virtual devices, separate facts from hypotheses, correct a request and retest. It does not communicate with physical equipment, scan networks or issue Modbus write commands. A simulation result does not prove safety or correctness on a real installation; use device-specific documentation, qualified human review and independent protective systems.

## Judge quick start

Requires Python 3.10+; no third-party packages or hardware are needed.

```sh
python3 -m backend.app
```

Open http://127.0.0.1:8000. Press **Run simulated read** for each case, inspect its simulator documentation and evidence, then change the indicated setting and retest:

1. **The Giant Pressure:** raw 39; change scale from `1` to documented `0.01` and observe `0.39 bar`.
2. **The Silent Sensor:** initial Unit ID 1 times out; compare the simulator's documented ID and retest with `17`. A timeout by itself does not establish the cause.
3. **The Wrong Register:** initial address 21 returns an exception; the simulator map specifies zero-based address `20`.

The case history contains actual requests and results; Print report uses the browser's print dialog. Changing the selected case clears the displayed history. Stop the server with Ctrl+C.

## Architecture and limitations

The same-origin Python server validates input and communicates over real Modbus TCP with read-only simulators bound to IPv4 loopback and ephemeral ports. The user cannot choose a target host or port. A deterministic diagnostic layer constructs observations, possibilities, training hints, next steps and verification; optional AI can only provide advisory explanation text. The browser does not receive provider credentials.

The simulator/client is a deliberately small Python standard-library implementation of FC03 and FC04, not a maintained general-purpose Modbus package. Silent mismatch behavior and register maps are specific to this simulator. No physical device, Modbus RTU, write, independent protocol interoperability or functional-safety test is claimed. This loopback-only server is not a public production deployment; public hosting requires a security and deployment review.

## Optional AI provider

Set `GHOSTWIRE_AI_BASE_URL`, `GHOSTWIRE_AI_MODEL`, and `GHOSTWIRE_AI_API_KEY` in the **server environment** before starting. `.env.example` lists names; it is not automatically loaded. The base URL is an OpenAI-compatible API base such as `https://your-provider/v1`; `/chat/completions` is appended. Structured simulated evidence is sent to that provider. Check provider terms and data handling first. If settings are absent or a request fails, a clearly labeled deterministic explanation is shown. AI prose may be wrong; the protocol result and evidence board remain authoritative. Never put real credentials in source, screenshots or shared logs.

## Test

```sh
python3 -m unittest discover -s backend/tests -v
```

Tests cover local TCP reads and exceptions, mismatch timeout, write rejection, scenario diagnostics, input validation, browser-origin restriction and AI-provider/fallback behavior using a local test provider. A local stub is not evidence of a successful external provider call. See `docs/verification.md` for checks actually performed.

## Competition and provenance

See `docs/bmad/competition-check.md`, `docs/kiro-usage.md`, `docs/existing-code-disclosure.md`, and `docs/video/storyboard.md`. The root `.kiro` directory is intentionally not ignored. Its presence is not proof of Kiro use; the current Kiro authentication blocker is documented. No public GitHub repository, public demo, YouTube upload, or Devpost submission is claimed. Apache-2.0 is the initial selected license; see `LICENSE`.
