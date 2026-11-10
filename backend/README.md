# GhostWire backend

Run from the project root with Python 3:

```sh
python3 -m backend.app
```

Open `http://127.0.0.1:8000`. Set `GHOSTWIRE_PORT` to change the loopback HTTP port. The server serves `frontend/` and starts three read-only Modbus/TCP simulators on ephemeral IPv4 loopback ports. Neither the API nor the client accepts a Modbus network destination. FC03 and FC04 are implemented; writes are rejected. The silent-unit behavior is specific to this simulator, not a guarantee about real devices. All observations are simulated.

No third-party package is required: this uses a deliberately limited standard-library Modbus/TCP implementation because no maintained Modbus library was preinstalled in the checked Python environment. No package installation was attempted. It is a training simulator, not a general industrial Modbus stack.

Optional explanation provider configuration (all three variables required):

- `GHOSTWIRE_AI_BASE_URL`: OpenAI-compatible API base URL, for example `https://provider.example/v1` (the backend appends `/chat/completions`).
- `GHOSTWIRE_AI_API_KEY`: provider key, kept on the server.
- `GHOSTWIRE_AI_MODEL`: provider model identifier.

Structured simulation evidence is sent to the configured provider. The provider produces advisory text only; protocol status and register values come from the local Modbus exchange. Missing configuration, errors, or invalid responses use a labeled deterministic explanation. Configure a provider only if its data handling is acceptable to you.

From the project root, the backend tests can be run with:

```sh
python3 -m unittest discover -s backend/tests -v
```
