#!/usr/bin/env python3
"""Stop hook for GhostWire: build the server and ask the agent to continue only if the build fails.

Runs `tools/dotnet build csharp/GhostWire/GhostWire.csproj -v q -nologo` from the repository root.
  * Build succeeds: print nothing, exit 0 (the response stays finished).
  * Build fails with compiler/MSBuild errors: print {"decision": "block", "reason": "..."} on stdout and
    exit 0. For Stop hooks Kiro treats "block" as "start another agent turn with this reason".
  * Build fails only because output files are locked (a running GhostWire.exe), or the build cannot
    start: warn on stderr and exit 0, so the agent is not looped on a problem it cannot fix.
"""
import json
import os
import re
import subprocess
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
COMMAND = ["tools/dotnet", "build", "csharp/GhostWire/GhostWire.csproj", "-v", "q", "-nologo"]
LOCK_CODES = ("MSB3021", "MSB3026", "MSB3027")
MAX_REASON = 3500


def main():
    sys.stdin.read()  # Stop event JSON is not needed.
    try:
        result = subprocess.run(COMMAND, cwd=ROOT, capture_output=True, text=True, timeout=170)
    except (OSError, subprocess.TimeoutExpired) as exc:
        print(f"stop-verify: build did not run ({exc}); not continuing.", file=sys.stderr)
        return 0
    if result.returncode == 0:
        return 0
    output = (result.stdout or "") + (result.stderr or "")
    errors = list(dict.fromkeys(l.strip() for l in output.splitlines() if re.search(r":\s*error\s+[A-Z]+\d+", l)))
    if errors and all(any(code in e for code in LOCK_CODES) for e in errors):
        print("stop-verify: build failed only because output files are locked (is GhostWire running?).", file=sys.stderr)
        return 0
    detail = "\n".join(errors) if errors else "\n".join(output.strip().splitlines()[-30:])
    reason = ("The build failed: `tools/dotnet build csharp/GhostWire/GhostWire.csproj -v q -nologo` exited with "
              f"code {result.returncode}. Fix these errors, then rerun the build and the test harness:\n{detail}")
    print(json.dumps({"decision": "block", "reason": reason[:MAX_REASON]}))
    return 0


if __name__ == "__main__":
    sys.exit(main())
