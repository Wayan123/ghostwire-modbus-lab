# Judge quick start

1. From the project root, run `dotnet run --project csharp/GhostWire/GhostWire.csproj` with the .NET 8 SDK.
2. Open http://127.0.0.1:8000. No login, physical PLC, sensor or AI key is required for the core simulation.
3. Select **The Giant Pressure** and run the read. Inspect raw 39 and the simulated documentation. Set the scale to 0.01 and retest: the displayed result is 0.39 bar.
4. Select **The Silent Sensor**, run the initial read, inspect the timeout and possible causes. Check the simulated documentation, set Unit ID to 17 and retest.
5. Select **The Wrong Register**, run the read, compare address 21 to the simulated zero-based map at 20, then retest with 20.
6. Inspect observations, hypotheses, training hints, verification and case history. The report can be printed via the browser.

All devices and measurements are simulated. The server is limited to loopback; it cannot test or write real equipment. Timeout is not proof of a particular cause. External AI is optional and may be wrong.
