# Demo video

- **File (not in git, 31 MB):** `content-studio/productions/2026-W48-ghostwire-kiroween/renders/ghostwire-kiroween-demo.mp4`, 1920×1080, 30 fps, H.264 + AAC, about 2:16, −16.9 LUFS, true peak −1.5 dBFS.
- **Captions:** burned-in English, plus [`ghostwire-kiroween-demo.srt`](ghostwire-kiroween-demo.srt) for the YouTube subtitle track.
- **Made automatically:** `tools/record.mjs` (Windows Chrome CDP screencast of the running v1.1.0 release), `tools/tts.py` (local VoxCPM2 narration on the GPU, checked with local Whisper), `tools/build.py` (ffmpeg assembly). The Kiro scenes are rendered from `.kiro/specs/ot-security-lab/tasks.md`, `.kiro/agents/*.md` and `docs/kiro-log/LEDGER.md`. The guard-hook block is from real session 08.
- **Claims register:** `CLAIMS.md` in the production folder. Every line maps to a test, a file or a ledger session.
- **Script order:** hook (95 °C setpoint) → intro → Silent Sensor → wire evidence → Giant Pressure → Phantom Writer log, answer, verified → Night Scanner → Kiro spec, agents, guard hook, ledger → close.

The Wrong Register case is not shown, to stay well under 3 minutes. It is in the app and in the judge guide.
