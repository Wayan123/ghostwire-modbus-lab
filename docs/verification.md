# Verification record

## Browser audit fixes after v1.0.0 (task 15), 2026-11-11

- Harness: `tools/dotnet run --project csharp/GhostWire.Tests/GhostWire.Tests.csproj` printed `All GhostWire protocol and HTTP checks passed.` (PBT seed 1616322598, 26673 check calls).
- Windows Chrome 157 headless, driven over the DevTools protocol by a temporary script outside the repository (deleted afterwards), against the C# server on loopback port 8765, passed 28 of 28 checks:
  - `/favicon.svg` returned 200 `image/svg+xml`. The session had no HTTP 4xx or 5xx responses and no console errors, warnings or exceptions.
  - The traffic log had no "Select" button and no "Answer" column. Its 12 buttons were the "Show frames" toggles, and each opened a full-width row with 2 hex frames.
  - Times showed as `HH:MM:SS` on one line at 1440 px, with the full ISO timestamp in the title and in visually hidden text. The table (858 px) fitted its container at 1440 px.
  - Unit ID 999, address 70000, quantity 0 and scale 0 each sent no `/api/test` request. Each set `aria-invalid="true"`, focused the field and showed the range message, for example "Unit ID must be a whole number from 1 to 247. Nothing was sent." A valid read afterwards was sent and completed.
  - Two wrong answers showed no hint. The 3rd showed training hint 1 and the 4th added hint 2. Wrong answers did not mark a row and the status message did not contain `1111` or `MAINT`. The typed answer `1111` / `MAINT-LAPTOP-07` verified, marked one row and hid the hints.
  - At 360 px, document and body scroll width were 360 with Phantom Writer selected and frames open. The log scrolled inside its labeled `role="region"` (`tabindex="0"`) and the scroll note was visible.
  - The guided-tour button selected The Silent Sensor (`unit-id`) and focused the case picker. Dismissal persisted across a reload. Under emulated `prefers-reduced-motion: reduce` the banner had no transition or animation.
- Not checked in this run: a real screen reader, print preview and browsers other than Chrome.

## ot-security-lab feature (frame inspector, Phantom Writer, categories, property tests), 2026-11-11

Observed in the local WSL environment using Windows .NET SDK 8.0.427 through `tools/dotnet`.

- `tools/dotnet build csharp/GhostWire/GhostWire.csproj` succeeded with 0 warnings and 0 errors.
- `tools/dotnet run --project csharp/GhostWire.Tests/GhostWire.Tests.csproj` printed `PBT seed: 1616322598 (0x60572026) (default)`, `Check calls passed: 26671` and `All GhostWire protocol and HTTP checks passed.` on two consecutive runs. Property results, with the elapsed time from one run:
  - P-a codec round-trip (FC03/FC04/FC06 requests and responses, exceptions): 600 cases, 14 ms.
  - P-b decoder never throws on 0–300 random bytes, both directions: 600 cases, 13 ms.
  - P-e frames hex equals the bytes the simulator recorded: 200 cases, 4,585 ms.
  - P-c transport status partition (response/exception/timeout, register count equals quantity, exception 02 out of map): 200 cases in batches of 8, 4,844 ms.
  - P-d timeout diagnosis is never verified, has at least two causes, at least one not about the unit ID, and no certainty wording: 400 cases, 16 ms.
