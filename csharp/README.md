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

- `GET /api/scenarios` returns `{ "scenarios": [...] }`; each entry contains `id`, `title`, `summary`, `initial` and `documentation`.
- `POST /api/test` accepts JSON with exactly `scenario_id`, `unit_id`, `function_code`, `address`, `quantity` and `scale`. Integer unit IDs range from 1–247; function is 3 or 4; address is 0–65535; quantity is 1–125 and must not extend beyond address 65535; scale must be finite and greater than zero, at most 1,000,000. No target host or port is accepted.
- A successful test returns `status` (`response`, `timeout`, `exception`, or `error`), `raw_registers`, nullable `engineering_value`, arrays `observations`, `possible_causes`, `training_hints`, and `next_steps`, boolean `verified`, echoed `request`, `explanation`, and `explanation_source`. Pressure results also contain `unit: "bar"`. HTTP errors return `{ "error": "..." }` where the application handles them.

Each scenario owns an ephemeral, loopback-only simulator. The client performs actual Modbus TCP FC03/FC04 reads; writes are rejected. A mismatched unit ID silently times out in this simulation, but a timeout alone never proves its cause. No real device access or scanning is supported. Host and Origin are restricted to the actual local HTTP origin. Requests are bounded to 16 KiB, 120 per minute and eight concurrent tests; excess traffic receives 429. The limits are local, in-memory and not intended as public-server defenses.

Optional advisory explanations use `GHOSTWIRE_AI_BASE_URL`, `GHOSTWIRE_AI_API_KEY` and `GHOSTWIRE_AI_MODEL`. The configured OpenAI-compatible service receives the simulated evidence at `<base>/chat/completions`; use this option only with a provider you trust. The server never returns the key, does not follow provider redirects, applies a four-second deadline and limits responses to 64 KiB and displayed text to 4,000 characters. Missing or failed provider configuration uses labeled deterministic text. Provider prose is advisory and may be inaccurate; only the structured protocol fields are observations.
