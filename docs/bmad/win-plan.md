# Win plan: GhostWire on Kiroween 2026

Source data was pulled on 2026-11-11 from the Devpost MCP (rules, judging criteria, dates, prizes, announcements) and from kiro.dev/docs.

## Rule facts that matter

| Item | Fact | Status |
|---|---|---|
| Deadline | 2026-12-04 14:00 PT = **2026-12-05 05:00 WIB**; locked after that | internal target: 2026-12-03 |
| Eligibility (Indonesia) | Nov 11 announcement: "Residents of Indonesia are now eligible", with a Bahasa Indonesia translation | resolved |
| Kiro | "Insufficient use of Kiro will result in disqualification" | Kiro is now the builder; evidence goes in `docs/kiro-log/` |
| New project | Built during Oct 30 – Dec 4; disclose pre-existing work | repo created 2026-11-10; disclosure file exists |
| Repo | Public GitHub, OSI license visible in About, `/.kiro` at root and not ignored | Apache-2.0 detected by GitHub |
| App URL | "URL to your functional application"; a test build is allowed | GitHub Release zip |
| Video | < 3 min, public YouTube, shows the app working **and how Kiro was used**, no copyrighted music | to produce |
| Credits | Credit request by **Dec 1 12:00 PT**; redeem by Dec 4 | owner action |

Do not submit "PLC Frankenstein" (multi-plc-agent was created 2026-10-27, before the submission period opened).

## Judging criteria → what we show

| Criterion | Judge question | GhostWire answer |
|---|---|---|
| Potential Impact | Real problem, real audience, would anyone use it? | OT technicians and vocational schools. Modbus has no authentication, and "dead sensor" plus "unexplained setpoint change" are daily incidents. The lab runs offline, needs no hardware, and is safe for a classroom. |
| Technological Implementation | Depth and variety of Kiro use | Spec (EARS + properties) → 3 custom agents → 4 working hooks (test-on-save, physical-target guard, docs sync, stop-build) → steering with fileMatch → PBT → headless V3 bridge with an audit ledger. |
| Design | Complete, polished product | Control-room theme, a 4-step flow, wire-evidence byte highlighting, case report, text status labels, reduced motion. |
| Innovation | Novel combination | Frankenstein: a 1979 serial-era protocol + real TCP simulators + forensics of raw frames + a grounded AI explainer + a Kiro-governed safety hook. A troubleshooting trainer and an ICS security trainer in one app. |
| Presentation | Clear end-to-end video | Story: "a sensor goes silent → wrong ID; boiler hits 95 °C → who wrote it?" Live retests, then 30 s of Kiro artifacts in action (hook blocks a non-loopback target). |

## Bonus prizes
- **Social Blitz:** the post must *start* with `#KiroweenHackathon26`, name your favorite Kiro feature, and say how it changed your workflow. Post it during the submission period and paste the URL into the form.
- **Blog:** dev.to or Medium, saying "written as part of the Kiroween 2026 hackathon". Topic: "Teaching a 1979 protocol with a 2026 agent: building GhostWire with Kiro specs and hooks."
- **Best Demo:** automatic.

## Timeline (WIB)
| Dates | Deliverable |
|---|---|
| Nov 11–13 | Kiro workspace + spec + Phantom Writer + frames + PBT |
| Nov 14–18 | UI polish (Kiro builder + security reviewer), accessibility, second security case if time allows |
| Nov 19–22 | Release zip tested on a clean Windows VM, README/judge guide |
| Nov 23–27 | Video (content-studio pipeline), captions, blog draft |
| Nov 28–Dec 1 | Kiro credit request deadline; social post; Devpost text |
| Dec 2–3 | Final freeze, submit; Dec 4 buffer |
