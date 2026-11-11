---
inclusion: always
---

# Product: GhostWire OT Security & Troubleshooting Lab

GhostWire is a Kiroween 2026 entry (Frankenstein category). It teaches industrial technicians and students to investigate Modbus TCP faults and security incidents from evidence, using only local simulators on the learner's computer.

## Learning loop

1. Inspect the symptom and the simulated device documentation.
2. Gather evidence: a simulator read, its raw frame, or a captured traffic log.
3. Form a hypothesis and keep it separate from what was observed.
4. Change one setting, then retest or re-check the log.
5. Report what the evidence verified and what it did not.

## Cases

- Implemented fault cases: **The Giant Pressure** (scale), **The Silent Sensor** (unit ID, timeout), **The Wrong Register** (zero-based address, exception 02).
- Planned incident case, **Phantom Writer**: an unauthorized FC06 write changes a simulated setpoint. The learner identifies the write (time, unit ID, register, old and new value) from a captured traffic log.
- Planned **frame inspector**: shows the MBAP header and PDU bytes of the request and response for every read.
- Planned **property-based tests** for the Modbus codec and the diagnosis rules.

Treat planned features as not implemented until their spec tasks are done and tested.

## Writing for learners

- Use plain language a beginner can follow. Define a term (MBAP, PDU, FC06, unit ID) the first time a screen uses it.
- Label every statement as one of: observation, possible cause, training hint, next step, or verified outcome.
- Say "in this simulation". Never imply a result says anything about a physical installation.
