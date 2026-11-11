# How GhostWire was built with Kiro

Every claim below traces to a session in [`docs/kiro-log/LEDGER.md`](kiro-log/LEDGER.md). That folder also has the exact prompt files and the raw Kiro CLI V3 `stream-json` transcripts. Run counts come from those transcripts.

## Surfaces and versions

- **Kiro CLI 3.2.0, V3 harness, headless** (`kiro-cli chat --v3 --no-interactive --output-format stream-json`), model `claude-opus-5.5`. Signed in with a Kiro account.
- `tools/kiro_bridge.py` wraps each headless session and writes the ledger entry. It lets a second terminal agent (omp, used as orchestrator and verifier) start Kiro work without doing the work itself. The rules for that split are in [`kiro-handoff.md`](kiro-handoff.md).
- The `.kiro/` files are the shared format, so the same agents, hooks and steering load in Kiro IDE.

Code that existed before Kiro took over is the C# baseline in commit `c3005fa`, disclosed in [`existing-code-disclosure.md`](existing-code-disclosure.md). Everything after it in `.kiro/`, `csharp/` and `frontend/` was written by Kiro sessions.

## Workflow

```mermaid
flowchart LR
  P[Owner brief] --> S1[Kiro session 01<br/>steering + agents + hooks]
  S1 --> S2[Kiro session 02<br/>spec ot-security-lab]
  S2 --> S3[Kiro session 03<br/>ghostwire-builder runs tasks 1-13]
  S3 --> S4[Kiro session 04<br/>ot-security-reviewer]
  S4 --> S5[Kiro session 05<br/>builder fixes finding, closes task 14]
  H[(4 hooks)] -. fire on every edit/stop .-> S3
  H -.-> S5
```

## Spec-driven development

`.kiro/specs/ot-security-lab/` has 7 requirements with EARS acceptance criteria, a design with an explicit set of **correctness properties**, and 27 tasks. Each task cites the requirement IDs it satisfies. Kiro (session 02) wrote all three files from a one-page brief. Choices Kiro made that a vibe-coding pass would likely have missed:

- **Two simulators for the incident.** The forged FC06 write goes to a private, write-enabled simulator that is disposed before the HTTP server accepts requests. Learners read a separate read-only simulator. This kept the existing "writes are rejected" check true for every simulator a request can reach.
- **Wrong answers withhold the attacker's label.** A wrong submission never reveals the attacker label, so the AI explanation can't leak the answer either.
- **The suspicious row is flagged only after a correct answer**, so the exercise isn't given away.

Session 03 then ran the whole task list as the `ghostwire-builder` agent and checked off tasks 1–13. Task 14 (the security review) was deliberately left to a different agent.

## Property-based testing

The design's correctness properties became five seeded property tests in the dependency-free harness (`csharp/GhostWire.Tests`). There is no PBT library: Kiro wrote a SplitMix64 generator so a seed reproduces the same cases on any .NET version. Set the seed with `GHOSTWIRE_PBT_SEED`.

| Property | Cases per run |
|---|---|
| P-a: encode→decode round-trip for FC03/FC04/FC06 requests and responses | 600 |
| P-b: the decoder never throws on random bytes | 600 |
| P-c: every simulator response is exactly one of response / exception / timeout, and the register count matches the request | 200 |
| P-d: a timeout diagnosis never claims a single certain cause | 400 |
| P-e: the `frames` hex shown to the learner equals the bytes recorded on the socket | 200 |

P-e is the property that matters for a forensics trainer: the "wire evidence" panel cannot show bytes that weren't sent.

## Steering

| File | Inclusion | Purpose |
|---|---|---|
| `product.md` | always | audience, the four-step flow, tone |
| `safety.md` | always | simulation-only rules, plus an **evidence rule**: never state a Modbus result the simulator didn't return; a timeout never proves one cause |
| `tech.md` | always | stack, the `tools/dotnet` wrapper for `/mnt/d`, test command |
| `accessibility.md` | fileMatch `frontend/**` | text status labels, keyboard use, reduced motion |
| `modbus-protocol.md` | fileMatch `csharp/**/*.cs` | MBAP/PDU layouts, exception codes, zero-based addressing |

The fileMatch files load only when Kiro touches matching files, so protocol detail doesn't crowd the context during UI work. Every session transcript records which steering documents were included (`steeringDocuments` events).

## Custom agents (`.kiro/agents/*.md`)

- **ghostwire-builder** has read, write and shell tools. Its permission rules allow only `tools/dotnet *` and `git status/diff`, and deny `git push*` and `rm -rf*`. It ran sessions 03 and 05.
- **ot-security-reviewer** is read-only, and shell is limited to `git diff*`. Session 04 reviewed commit `0b62fab` against eight checks: request-chosen hosts, learner-reachable writes, SSRF through the AI base URL, secrets, input bounds, decoder crashes, XSS and overclaiming. It found 0 critical, high or medium issues and 3 low ones. The builder fixed the one in code and corrected the storyboard wording.
- **scenario-author** may write only to `docs/scenarios/**` and is used to design future cases.

Kiro also called its built-in `introspect` sub-agent to confirm current V3 file formats before writing any config.

## Agent hooks (`.kiro/hooks/*.json`), observed firing

| Hook | Trigger | What it did | Events in transcripts |
|---|---|---|---|
| `guard-no-physical-targets` | PreToolUse on `fs_write`/`fs_append`/`str_replace` | Runs `tools/hooks/guard_targets.py`, which blocks (exit 2) C# edits that add a non-loopback IPv4 literal, `0.0.0.0`, or a socket host taken from request JSON | ~90 |
| `run-tests-after-csharp-change` | PostFileSave `csharp/**/*.cs` | Reruns the harness (property tests included) and feeds the failure output back to the agent | 53 |
| `scenario-docs-sync` | PostFileSave `Training.cs` (agent action) | Asks Kiro to update `docs/judge-quick-start.md` when scenario data changes; it did so when Phantom Writer was added | 15 |
| `stop-verify` | Stop | Builds the server and makes the agent continue if compilation fails | 3 |

The guard hook turns the project's main safety rule into an enforced check rather than a request. A demonstration of it blocking an edit is recorded for the video.

## MCP

Kiro used no MCP server for the app itself. The orchestrator used the Devpost MCP server to pull the official rules, judging criteria and announcements quoted in `docs/bmad/win-plan.md`.

## What we learned

- The spec→tasks→builder loop with test-on-save produced 13 tasks of working code in one session. The hook output corrected failures without a human relaying them.
- Separating builder and reviewer agents, each with its own permissions, gave an independent review that could not edit what it was reviewing.
- A Windows build lock from concurrent hook runs once made the harness fail. Kiro noticed, waited and retried. `stop_build_check.py` treats lock-only errors as a warning so the Stop hook doesn't loop.
