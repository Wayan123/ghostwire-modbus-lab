#!/usr/bin/env python3
"""PreToolUse guard for GhostWire: block writes to csharp/** that add network targets.

Reads the Kiro hook event JSON from stdin. Exit codes:
  0  allow (also used when the event cannot be parsed: fail-open with a warning)
  2  block; the reason is printed on stderr and returned to the agent

Blocked content in files under csharp/ (bin/ and obj/ are ignored):
  * an IPv4 literal outside 127.0.0.0/8, including 0.0.0.0
  * a TcpClient / Socket connect whose host expression comes from request JSON

Stdlib only. The tool_input field names are not documented by Kiro, so the
script looks for common path and content keys and falls back to scanning every
string value in the event.
"""
import json
import os
import re
import sys

PATH_KEYS = ("path", "file_path", "filePath", "targetFile", "target_file", "filename", "file")
CONTENT_KEYS = ("text", "content", "contents", "newStr", "new_str", "new_string", "newText",
                "file_text", "fileText", "replacement", "code", "data")

IPV4 = re.compile(r"(?<![\w.])((?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(?:\.(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)){3})(?![\w]|\.\d)")
# First argument of a connect-style call or TcpClient constructor.
CONNECT_CALL = re.compile(r"\b(?:ConnectAsync|Connect|BeginConnect|new\s+TcpClient|new\s+IPEndPoint)\s*\(\s*([^,)]*)")
# Expressions that read from the HTTP request or its JSON body.
REQUEST_SOURCE = re.compile(
    r"GetProperty\s*\(|TryGetProperty\s*\(|GetString\s*\(|JsonElement|JsonDocument|JsonNode|JsonSerializer\.Deserialize|"
    r"ReadFromJsonAsync|\bpayload\b|\brequest\s*\.\s*\w*(host|target|address_ip|ip|endpoint|server)\w*|"
    r"\bRequest\s*\.\s*(Query|Form|Headers|Body|RouteValues)|\[\s*\"(host|target|ip|server|endpoint)\"\s*\]",
    re.IGNORECASE)
SAFE_HOST = re.compile(r"^\s*(IPAddress\s*\.\s*(Loopback|IPv6Loopback)|\"(127\.\d+\.\d+\.\d+|localhost|::1)\")\s*$")


def warn(message):
    print(f"guard_targets: {message}", file=sys.stderr)


def walk_strings(value, key=None):
    """Yield (key, string) for every string in a JSON value."""
    if isinstance(value, dict):
        for k, v in value.items():
            yield from walk_strings(v, k)
    elif isinstance(value, list):
        for item in value:
            yield from walk_strings(item, key)
    elif isinstance(value, str):
        yield key, value


def find_paths_and_content(event):
    tool_input = event.get("tool_input")
    if not isinstance(tool_input, (dict, list)):
        tool_input = event.get("input") or event.get("arguments") or event.get("parameters") or event
    paths, contents, others = [], [], []
    for key, text in walk_strings(tool_input):
        if key in PATH_KEYS:
            paths.append(text)
        elif key in CONTENT_KEYS:
            contents.append(text)
        else:
            others.append(text)
    if not contents:
        # Unknown shape: scan every non-path string so content is not missed.
        contents = others
    return paths, contents


def is_csharp_target(path, cwd):
    p = path.replace("\\", "/")
    if p.startswith("file://"):
        p = p[len("file://"):]
    if cwd and os.path.isabs(p):
        root = cwd.replace("\\", "/").rstrip("/") + "/"
        if p.startswith(root):
            p = p[len(root):]
    p = p.removeprefix("./")
    segments = p.split("/")
    if "csharp" not in segments:
        return False
    after = segments[segments.index("csharp") + 1:]
    return not any(s in ("bin", "obj") for s in after)


def check_content(text):
    problems = []
    for match in IPV4.finditer(text):
        ip = match.group(1)
        if ip.startswith("127."):
            continue
        line = text.count("\n", 0, match.start()) + 1
        label = "unspecified address 0.0.0.0 (all interfaces)" if ip == "0.0.0.0" else f"non-loopback IPv4 literal {ip}"
        problems.append(f"line {line}: {label}")
    for match in CONNECT_CALL.finditer(text):
        arg = match.group(1).strip()
        if not arg or SAFE_HOST.match(arg):
            continue
        line = text.count("\n", 0, match.start()) + 1
        if REQUEST_SOURCE.search(arg):
            problems.append(f"line {line}: connection host '{arg}' is taken from request data")
            continue
        # Follow simple identifiers back to an assignment in the same content.
        for ident in set(re.findall(r"[A-Za-z_]\w*", arg)):
            if ident in ("IPAddress", "Parse", "IPEndPoint", "await", "new", "var", "Loopback"):
                continue
            assign = re.compile(rf"\b{re.escape(ident)}\b\s*(?:=(?!=)|:)\s*([^;\n]*)")
            for a in assign.finditer(text):
                if REQUEST_SOURCE.search(a.group(1)):
                    problems.append(f"line {line}: connection host '{arg}' comes from request data via '{ident}'")
                    break
    return problems


def main():
    raw = sys.stdin.read()
    try:
        event = json.loads(raw) if raw.strip() else {}
        if not isinstance(event, dict):
            raise ValueError("event is not a JSON object")
    except (ValueError, json.JSONDecodeError) as exc:
        warn(f"could not parse hook event, allowing the write unchecked ({exc})")
        return 0

    try:
        cwd = event.get("cwd") if isinstance(event.get("cwd"), str) else os.getcwd()
        paths, contents = find_paths_and_content(event)
        targets = [p for p in paths if is_csharp_target(p, cwd)]
        if not paths:
            warn(f"no file path found in {event.get('tool_name', 'tool')} event; allowing")
            return 0
        if not targets:
            return 0
        problems = []
        for text in contents:
            problems.extend(check_content(text))
        if problems:
            unique = list(dict.fromkeys(problems))
            print(
                "Blocked by guard-no-physical-targets: the write to "
                + ", ".join(targets)
                + " adds a network target outside the loopback simulator.\n- "
                + "\n- ".join(unique)
                + "\nGhostWire must connect only to IPAddress.Loopback and simulator-owned ports (see .kiro/steering/safety.md). "
                "Remove the literal or the request-controlled host and retry.",
                file=sys.stderr,
            )
            return 2
        return 0
    except Exception as exc:  # fail-open on anything unexpected
        warn(f"unexpected error, allowing the write unchecked ({exc!r})")
        return 0


if __name__ == "__main__":
    sys.exit(main())
