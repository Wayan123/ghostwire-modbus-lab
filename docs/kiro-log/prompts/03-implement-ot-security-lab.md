Execute ALL tasks in .kiro/specs/ot-security-lab/tasks.md in order, as the ghostwire-builder agent. For each task: read the requirement IDs it references, implement it, run `tools/dotnet run --project csharp/GhostWire.Tests/GhostWire.Tests.csproj`, fix failures, then mark the checkbox `[x]` in tasks.md. Do not skip property-based test tasks.

Constraints (also in steering): no new NuGet/npm dependencies; loopback only; the learner's client path never sends FC06; the incident write happens only inside the server's private simulator at startup; keep existing API fields compatible; keyboard-accessible UI with text status labels and prefers-reduced-motion respected; no invented protocol values in UI text.

If a hook blocks a write or reports a failing test, read its message and fix the cause; do not work around the guard.

When finished, report: tasks completed, files changed, final test output summary (pass/fail counts, PBT seeds and case counts), and anything you could not complete with the reason. Do not commit.
