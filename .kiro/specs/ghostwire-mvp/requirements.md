# GhostWire requirements

These requirements were authored with omp from the project owner's brief. Their presence alone is not evidence of Kiro usage.

- As a learner I can select Giant Pressure, Silent Sensor, or Wrong Register and run a real Modbus TCP read against a simulated device without physical equipment.
- I can inspect observed communication results, suspected causes, simulator-only hints, recommended next tests and verified results separately.
- Giant Pressure returns simulated raw register 39; documentation states 0.01 bar per count; correcting scale yields 0.39 bar.
- Silent Sensor initially times out for mismatched simulated Unit ID; timeout alone never proves its cause. Correcting the ID and retesting verifies a response.
- Wrong Register shows the requested protocol address and device exception, and explains the simulator's documented zero-based address convention.
- I can change the relevant setting, retest, and see a case report based on real attempts.
- The application exposes no arbitrary Modbus target and never writes a device register. It marks simulated values and requires qualified human review for physical equipment.
- An optional AI explanation is grounded in observed evidence and labeled; when unavailable, an explicitly labeled deterministic explanation is provided.
- Interface is responsive, keyboard-accessible and identifies state in text as well as color.
