using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace GhostWire;

public sealed record IncidentStep(string Client, ushort TransactionId, int OffsetSeconds, byte Function, ushort Address, ushort QuantityOrValue);

public sealed record TrafficLogEntry(int sequence, string time, string client, int transaction_id, int unit_id, int function_code, string function_name,
                                     int address, ushort[] values, string status, IReadOnlyList<DecodedFrame> frames);
public sealed record TrafficLog(string scenario_id, bool simulated, string capture_note, IReadOnlyList<TrafficLogEntry> entries);

// Fixed Phantom Writer script. These are inputs to the private simulator, not claimed results:
// every value in the log is decoded from the bytes that simulator actually received and sent.
public static class IncidentScript
{
    public const string ScenarioId = "phantom-writer";
    public const byte Unit = 5;
    public const ushort Register = 100;
    public const ushort InitialSetpoint = 650;
    public const ushort WrittenValue = 950;
    public const string ReaderClient = "HMI-01";
    public const string WriterClient = "MAINT-LAPTOP-07";
    public const ushort WriteTransactionId = 1111;
    public static readonly DateTimeOffset Start = new(2026, 10, 31, 2, 13, 0, TimeSpan.Zero);
    public const string CaptureNote = "Simulated capture generated at server start from loopback exchanges with a private simulator. Modbus TCP frames carry no client identity; client labels are capture metadata.";

    public static readonly IncidentStep[] PhantomWriter = BuildSteps();

    private static IncidentStep[] BuildSteps()
    {
        var steps = new List<IncidentStep>();
        for (int i = 0; i < 7; i++) steps.Add(new(ReaderClient, (ushort)(i + 1), i * 5, 3, Register, 1));
        steps.Add(new(WriterClient, WriteTransactionId, 33, 6, Register, WrittenValue));
        int[] later = { 35, 40, 45, 50 };
        for (int i = 0; i < later.Length; i++) steps.Add(new(ReaderClient, (ushort)(8 + i), later[i], 3, Register, 1));
        return steps.ToArray();
    }
}

// Startup-only component. It is the only code that encodes and sends FC06, and only to its own private,
// loopback-bound simulator, which is disposed before this method returns.
public static class IncidentLogGenerator
{
    private sealed class Recorder : IExchangeRecorder
    {
        private readonly object gate = new();
        private readonly List<SimulatorExchange> items = new();
        public void Record(SimulatorExchange exchange) { lock (gate) items.Add(exchange); }
        public SimulatorExchange[] Items { get { lock (gate) return items.ToArray(); } }
    }

    public static async Task<(TrafficLog Log, ushort FinalSetpoint)> GenerateAsync(CancellationToken ct)
    {
        var recorder = new Recorder();
        var clientByPort = new Dictionary<int, string>();
        var script = IncidentScript.PhantomWriter;
        await using (var simulator = new ModbusSimulator(IncidentScript.Unit, new() { [3] = new() { [IncidentScript.Register] = IncidentScript.InitialSetpoint } }, allowWrites: true, recorder: recorder))
        {
            foreach (var step in script)
            {
                var request = step.Function == 6
                    ? ModbusCodec.EncodeWriteSingleRequest(step.TransactionId, IncidentScript.Unit, step.Address, step.QuantityOrValue)
                    : ModbusCodec.EncodeReadRequest(step.TransactionId, IncidentScript.Unit, step.Function, step.Address, step.QuantityOrValue);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                deadline.CancelAfter(TimeSpan.FromMilliseconds(300));
                using var tcp = new TcpClient();
                await tcp.ConnectAsync(IPAddress.Loopback, simulator.Port, deadline.Token);
                clientByPort[((IPEndPoint)tcp.Client.LocalEndPoint!).Port] = step.Client;
                using var stream = tcp.GetStream();
                await stream.WriteAsync(request, deadline.Token);
                var header = new byte[7];
                await stream.ReadExactlyAsync(header, deadline.Token);
                int length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4));
                if (length is < 2 or > 254) throw new InvalidOperationException($"Incident step {step.TransactionId}: invalid reply length {length}");
                await stream.ReadExactlyAsync(new byte[length - 1], deadline.Token);
                // The simulator records the exchange and then closes the connection, so end-of-stream means
                // this step's recording is complete and the steps stay strictly ordered.
                if (await stream.ReadAsync(new byte[1], deadline.Token) != 0) throw new InvalidOperationException($"Incident step {step.TransactionId}: unexpected extra reply bytes");
            }
        }

        var captured = recorder.Items;
        if (captured.Length != script.Length) throw new InvalidOperationException($"Incident capture has {captured.Length} exchanges, expected {script.Length}");
        var entries = new List<TrafficLogEntry>();
        for (int i = 0; i < script.Length; i++)
        {
            var step = script[i];
            var exchange = captured[i];
            if (!clientByPort.TryGetValue(exchange.RemotePort, out var client) || client != step.Client)
                throw new InvalidOperationException($"Incident capture entry {i + 1} does not match the connection opened for its step");
            if (exchange.Sent == null) throw new InvalidOperationException($"Incident capture entry {i + 1} has no response");
            if (!ModbusCodec.TryParse(exchange.Received, FrameDirection.Request, out var request, out var requestError) || request == null)
                throw new InvalidOperationException($"Incident capture entry {i + 1} request did not decode: {requestError}");
            if (!ModbusCodec.TryParse(exchange.Sent, FrameDirection.Response, out var response, out var responseError) || response == null)
                throw new InvalidOperationException($"Incident capture entry {i + 1} response did not decode: {responseError}");
            if (request.Header.TransactionId != step.TransactionId || response.Header.TransactionId != step.TransactionId || request.Function != step.Function || response.Function != step.Function)
                throw new InvalidOperationException($"Incident capture entry {i + 1} differs from the script");
            ushort[] values = step.Function == 6 ? new[] { response.QuantityOrValue!.Value } : response.Registers;
            var frames = new List<DecodedFrame> { ModbusCodec.Decode(exchange.Received, FrameDirection.Request), ModbusCodec.Decode(exchange.Sent, FrameDirection.Response, request.Address) };
            entries.Add(new(i + 1, IncidentScript.Start.AddSeconds(step.OffsetSeconds).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture), client,
                request.Header.TransactionId, request.Header.UnitId, request.Function, ModbusCodec.FunctionName(request.Function), request.Address!.Value, values, "response", frames));
        }

        // Validate the capture against the script: 650 before the write, an FC06 echo of 950, then 950.
        int writeIndex = Array.FindIndex(script, s => s.Function == 6);
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e.unit_id != IncidentScript.Unit || e.address != IncidentScript.Register || e.values.Length != 1)
                throw new InvalidOperationException($"Incident capture entry {i + 1} has unexpected unit, address or values");
            ushort expected = i < writeIndex ? IncidentScript.InitialSetpoint : IncidentScript.WrittenValue;
            if (e.values[0] != expected) throw new InvalidOperationException($"Incident capture entry {i + 1} shows {e.values[0]}, expected {expected}");
            if (i == writeIndex && (e.function_code != 6 || !captured[i].Sent!.AsSpan(7).SequenceEqual(captured[i].Received.AsSpan(7))))
                throw new InvalidOperationException("Incident capture write is not an FC06 echo");
        }
        if (entries.Count(e => e.function_code == 6) != 1) throw new InvalidOperationException("Incident capture must contain exactly one write");
        ushort final = entries[^1].values[0];
        return (new TrafficLog(IncidentScript.ScenarioId, true, IncidentScript.CaptureNote, entries), final);
    }
}
