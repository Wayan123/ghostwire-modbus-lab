# Devpost submission draft: GhostWire

**Status:** local draft. Not sent to Devpost. Fill in the URLs once they exist.

- **Name:** GhostWire: OT Security & Troubleshooting Lab
- **Tagline:** Investigate dead sensors and phantom setpoint writes on real Modbus TCP, safely, with a Kiro-built forensic lab.
- **Category:** Frankenstein
- **Repo:** https://github.com/Wayan123/ghostwire-modbus-lab
- **App / test build:** https://github.com/Wayan123/ghostwire-modbus-lab/releases/tag/v1.0.0 (Windows x64 zip; needs .NET 8 ASP.NET Core Runtime)
- **Video:** _YouTube URL TBD_
- **Bonus:** Social Blitz (_URL_), Blog (_URL_)

## Inspiration
On a factory floor, "the sensor is dead" usually means a wrong Unit ID, an off-by-one register or a missing scale factor. "The boiler changed setpoint by itself" can mean someone wrote to it. Modbus, from 1979 and still everywhere, has no authentication. Technicians learn both lessons on live equipment, where mistakes are expensive or dangerous.

## What it does
GhostWire is a local lab with four cases against real Modbus TCP simulators on loopback.
- **Troubleshooting:** The Giant Pressure (scaling), The Silent Sensor (Unit ID; a timeout is never presented as proof), The Wrong Register (zero-based addressing).
- **Security incident: Phantom Writer.** A boiler setpoint reads 95.0 °C. The learner opens a traffic log that the server captured from real exchanges at startup, finds the unauthorized FC06 write (transaction 1111 from `MAINT-LAPTOP-07`), submits it, and gets real-world mitigations (IEC 62443 zones and conduits, write allow-listing, Modbus/TCP Security). These are labeled as guidance the simulation does not verify.
- **Wire evidence:** every read shows the actual request and response bytes, with MBAP and PDU fields highlighted on hover or keyboard focus.
- An evidence board separates observations, possible causes, simulator hints, next steps and verification. Case history and a printable report are included. An optional AI explanation never changes results; a labeled deterministic explanation is used when no AI is configured.
- It needs no hardware, accounts or API keys. The learner's client can only read; it cannot target arbitrary hosts.

## How we built it
ASP.NET Core on .NET 8 with a purpose-built, dependency-free Modbus codec, simulator and client, plus a plain HTML/CSS/JS frontend. The codec never throws on hostile bytes, which a property test checks.

## How we used Kiro
_(Condensed from docs/kiro-usage.md; every item has a session ID in docs/kiro-log/LEDGER.md.)_
- **Kiro CLI V3, headless.** Five sessions built the workspace, the spec, the implementation, a security review and the fix. A small bridge script logs each session with its tools, files and credits.
- **Spec-driven development.** A 7-requirement EARS spec with correctness properties and 27 tasks. The `ghostwire-builder` agent ran tasks 1–13 in one session.
- **Property-based testing.** Five properties, 2,000 seeded cases per run, including "the bytes shown to the learner are exactly the bytes on the socket".
- **Steering.** Always-on safety and evidence rules, plus a fileMatch Modbus protocol guide that loads only for C# work.
- **Custom agents.** The builder has least-privilege shell access. A read-only security reviewer found 3 low issues; the builder fixed them. A scenario author handles new cases.
- **Hooks.** A PreToolUse guard blocks any code that targets a non-loopback IP (about 90 checks observed). Test-on-save ran 53 times. A docs-sync agent hook updated the judge guide when a case was added. A Stop hook checks the build.

## Challenges
- Building a forensic log without hard-coded fakes: it is generated from real loopback exchanges at startup, and the server refuses to start if the capture differs from the script.
- Keeping the learner path read-only while the incident needs a write: we used two simulators, one private and write-enabled that is disposed before serving, and one public and read-only.
- Never overclaiming: a timeout is shown as possible causes, never a diagnosis.

## Accomplishments
26,000+ assertions pass, including the property tests. The UI was verified in Chrome: keyboard-only use, 360 px width with no overflow, no JS errors.

## What's next
More incident cases (replay attacks, a rogue master polling storm), import of a user's own read-only capture, Modbus RTU frames, and an instructor mode.

## Built with
csharp, dotnet, asp.net-core, modbus, javascript, html, css, kiro
