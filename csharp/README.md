# GhostWire C# local server

Requires the .NET 8 SDK and runtime. From the repository root:

```powershell
dotnet run --project csharp/GhostWire/GhostWire.csproj
```

Open http://127.0.0.1:8000. Set `GHOSTWIRE_PORT` to a port from 1 to 65535 to change the HTTP listener. The server binds IPv4 loopback only. The build copies the repository's `frontend` directory next to the application; the same copied assets are served when starting the built DLL from another working directory:

```powershell
dotnet csharp/GhostWire/bin/Debug/net8.0/GhostWire.dll
```

The standalone, dependency-free behavior harness starts real ephemeral TCP listeners and an HTTP server:

```powershell
dotnet run --project csharp/GhostWire.Tests/GhostWire.Tests.csproj
```

## API

- `GET /api/scenarios` returns `{ "scenarios": [...] }`; each entry contains `id`, `title`, `summary`, `initial`, `documentation`, `category` (`troubleshooting` or `security`) and `difficulty` (`beginner` or `intermediate`). `phantom-writer` is the only `security` case.
- `POST /api/test` accepts JSON with exactly `scenario_id`, `unit_id`, `function_code`, `address`, `quantity` and `scale`. Integer unit IDs range from 1–247; function is 3 or 4; address is 0–65535; quantity is 1–125 and must not extend beyond address 65535; scale must be finite and greater than zero, at most 1,000,000. No target host or port is accepted.
- A successful test returns `status` (`response`, `timeout`, `exception`, or `error`), `raw_registers`, nullable `engineering_value`, arrays `observations`, `possible_causes`, `training_hints`, and `next_steps`, boolean `verified`, echoed `request`, `frames`, `explanation`, and `explanation_source`. Pressure results also contain `unit: "bar"`; Phantom Writer results contain `unit: "°C"`. HTTP errors return `{ "error": "..." }` where the application handles them.
- `frames` is built from the bytes actually written to and read from the loopback socket: `[request, response]` when response bytes arrived, `[request]` on a timeout, and `[]` when the request was never fully written. Each frame is `{ "direction": "request" | "response", "hex": "00 01 …", "fields": [ { "name", "value", "bytes": { "offset", "length" } } ], "decode_error": string | null }`. Fields are listed in byte order (transaction ID, protocol ID, length, unit ID, function code, then the function's data). A malformed frame returns the fields decoded so far and a non-null `decode_error`.
- `GET /api/incident/{scenario_id}/log` returns the Phantom Writer traffic log for `phantom-writer` and 404 `{ "error": "Not found" }` for any other id. The id is matched against the fixed list of security cases and is never used to build a path, host or URL. The body is `{ "scenario_id", "simulated": true, "capture_note", "entries": [...] }`; each entry has `sequence`, `time` (simulated), `client` (label), `transaction_id`, `unit_id`, `function_code`, `function_name`, `address`, `values`, `status` and `frames` (same format as above). `unit_id`, `function_code`, `address` and `values` are decoded from the captured bytes. The log is identical on every start.
- `POST /api/incident/answer` accepts JSON with exactly `scenario_id` (a security case), `transaction_id` (integer 0–65535) and `client` (1–32 characters of `A-Z a-z 0-9 . _ -`). Missing, extra or duplicate fields, a non-security `scenario_id`, out-of-range or wrong-type values return 400; a non-JSON content type returns 415. The response contains `status: "answered"`, `verified`, echoed `answer`, `observations`, `possible_causes`, `training_hints`, `next_steps`, `mitigations`, `log_entry` (the entry for the submitted transaction, or null), `explanation` and `explanation_source`. `verified` is true only when both the transaction ID and the client label (case-insensitive) match the captured FC06 entry. `mitigations` is non-empty only when verified; each item is labeled as real-world guidance not verified by this simulation. An unverified answer never reveals the correct pair; for the write entry, `log_entry.client` is redacted.

Each scenario owns an ephemeral, loopback-only simulator. The client performs actual Modbus TCP FC03/FC04 reads and refuses to build any other function code; learner-reachable simulators answer FC06 with exception 01. A mismatched unit ID silently times out in this simulation, but a timeout alone never proves its cause. No real device access or scanning is supported. Host and Origin are restricted to the actual local HTTP origin. Requests are bounded to 16 KiB, 120 per minute and eight concurrent tests (shared by `/api/test` and `/api/incident/answer`); excess traffic receives 429. The limits are local, in-memory and not intended as public-server defenses.

### Phantom Writer generation

At startup, before HTTP requests are accepted, `IncidentLogGenerator` runs a fixed 12-step script (11 FC03 reads from `HMI-01`, one FC06 write of 950 to register 100 from `MAINT-LAPTOP-07`, transaction 1111) against a private, write-enabled simulator on loopback, one TCP connection per step with the 300 ms Modbus deadline and a 5 s overall deadline. The simulator's recorder captures the bytes it received and sent; the generator decodes them with the same codec as the frame inspector, checks them against the script, and disposes the private simulator. Any mismatch stops startup. The learner-facing `phantom-writer` simulator is read-only and holds the final captured value of register 100. This is the only place FC06 is sent, and its values come only from the script.

Optional advisory explanations use `GHOSTWIRE_AI_BASE_URL`, `GHOSTWIRE_AI_API_KEY` and `GHOSTWIRE_AI_MODEL`. The configured OpenAI-compatible service receives the simulated evidence, including `frames` and, for answers, the matched log entry, at `<base>/chat/completions`; use this option only with a provider you trust. The server never returns the key, does not follow provider redirects, applies a four-second deadline and limits responses to 64 KiB and displayed text to 4,000 characters. Missing or failed provider configuration uses labeled deterministic text. Provider prose is advisory and may be inaccurate; only the structured protocol fields are observations. `status`, `raw_registers`, `frames`, `verified` and `mitigations` are computed before the provider call and are not changed by it.
