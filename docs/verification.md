# Verification record

Observed 2026-11-10 in the local WSL environment:

- `python3 -m unittest discover -s backend/tests -v`: 11 tests passed. These include real loopback TCP simulator/client reads, FC03 and FC04, address exception, simulated unit mismatch timeout, write rejection, API validation and local-provider/fallback tests. Provider tests use a local test service; they do not establish a successful external AI-provider call.
- Browser against the local server: Giant Pressure changed from 39 bar, not verified, to 0.39 bar, verified, after scale 0.01. Silent Sensor timed out at Unit ID 1 and returned a verified response at 17. Wrong Register returned exception 02 at address 21 and verified at 20. The explanation was labeled deterministic fallback.
- Mobile browser inspection reported document width and scroll width both 360 CSS pixels at the sampled viewport; no browser error entries were reported during the inspected session. This is not a full accessibility or cross-browser audit.
- Not tested: physical PLCs or sensors, external AI provider, deployment, independent Modbus implementation interoperability, end-to-end Kiro usage, or a public repository.

These are simulator and local software checks only, not safety certification or evidence about physical equipment.
