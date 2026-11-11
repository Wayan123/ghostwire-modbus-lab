# Verification record

## Windows x64 executable package, 2026-11-10

- Published with Windows .NET SDK 8.0.427 using `dotnet publish -c Release --no-restore --self-contained false`. The apphost is a 148 KiB PE32+ executable; the full folder occupied 264 KiB before adding its README. Runtime dependencies are not bundled.
- Launched the published `GhostWire.exe` on the same machine with .NET 8 runtimes installed. It listened on Windows loopback. HTTP calls through Windows Node verified pressure correction to 0.39, silent-sensor timeout then verified response, wrong-register exception then verified response, and deterministic fallback.
- The executable was not tested on a separate machine or without the required .NET 8 ASP.NET Core Runtime.

## C# implementation, 2026-11-10

- Windows .NET SDK 8.0.427: `dotnet build csharp/GhostWire/GhostWire.csproj` succeeded with zero warnings and errors.
- `dotnet run --project csharp/GhostWire.Tests/GhostWire.Tests.csproj` reported all protocol and HTTP checks passed, including loopback TCP reads, three scenarios, invalid input, host/origin and body limits, rate limiting, a local test provider and fallback. This is not an external AI-provider test.
- Windows Chrome headless exercised three fault-and-retest flows against the running C# server: 0.39 bar for corrected pressure; silent sensor timeout then verified response; wrong address exception then verified response. Case switching restored two actual pressure reads, comparison showed the changed scale, and the print button became enabled. A 320-CSS-pixel viewport reported no horizontal overflow. `docs/csharp-desktop-check.png` is an observed Windows Chrome screenshot.
- Color measurements for three representative text/background pairs passed 4.5:1. This is not a full accessibility audit, a screen-reader test, or a real-device mobile test.
- Not tested: physical devices, Modbus interoperability with an independent implementation, external AI provider, public deployment, or genuine Kiro development use. C# source has not yet been published to GitHub.

## Historical Python implementation

Historical Python implementation, observed 2026-11-10 in the local WSL environment. These results do not establish the behavior of the C# replacement:

- `python3 -m unittest discover -s backend/tests -v`: 11 tests passed. These include real loopback TCP simulator/client reads, FC03 and FC04, address exception, simulated unit mismatch timeout, write rejection, API validation and local-provider/fallback tests. Provider tests use a local test service; they do not establish a successful external AI-provider call.
- Browser against the local server: Giant Pressure changed from 39 bar, not verified, to 0.39 bar, verified, after scale 0.01. Silent Sensor timed out at Unit ID 1 and returned a verified response at 17. Wrong Register returned exception 02 at address 21 and verified at 20. The explanation was labeled deterministic fallback.
- Mobile browser inspection reported document width and scroll width both 360 CSS pixels at the sampled viewport; no browser error entries were reported during the inspected session. This is not a full accessibility or cross-browser audit.
- Not tested: physical PLCs or sensors, external AI provider, deployment, independent Modbus implementation interoperability, end-to-end Kiro usage, or a public repository.

These are simulator and local software checks only, not safety certification or evidence about physical equipment.
