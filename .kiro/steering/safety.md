---
inclusion: always
---

# Safety and evidence

## Hard limits

- Never offer user-selected Modbus hosts or ports, physical-device writes, live PLC deployment or network scanning.
- Learners never issue Modbus writes. In Phantom Writer, the FC06 write is scripted inside the simulator against an in-memory register, or replayed from a recorded log. `/api/test` stays read-only (FC03/FC04).
- Incident cases are defensive. They teach detection and explanation from evidence, not how to attack real equipment.
- No API keys in frontend, files, logs or screenshots. The AI base URL comes only from the server environment, never from a request.

## Labels

- Simulator values and training hints must be labeled as simulated.
- Real-world work needs device documentation and qualified review. Emergency-stop and protective functions require independent safety design.

## Evidence rule

- Never claim a Modbus result that did not come from the simulator. Every value, frame, exception code and log line shown to a learner or written in docs must come from a simulator run or a recorded simulator log.
- Timeouts never prove a single cause. A timeout alone cannot prove an incorrect unit ID; list it as one possible cause beside others such as connection, routing or device unavailability.
- AI explains evidence and cannot change protocol outcomes. Provider text is advisory and labeled with its source.
- `verified` is true only when deterministic rules confirm the expected settings and a successful simulator response.
