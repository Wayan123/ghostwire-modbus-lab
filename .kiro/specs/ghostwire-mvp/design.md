# GhostWire design

The browser calls a same-origin Python HTTP API. The backend validates configuration and performs an actual Modbus TCP request to a controlled simulator on loopback. The simulator implements only scenario reads, not writes. Structured observations and deterministic scenario rules remain separate from speculative hypotheses and training hints. An optional server-side provider receives the structured result for explanation; deterministic fallback works without credentials. The frontend maintains the current case's real attempts and reports them without inventing results. See `docs/bmad/implementation-plan.md` for API contract and milestones.

Security: prohibit caller-selected hosts and ports, use no frontend secrets, do not expose live industrial systems, and require a separate security review before public deployment. An AI answer is not the source of protocol truth. Results do not certify physical system safety.

These documents were prepared for use by Kiro; they do not by themselves prove that Kiro was used.