- One run with `GHOSTWIRE_PBT_SEED=12345` also passed and printed `PBT seed used: 12345`.
- Example checks that passed in the same run include: the Giant Pressure request `00 01 00 00 00 06 07 03 00 0A 00 01` and response `00 01 00 00 00 05 07 03 02 00 27` (field `Register 10 = 39 (0x0027)`); the Silent Sensor timeout returns one request frame; the Wrong Register response decodes `02 · Illegal Data Address`; `ModbusClient` refuses function 6 before any bytes reach the simulator; the default simulator answers FC06 with `86 01` and leaves the register unchanged; the learner-facing `phantom-writer` simulator answers FC06 with `86 01` and register 100 still reads 950; the Phantom Writer read returns raw 950, 95 °C, `verified: true`; four scenarios with the specified categories and difficulties.
- Traffic log checks: 12 entries, unique transaction IDs, exactly one FC06 entry, two client labels, every frame re-decodes to its entry's fields, and identical JSON from two independent `TrainingService` instances. `/api/incident/unit-id/log`, `/api/incident/../log` and an encoded traversal id return 404.
- Answer checks: transaction 1111 with `MAINT-LAPTOP-07` verifies with four labeled mitigations and observations citing function 06, unit 5, register 100, 650, 950, `2026-10-31T02:13:33Z` and the label. A wrong client, a read transaction and a missing transaction each return `verified: false`, no mitigations and no `MAINT-LAPTOP-07` in the body. Extra, duplicate and missing fields, a non-security scenario, `transaction_id` 70000 or as a string, client `a b`, a 33-character client and invalid JSON return 400; `text/plain` returns 415; a foreign Origin or Host returns 403; an oversized body is rejected.
- Local test provider: the provider request contains `frames` (and `log_entry` for answers); provider text left `verified`, `frames` and `mitigations` unchanged for `/api/test` and `/api/incident/answer`. This is not an external AI-provider test.
- Traffic log served by the running server (`GET /api/incident/phantom-writer/log`): sequences 1–7 are FC03 reads by `HMI-01`, transactions 1–7, at 02:13:00Z to 02:13:30Z, each returning 650 (`… 05 03 02 02 8A`). Sequence 8 is FC06 by `MAINT-LAPTOP-07`, transaction 1111, at 02:13:33Z: request and echo `04 57 00 00 00 06 05 06 00 64 03 B6`. Sequences 9–12 are FC03 reads by `HMI-01`, transactions 8–11, at 02:13:35Z to 02:13:50Z, each returning 950 (`… 05 03 02 03 B6`).

### Browser checks (Windows Chrome headless driven over the DevTools protocol by a temporary script outside the repository)

35 of 35 checks passed against the running server after one fix (the answer button no longer becomes `disabled` during submission, which had dropped keyboard focus):

- The picker shows "Troubleshooting" and "Security incidents" groups with difficulty in the option text; the traffic log panel is hidden for troubleshooting cases and shown for Phantom Writer.
- Keyboard only: Tab reached the Wire evidence summary, Enter opened it, and Tab moved through field rows. Each focused row highlighted its bytes (underline as well as background) and stated "bytes 0–1", "byte 6" and so on as text. Tab reached a log row's Select button, Enter filled the answer form, and the flagged row was reachable with Tab. Focus stayed on the submit button while the result was announced in the `role="status"` message.
- No row was marked before a verified answer. After the verified answer the FC06 row showed "⚠ Unauthorized write" with a double left border and hatched background, and four mitigations under "Next steps · real-world guidance, not verified by this simulation". A wrong answer showed "Status: answer not verified" and no mitigations.
- Emulated `prefers-reduced-motion: reduce` set the byte-highlight transition to 0s.
- Emulated print media hid the investigation panels, kept case history, expanded the answer attempts, and `Page.printToPDF` rendered.

Not checked: a screen reader, a real mobile device, a physical printer, startup failure injection for the incident generator (the code throws and the host resolves the service eagerly, but no harness check forces a mismatch), and the empty `frames` case for a request that was never written (not reproducible on a working loopback listener).

### Security review

The `ot-security-reviewer` agent reviewed commit 0b62fab against `safety.md` (session `sess_6771b137-8663-47a2-b2ec-149419fbbada`, see `docs/kiro-log/LEDGER.md`). It reported 0 critical, 0 high and 0 medium findings, 3 low findings and 2 info findings.

