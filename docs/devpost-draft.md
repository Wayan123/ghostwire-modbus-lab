# Devpost submission draft: GhostWire

**Status:** local draft. Not sent to Devpost. Fill in the URLs once they exist.

- **Name:** GhostWire: OT Security & Troubleshooting Lab
- **Tagline:** Investigate dead sensors and phantom setpoint writes on real Modbus TCP, safely, with a Kiro-built forensic lab.
- **Category:** Frankenstein
- **Repo:** https://github.com/Wayan123/ghostwire-modbus-lab
- **App / test build:** https://github.com/Wayan123/ghostwire-modbus-lab/releases/latest (Windows x64 zip; needs .NET 8 ASP.NET Core Runtime)
- **Video:** _YouTube URL TBD_
- **Bonus:** Social Blitz (_URL_), Blog (_URL_)

## Inspiration
On a factory floor, "the sensor is dead" usually means a wrong Unit ID, an off-by-one register or a missing scale factor. "The boiler changed setpoint by itself" can mean someone wrote to it. Modbus, from 1979 and still everywhere, has no authentication. Technicians learn both lessons on live equipment, where mistakes are expensive or dangerous.

## What it does
GhostWire is a local lab with five cases against real Modbus TCP simulators on loopback.
- **Troubleshooting:** The Giant Pressure (scaling), The Silent Sensor (Unit ID; a timeout is never presented as proof), The Wrong Register (zero-based addressing).
- **Security incident: Phantom Writer.** A boiler setpoint reads 95.0 °C. The learner opens a traffic log that the server captured from real exchanges at startup, finds the unauthorized FC06 write, types the transaction and client label (wrong guesses earn progressive hints, never the answer), and gets real-world mitigations (IEC 62443 zones and conduits, write allow-listing, Modbus/TCP Security). These are labeled as guidance the simulation does not verify.
- **Security incident: The Night Scanner.** One engineering workstation sweeps unit IDs 1–10 between normal HMI polls. The captured log contains real timeouts and an exception. The learner finds where the reconnaissance starts, then sees guidance on baselining, IDS rules for unit-ID sweeps, and restricting port 502.
- **Wire evidence:** every read shows the actual request and response bytes, with MBAP and PDU fields highlighted on hover or keyboard focus.
- An evidence board separates observations, possible causes, simulator hints, next steps and verification. Case history and a printable report are included. An optional AI explanation never changes results; a labeled deterministic explanation is used when no AI is configured.
- It needs no hardware, accounts or API keys. The learner's client can only read; it cannot target arbitrary hosts.

## How we built it
ASP.NET Core on .NET 8 with a purpose-built, dependency-free Modbus codec, simulator and client, plus a plain HTML/CSS/JS frontend. The codec never throws on hostile bytes, which a property test checks.

## How we used Kiro
_(Condensed from docs/kiro-usage.md; every item has a session ID in docs/kiro-log/LEDGER.md.)_
- **Kiro CLI V3, headless.** 12 sessions (about 230 credits) built the workspace, two specs, both incident cases and the UI, plus two security reviews and their fixes. A small bridge script logs each session with its tools, files and credits.
- **Spec-driven development.** Two EARS specs (`ot-security-lab` 28/28, `night-scanner` 13/13 tasks) with explicit correctness properties. The `ghostwire-builder` agent ran a whole task list per session.
- **Property-based testing.** Six properties, 2,048 seeded cases per run, including "the bytes shown to the learner are exactly the bytes on the socket". **PBT caught a real bug:** seed 987654321 exposed a Windows port-reuse race in the capture matcher that could also have hit production startup.
- **Steering.** Always-on safety and evidence rules, plus a fileMatch Modbus protocol guide that loads only for C# work.
- **Custom agents.** The builder has least-privilege shell access (no push, no `rm -rf`). A read-only security reviewer audited each feature. It found an **answer oracle** (the shape of a wrong answer revealed the right row), and the builder fixed it with a byte-identical-reply test. A scenario author handles new cases.
- **Hooks.** A PreToolUse guard ran 209 times. In a recorded session it blocked Kiro from hard-coding a plant IP (`192.168.1.50`). Test-on-save ran 89 times. A docs-sync agent hook updated the judge guide when each case was added. A Stop hook checks the build.

## Challenges
- Building a forensic log without hard-coded fakes: it is generated from real loopback exchanges at startup, and the server refuses to start if the capture differs from the script.
- Keeping the learner path read-only while an incident needs a write: we used two simulators, one private and write-enabled that is disposed before serving, and one public and read-only.
- A forensic exercise must not leak its answer: every wrong answer returns the same reply.
- Never overclaiming: a timeout is shown as possible causes, never a diagnosis.

## Accomplishments
27,000+ assertions pass, including six property tests across four seeds. Release v1.1.0 was downloaded from GitHub and audited in Chrome: all five cases, keyboard use, input validation that sends nothing on invalid values, 360 px width with no overflow, no console or HTTP errors.

## What's next
More incident cases (replay attacks, a rogue-master polling storm), import of a user's own read-only capture, Modbus RTU frames, and an instructor mode.

## Built with
csharp, dotnet, asp.net-core, modbus, javascript, html, css, kiro
