# GhostWire product brief

## Problem
Industrial students and early-career technicians can misinterpret a Modbus timeout or raw register because key facts are spread among the request, response and vendor map. A single symptom rarely proves one root cause.

## Audience and value
An interactive, safely isolated simulator lets learners inspect real protocol results, compare a virtual device's documentation, change a setting and see what the retest actually proves. It needs no physical PLC or sensor.

## Three required stories
1. As a learner, I read raw 39 from Giant Pressure, compare the virtual device's documented scale 0.01, set that scale and verify 0.39 bar.
2. As a learner, I see a timeout on Silent Sensor without being told that timeout proves the Unit ID, compare the documented virtual unit, change my setting and retest.
3. As a learner, I see a wrong-address exception on Wrong Register, compare vendor notation to the protocol address, and retest the corrected request.

## Success criteria
A judge runs each case locally end-to-end, sees observed facts separated from possible causes, training hints and verified results, and can review the actual case history. Optional AI output has its source identified and cannot redefine observed results. A no-key fallback remains useful. All values are clearly simulated.

## Out of scope
Physical equipment, live register writes, network scanning, PLC downloads, functional-safety claims, unverified use of Kiro, and publishing or submission without owner approval.

## Contest risks
See `competition-check.md` for the material Indonesia jurisdiction discrepancy and the requirement to demonstrate real Kiro use.
