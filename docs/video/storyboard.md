# Demo video plan (Kiroween 2026)

Target: **2:40**, 1920×1080, 30 fps, English narration and burned-in English captions. No third-party music; use silence or a self-generated tone. It goes on public YouTube once the owner approves. It is produced with the content-studio pipeline (HyperFrames, already installed and pinned) and follows that pipeline's `CLAIMS.md` / `QC.md` rule: every on-screen claim maps to a recorded app action or a ledger entry.

## Script

| Time | Visual (all real recordings) | Narration |
|---|---|---|
| 0:00–0:12 | Boiler setpoint reads 95.0 °C; the result panel shows the text "Status: response" and the simulated documentation states a normal setpoint of 65.0 °C | "In this simulation, a boiler setpoint jumps to 95 degrees. Modbus has no passwords. Who wrote it?" |
| 0:12–0:25 | Title card + case picker grouped Troubleshooting / Security | "GhostWire is a safe OT lab: real Modbus TCP against local simulators, for technicians who have to debug and defend plants." |
| 0:25–0:55 | Silent Sensor: timeout → possible causes (not proof) → Unit ID 17 → verified; open **Wire evidence**, hover bytes → MBAP fields highlight | "A timeout never proves one cause. GhostWire shows what was observed, what's possible, and the exact bytes on the wire." |
| 0:55–1:10 | Giant Pressure 39 → 0.39 bar | "Raw counts aren't engineering values." |
| 1:10–1:45 | Phantom Writer: read 950 → open traffic log → spot FC06 from MAINT-LAPTOP-07, txn 1111 → submit → verified + mitigations (IEC 62443 zones, write allow-list, Modbus/TCP Security) | "The log was captured from real exchanges. Find the write, name the source, then see real-world guidance that this simulation does not verify." |
| 1:45–2:25 | Kiro: `.kiro/specs/ot-security-lab` tasks checked; agents list; the guard hook **blocking** a non-loopback IP write (recorded live); test-on-save hook output; `docs/kiro-log/LEDGER.md` | "Kiro built this: a spec with property-based tests, three custom agents, and hooks. One hook physically refuses code that targets a real plant." |
| 2:25–2:40 | Repo URL, Apache-2.0, "simulation only" | "GhostWire. Investigate the evidence before you touch a real system." |

## Shots to record (Windows Chrome, 1440×900, then scale)
1. Each of the 4 cases from a fresh page load.
2. Kiro CLI session: ask `ghostwire-builder` to add a "remote gateway at 192.168.1.50" → the guard hook blocks it (exit 2 message). Keep this transcript in `docs/kiro-log/`.
3. `/spec run ot-security-lab` view, or the checked `tasks.md`.

## Deliverables
`docs/video/ghostwire-demo.mp4`, `.srt`, `CLAIMS.md`, `QC.md`; YouTube URL recorded in `docs/devpost-draft.md`.
