# GhostWire: The AI Detective for Industrial Faults

GhostWire is a **simulation-only** Modbus troubleshooting lab. Inspect actual TCP responses from controlled virtual devices, separate observations from hypotheses, correct a request, and retest. The application does not connect to physical equipment, scan networks, or issue Modbus writes. Simulated results do not establish the safety or condition of a real installation; use device documentation, qualified human review, and independent protective systems.

## Lightweight Windows package

The framework-dependent Windows x64 package is generated locally with:

```powershell
dotnet publish csharp/GhostWire/GhostWire.csproj -c Release --self-contained false -o dist/GhostWire-Windows
```

Install the [.NET 8 ASP.NET Core Runtime for Windows x64](https://dotnet.microsoft.com/download/dotnet/8.0) on the target computer. Keep the entire output folder together, double-click `GhostWire.exe`, leave its console window open, and open <http://127.0.0.1:8000> in a browser. The browser does not open automatically. Close the console to stop it. The `.exe` alone is not a standalone package.

## Judge quick start

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0); no physical hardware, AI key, or third-party NuGet packages are needed. From the repository root:

```sh
dotnet run --project csharp/GhostWire/GhostWire.csproj
```

Open <http://127.0.0.1:8000>. Choose a case, inspect its simulated device documentation, and run the initial read:

1. **The Giant Pressure:** raw 39; change scale from `1` to documented `0.01` and retest for `0.39 bar`.
2. **The Silent Sensor:** Unit ID 1 times out; compare the simulator's documentation and retest with `17`. Timeout alone does not prove its cause.
3. **The Wrong Register:** address 21 returns an exception; the simulated zero-based map specifies address `20`.

The evidence panel separates observed results, hypotheses, training hints, next steps and verification. Previous-versus-current comparison and case history use completed reads; case history persists across case changes until page reload. **Print case report** uses the browser's print dialog. Stop the server with Ctrl+C.

## Architecture and safety

An ASP.NET Core C# server validates requests and communicates over real Modbus TCP with read-only simulators bound to loopback and ephemeral ports. The user cannot supply a target host or port. The Modbus implementation is a deliberately narrow .NET implementation of FC03 and FC04, not a maintained general-purpose industrial Modbus library. Mismatch behavior and register maps are specific to this simulator. No physical device, Modbus RTU, independent protocol interoperability, or functional-safety test is claimed.

The server listens on loopback only and is not a public production deployment. Public hosting requires a separate security and deployment review. See `csharp/README.md` for API limits and the local server contract.

## Optional AI explanation

Set `GHOSTWIRE_AI_BASE_URL`, `GHOSTWIRE_AI_MODEL`, and `GHOSTWIRE_AI_API_KEY` in the **server environment** before starting; `.env.example` lists names and is not automatically loaded. The base URL is an OpenAI-compatible API base, to which `/chat/completions` is appended. Structured simulated evidence is sent to the configured provider: review its terms and data handling before enabling it. Credentials remain server-side. If settings are absent or the provider fails, a labeled deterministic explanation is shown. Provider prose can be inaccurate and does not change the protocol result.

## Verify

```sh
dotnet build csharp/GhostWire/GhostWire.csproj
dotnet run --project csharp/GhostWire.Tests/GhostWire.Tests.csproj
```

The C# behavior harness exercises real local TCP, all three cases, input validation, request bounds, host/origin restrictions, and a local test provider with deterministic fallback. It does not establish an external provider call or physical-device behavior. Actual observed checks and limitations are in `docs/verification.md`.

## Competition and provenance

The public repository is <https://github.com/Wayan123/ghostwire-modbus-lab>. Local changes are not automatically present on GitHub until pushed. See `docs/bmad/competition-check.md`, `docs/kiro-usage.md`, `docs/existing-code-disclosure.md`, and `docs/video/storyboard.md`. The `.kiro` folder remains at repository root, but its presence does not prove Kiro use; genuine documented Kiro work is still needed. No public demo, final YouTube video, or Devpost submission is claimed. Apache-2.0 is the project license; see `LICENSE`.
