# Readiness status: 2026-11-11

**Done locally (all verified):**
- Kiro CLI V3 built the `.kiro` workspace, the `ot-security-lab` spec (27/27 tasks) and the code. The evidence is in `docs/kiro-log/` (7 sessions) and `docs/kiro-usage.md`.
- The harness passes: 26,674 checks, including 5 property tests with 2,000 cases per run.
- Windows Chrome: all 4 cases pass end to end, with no JS errors and no overflow at 360 px.
- `ot-security-reviewer` found 0 critical, high or medium issues; the low finding was fixed.
- `dist/GhostWire-Windows-v1.0.0.zip` (sha256 `236891b4…8e38`) was started from a clean folder: all 4 cases, 0.39 bar verified.
- Eligibility for Indonesia is confirmed by the organizer's announcement of 2026-11-11.

**Owner actions remaining, in order:**
1. Push `main` to GitHub, then publish Release `v1.0.0` with the zip. The Release URL is the "working app" link.
2. Request Kiro credits by **Dec 1 12:00 PT**.
3. Record and edit the video (`docs/video/storyboard.md`; brief in content-studio `productions/2026-W48-ghostwire-kiroween`), then upload it to public YouTube.
4. Post the Social Blitz and blog posts (`docs/promo-drafts.md`).
5. Fill the Devpost project (`docs/devpost-draft.md`): category Frankenstein, URLs, Kiro write-up. Submit by Dec 3 WIB.
6. Leave the "PLC Frankenstein" draft unsubmitted.

**Optional if time allows (via Kiro, same workflow):** a second security case (replay or rogue-master polling storm), a short in-app guided tour, and a test of the guard-hook block on video.
