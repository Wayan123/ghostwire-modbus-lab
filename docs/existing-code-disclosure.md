# Existing code and attribution

GhostWire was started during the Kiroween 2026 submission period. The repository was created on 2026-11-10.

## Before Kiro (commit `c3005fa` and earlier)
A first Python prototype and then a C# baseline were written on 2026-11-10 with omp, a different terminal coding agent, working from the owner's planning PDF. That baseline had three troubleshooting cases, a loopback Modbus FC03/FC04 simulator and client, an optional AI explanation, and the original frontend. It is preserved in git history as the starting point.

## Built with Kiro (commit `2177acf` onward)
Everything under `.kiro/` (steering, custom agents, hooks, the `ot-security-lab` spec), `tools/hooks/`, and every change to `csharp/` and `frontend/` after `c3005fa` came from Kiro CLI sessions recorded in `docs/kiro-log/`. That includes the codec, wire frames, the Phantom Writer incident, property-based tests, the UI panels and the security fix.

## Written by the orchestrator, not Kiro
`tools/kiro_bridge.py`, `tools/dotnet`, `docs/kiro-handoff.md`, `docs/kiro-usage.md`, `docs/bmad/*`, `docs/devpost-draft.md`, `docs/promo-drafts.md` and `docs/video/storyboard.md`.

## Other sources
No code was copied from the owner's earlier projects, MultiPoll Studio and multi-plc-agent. They contributed domain experience only. There are no third-party packages beyond .NET 8 and ASP.NET Core, which keep their own licenses. GhostWire is licensed under Apache-2.0.
