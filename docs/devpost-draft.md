# Devpost project story — working draft

**Status:** Local editorial draft only; not sent to Devpost. Local verification is recorded in `verification.md`; genuine Kiro use and eligibility remain unresolved.

## Inspiration

Industrial equipment can appear faulty when a communication setting, register address or scaling interpretation is wrong. Beginners often need to compare observations with device documentation before deciding which hypothesis to test.

## What it does

GhostWire is an interactive, simulation-only Modbus troubleshooting lab. A learner inspects evidence, changes one setting and retests a virtual device. Its evidence board separates observed results from possible causes and simulator-specific hints. The three cases cover pressure scaling, a silent sensor and register addressing.

## How it was built

The application joins a local Modbus TCP simulator and client with deterministic diagnostics, a browser-based investigation interface and an optional AI explanation. Protocol outcomes come from actual loopback exchanges, not the explanation. A labeled deterministic fallback works without an AI provider. The external AI provider has not yet been verified.

## Responsible use

The devices and readings are simulated. A successful virtual test does not verify a physical installation or functional safety. Work on real equipment requires device-specific documentation, independent safety systems and qualified human review.

## Kiro usage

Record only Kiro interactions that actually occurred and what they contributed. The presence of a `.kiro` folder alone does not establish Kiro use.

## Before publication

Verify the implemented features and tests, resolve the jurisdiction discrepancy with the organizer, use the selected Frankenstein category, and provide a public GitHub URL, functional app or test-build URL, public YouTube demo and actual Kiro usage write-up. Do not submit or publish without explicit authorization.
