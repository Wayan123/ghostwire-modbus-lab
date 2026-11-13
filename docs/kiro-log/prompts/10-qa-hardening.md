Harden two small things found by the independent QA run (`../qa-2026-11-13/ghostwire/`), as the ghostwire-builder agent. Keep the changes small, and do not change any case, answer or log content.

## 1. Simulator drops the reply when a client pipelines requests
`ModbusSimulator.ServeAsync` (csharp/GhostWire/Modbus.cs) reads one request, replies, then disposes the socket. When a client sends a second request in the same TCP segment, unread bytes remain in the receive buffer. Closing then sends a TCP RST, and the client can lose the reply that was just written.

Measured with `qa-2026-11-13/tools/pipeline_probe.mjs`, 10 runs each:
- single request: 11/11 bytes every time;
- two coalesced requests: the reply arrived complete in only 2 of 10 runs (others got 0 or 7 bytes, then ECONNRESET).

The learner path is not affected, because the GhostWire client sends exactly one request per connection. The behaviour is still wrong on the wire.

Fix:
- After writing the reply, shut down the send side (`client.Client.Shutdown(SocketShutdown.Send)`).
- Then drain the remaining input with a short bounded read (≤ 260 bytes, ≤ 200 ms) before disposing.
- Keep the one-request-per-connection design and the existing deadlines.

Add one test to the harness: two coalesced FC03 requests on one connection must receive the first reply complete (11 bytes) in 20 of 20 runs.

## 2. Content-Security-Policy on the static UI
The middleware already sets `X-Content-Type-Options: nosniff`. Add these headers to non-API responses:

`Content-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'`

and `Referrer-Policy: no-referrer`.

The frontend has no inline scripts, no inline styles in index.html and no `innerHTML`, so it should work unchanged. Verify that the page still loads and that all five cases still work, using the existing harness and a quick headless check. Add a harness assertion that `/` carries the CSP.

## Checks
Run `dotnet build` and `dotnet run --project csharp/GhostWire.Tests/GhostWire.Tests.csproj`. Update `docs/verification.md` with one line per change. Do not commit.