- Low, `Modbus.cs` header mismatch: fixed in code. When the response header fails validation, `ModbusClient` keeps the 7 header bytes it received and the response frame's `decode_error` now reads, for example, `Client stopped after header: transaction ID mismatch. Only the 7 header bytes received were kept.` Before the fix, the decoder reported a "Truncated frame" error, which made it look as if the simulator had sent a short frame. A new harness check uses a loopback fake server that replies with transaction ID 2 and asserts that error text and the received bytes `00 02 00 00 00 05 07`.
- Low, `docs/video/storyboard.md` opening line: the narration now starts "In this simulation", and the shot description names the on-screen text "Status: response" instead of a color change.
- Low, `docs/video/storyboard.md` Phantom Writer line: the narration now says "then see real-world guidance that this simulation does not verify".
- The two info findings (learner `client` text in the AI prompt, `DisposeAsync` not awaiting in-flight handlers) were not changed.

After the fix, `tools/dotnet run --project csharp/GhostWire.Tests/GhostWire.Tests.csproj` printed `PBT seed used: 1616322598`, `Check calls passed: 26672` and `All GhostWire protocol and HTTP checks passed.` It ran with `-p:BaseOutputPath` set to a Windows temp folder because a running `GhostWire.exe` held a lock on the default `bin` output.

## Windows x64 executable package, 2026-11-10

- Published with Windows .NET SDK 8.0.427 using `dotnet publish -c Release --no-restore --self-contained false`. The apphost is a 148 KiB PE32+ executable; the full folder occupied 264 KiB before adding its README. Runtime dependencies are not bundled.
- Launched the published `GhostWire.exe` on the same machine with .NET 8 runtimes installed. It listened on Windows loopback. HTTP calls through Windows Node verified pressure correction to 0.39, silent-sensor timeout then verified response, wrong-register exception then verified response, and deterministic fallback.
- The executable was not tested on a separate machine or without the required .NET 8 ASP.NET Core Runtime.

## C# implementation, 2026-11-10

- Windows .NET SDK 8.0.427: `dotnet build csharp/GhostWire/GhostWire.csproj` succeeded with zero warnings and errors.
- `dotnet run --project csharp/GhostWire.Tests/GhostWire.Tests.csproj` reported all protocol and HTTP checks passed, including loopback TCP reads, three scenarios, invalid input, host/origin and body limits, rate limiting, a local test provider and fallback. This is not an external AI-provider test.
- Windows Chrome headless exercised three fault-and-retest flows against the running C# server: 0.39 bar for corrected pressure; silent sensor timeout then verified response; wrong address exception then verified response. Case switching restored two actual pressure reads, comparison showed the changed scale, and the print button became enabled. A 320-CSS-pixel viewport reported no horizontal overflow. `docs/csharp-desktop-check.png` is an observed Windows Chrome screenshot.
- Color measurements for three representative text/background pairs passed 4.5:1. This is not a full accessibility audit, a screen-reader test, or a real-device mobile test.
- Not tested: physical devices, Modbus interoperability with an independent implementation, external AI provider, public deployment, or genuine Kiro development use. C# source has not yet been published to GitHub.

## Historical Python implementation

Historical Python implementation, observed 2026-11-10 in the local WSL environment. These results do not establish the behavior of the C# replacement:

- `python3 -m unittest discover -s backend/tests -v`: 11 tests passed. These include real loopback TCP simulator/client reads, FC03 and FC04, address exception, simulated unit mismatch timeout, write rejection, API validation and local-provider/fallback tests. Provider tests use a local test service; they do not establish a successful external AI-provider call.
- Browser against the local server: Giant Pressure changed from 39 bar, not verified, to 0.39 bar, verified, after scale 0.01. Silent Sensor timed out at Unit ID 1 and returned a verified response at 17. Wrong Register returned exception 02 at address 21 and verified at 20. The explanation was labeled deterministic fallback.
- Mobile browser inspection reported document width and scroll width both 360 CSS pixels at the sampled viewport; no browser error entries were reported during the inspected session. This is not a full accessibility or cross-browser audit.
- Not tested: physical PLCs or sensors, external AI provider, deployment, independent Modbus implementation interoperability, end-to-end Kiro usage, or a public repository.

These are simulator and local software checks only, not safety certification or evidence about physical equipment.
