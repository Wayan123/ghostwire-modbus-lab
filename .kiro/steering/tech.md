---
inclusion: always
---

# Technology

## Stack and layout

- ASP.NET Core minimal API in C# on .NET 8: `csharp/GhostWire/` (`Program.cs` host and HTTP limits, `Modbus.cs` simulator and client, `Training.cs` scenarios, validation, diagnosis and optional AI explanation).
- Test harness: `csharp/GhostWire.Tests/Program.cs`, a dependency-free console program that uses real loopback TCP and HTTP. A failed `Check` throws and exits non-zero.
- Frontend: plain HTML, CSS and JavaScript in `frontend/`, with no build tool and no framework. The build copies it next to the server.
- No third-party NuGet packages. Adding one (for example a property-testing library) needs the owner's approval first.

## Commands

The repository lives on `/mnt/d` (a Windows drive under WSL), so `tools/dotnet` wraps the Windows `dotnet.exe`. Always call `tools/dotnet` from the repository root, never a bare `dotnet`, in agent shells and hooks.

- Test: `tools/dotnet run --project csharp/GhostWire.Tests/GhostWire.Tests.csproj` (prints `All GhostWire protocol and HTTP checks passed.` on success).
- Build: `tools/dotnet build csharp/GhostWire/GhostWire.csproj -v q -nologo`
- Run: `tools/dotnet run --project csharp/GhostWire/GhostWire.csproj`, then open http://127.0.0.1:8000.

## Rules for code

- The HTTP server binds IPv4 loopback only. Simulators bind `IPAddress.Loopback` on ephemeral ports.
- The client connects only to `IPAddress.Loopback` and a port owned by a `TrainingService` simulator. No request field may select a host, port or URL.
- `POST /api/test` accepts exactly `scenario_id`, `unit_id` (1–247), `function_code` (3 or 4), `address` (0–65535), `quantity` (1–125, within 65535) and `scale` (finite, > 0, ≤ 1,000,000). Keep this strict whitelist when adding fields.
- Keep structured evidence (`status`, `raw_registers`, `observations`, `possible_causes`, `training_hints`, `next_steps`, `verified`) separate from `explanation`. The frame inspector adds raw request and response bytes as structured fields, not prose.
- Keep the existing limits: 16 KiB bodies, 120 requests per minute, 8 concurrent tests, 300 ms Modbus deadline, 4 s and 64 KiB for the AI provider.
- Property-based tests go in the existing harness with a fixed, printed seed so failures can be reproduced.
- This is a narrow training transport. It is not a production industrial stack and not safety-certified software.
