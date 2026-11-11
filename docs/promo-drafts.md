# Social and blog drafts (owner posts them, during the submission period)

## Social Blitz (LinkedIn or X). The post MUST start with the hashtag.

#KiroweenHackathon26 My favorite thing about Kiro: hooks that enforce engineering rules instead of hoping the AI remembers them.

I'm building GhostWire, a safe OT troubleshooting and security lab that uses real Modbus TCP against local simulators. One Kiro PreToolUse hook blocks any code change that would point the app at a real plant IP. A test-on-save hook reruns the property-based tests on every C# edit.

That changed how I work with AI: I write the spec and the guardrails once, then let Kiro implement while the hooks keep it honest.

Repo: https://github.com/Wayan123/ghostwire-modbus-lab

## Blog (dev.to / Medium / Hashnode)

**Title:** Teaching a 1979 protocol with a 2026 agent: building an OT security lab with Kiro specs and hooks

Required sentence near the top: "This post is written as part of the Kiroween 2026 hackathon."

Outline:
1. The problem. Modbus has no authentication, and "dead sensor" or "mystery setpoint change" is a routine incident. Training usually needs real hardware.
2. What GhostWire does: 4 cases, wire-evidence bytes, a traffic-log forensics case.
3. Spec-driven work with Kiro: EARS requirements, correctness properties turned into property-based tests (show one property and the seed output).
4. Steering: `safety.md` as always-on context; `modbus-protocol.md` loaded only for `csharp/**` via fileMatch.
5. Custom agents: builder, read-only security reviewer, scenario author. Include a finding the reviewer caught (taken from the ledger).
6. Hooks: guard (with a screenshot of the block), test-on-save, docs-sync, stop-build.
7. Orchestration: the headless V3 bridge and its audit ledger.
8. Limits: simulation only, no claim about physical devices.
