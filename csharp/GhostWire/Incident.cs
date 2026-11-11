using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace GhostWire;

public sealed record IncidentStep(string Client, ushort TransactionId, int OffsetSeconds, byte Function, ushort Address, ushort QuantityOrValue, byte Unit = IncidentScript.Unit);

public sealed record TrafficLogEntry(int sequence, string time, string client, int transaction_id, int unit_id, int function_code, string function_name,
                                     int address, ushort[] values, string status, IReadOnlyList<DecodedFrame> frames, int? exception_code = null);
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

// Parameters of a Night Scanner capture. Production uses Default; the harness derives plans from seeds.
// These are inputs to the private gateway simulator, not claimed results.
public sealed record NightScannerPlan(int ScanUnits, byte RespondingUnit, ushort RespondingValue, int ScanStartSeconds, ushort ScanFirstTransactionId, string ScannerClient)
{
    public static readonly NightScannerPlan Default = new(10, 9, 1, 7, 301, "ENG-WS-13");
    private static readonly string[] Labels = { "ENG-WS-13", "VENDOR-RDP-2", "QA.LAPTOP_9" };

    public static NightScannerPlan FromSeed(ulong seed)
    {
        ulong state = seed;
        int Next(int min, int maxInclusive)
        {
            ulong z = state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            return (int)(min + (long)(z % (ulong)(maxInclusive - min + 1)));
        }
        int units = Next(5, 12);
        var candidates = Enumerable.Range(1, units).Where(u => u != NightScannerScript.BoilerUnit).ToArray();
        return new(units, (byte)candidates[Next(0, candidates.Length - 1)], (ushort)Next(0, 65535), Next(1, 14), (ushort)Next(100, 60000), Labels[Next(0, Labels.Length - 1)]);
    }

    public void Validate()
    {
        if (ScanUnits is < 5 or > 12) throw new ArgumentException("ScanUnits must be 5-12");
        if (RespondingUnit < 1 || RespondingUnit > ScanUnits || RespondingUnit == NightScannerScript.BoilerUnit) throw new ArgumentException("RespondingUnit must be in 1-ScanUnits and not 5");
        if (ScanStartSeconds is < 1 or > 30) throw new ArgumentException("ScanStartSeconds must be 1-30");
        if (ScanFirstTransactionId < 100 || ScanFirstTransactionId + ScanUnits - 1 > 65535) throw new ArgumentException("Scan transaction IDs must be 100-65535");
        if (!System.Text.RegularExpressions.Regex.IsMatch(ScannerClient, "^[A-Za-z0-9._-]{1,32}$") || string.Equals(ScannerClient, NightScannerScript.PollClient, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("ScannerClient must be a valid label other than the polling client");
    }
}

// The Night Scanner: HMI polling of unit 5 interleaved with an FC03 sweep of unit IDs 1..N. Reads only.
public static class NightScannerScript
{
    public const string ScenarioId = "night-scanner";
    public const byte BoilerUnit = 5;
    public const ushort Register = 100;
    public const ushort Setpoint = 650;
    public const string PollClient = "HMI-01";
    public const ushort ScanAddress = 0;
    public const int PollIntervalSeconds = 5;
    // Steps expected to get a reply use the 300 ms Modbus deadline. Only probes of unit IDs with no device use the
    // shorter deadline, so startup stays fast without failing on a slow host.
    public static readonly TimeSpan ReplyDeadline = TimeSpan.FromMilliseconds(300);
    public static readonly TimeSpan ProbeDeadline = TimeSpan.FromMilliseconds(100);
    public static readonly DateTimeOffset Start = new(2026, 11, 1, 3, 12, 0, TimeSpan.Zero);
    public const string CaptureNote = "Simulated capture generated at server start from loopback exchanges with a private gateway simulator. Modbus TCP frames carry no client identity; client labels are capture metadata. A request with no reply within 100 ms is logged as a timeout.";

    public static IncidentStep[] Build(NightScannerPlan plan)
    {
        plan.Validate();
        var timed = new List<(int Time, int Order, IncidentStep Step)>();
        int lastScan = plan.ScanStartSeconds + plan.ScanUnits - 1;
        ushort pollTx = 1;
        for (int t = 0; ; t += PollIntervalSeconds)
        {
            timed.Add((t, 0, new(PollClient, pollTx++, t, 3, Register, 1, BoilerUnit)));
            if (t > lastScan) break;
        }
        for (int k = 0; k < plan.ScanUnits; k++)
            timed.Add((plan.ScanStartSeconds + k, 1, new(plan.ScannerClient, (ushort)(plan.ScanFirstTransactionId + k), plan.ScanStartSeconds + k, 3, ScanAddress, 1, (byte)(k + 1))));
        // Interleave by simulated time; on a shared second the HMI poll comes first.
        return timed.OrderBy(x => x.Time).ThenBy(x => x.Order).Select(x => x.Step).ToArray();
    }

    public static Dictionary<int, Dictionary<int, Dictionary<int, ushort>>> Devices(NightScannerPlan plan) => new()
    {
        [BoilerUnit] = new() { [3] = new() { [Register] = Setpoint } },
        [plan.RespondingUnit] = new() { [3] = new() { [ScanAddress] = plan.RespondingValue } },
    };
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
        var script = IncidentScript.PhantomWriter;
        List<TrafficLogEntry> entries;
        await using (var simulator = new ModbusSimulator(IncidentScript.Unit, new() { [3] = new() { [IncidentScript.Register] = IncidentScript.InitialSetpoint } }, allowWrites: true, recorder: recorder))
            entries = await CaptureAsync(simulator, recorder, script, IncidentScript.Start, _ => TimeSpan.FromMilliseconds(300), ct);

        // Validate the capture against the script: 650 before the write, an FC06 echo of 950, then 950.
        int writeIndex = Array.FindIndex(script, s => s.Function == 6);
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e.status != "response") throw new InvalidOperationException($"Incident capture entry {i + 1} has status {e.status}, expected a response");
            if (e.unit_id != IncidentScript.Unit || e.address != IncidentScript.Register || e.values.Length != 1)
                throw new InvalidOperationException($"Incident capture entry {i + 1} has unexpected unit, address or values");
            ushort expected = i < writeIndex ? IncidentScript.InitialSetpoint : IncidentScript.WrittenValue;
            if (e.values[0] != expected) throw new InvalidOperationException($"Incident capture entry {i + 1} shows {e.values[0]}, expected {expected}");
            // An FC06 echo has the same MBAP header and PDU as its request, so the two frames are byte-identical.
            if (i == writeIndex && (e.function_code != 6 || e.frames.Count != 2 || e.frames[0].hex != e.frames[1].hex))
                throw new InvalidOperationException("Incident capture write is not an FC06 echo");
        }
        if (entries.Count(e => e.function_code == 6) != 1) throw new InvalidOperationException("Incident capture must contain exactly one write");
        ushort final = entries[^1].values[0];
        return (new TrafficLog(IncidentScript.ScenarioId, true, IncidentScript.CaptureNote, entries), final);
    }

    // Night Scanner: FC03 reads only, against a private read-only gateway that is disposed before this returns.
    public static async Task<TrafficLog> GenerateNightScannerAsync(NightScannerPlan plan, CancellationToken ct)
    {
        var script = NightScannerScript.Build(plan);
        var recorder = new Recorder();
        List<TrafficLogEntry> entries;
        await using (var gateway = new ModbusSimulator(NightScannerScript.Devices(plan), recorder: recorder))
            entries = await CaptureAsync(gateway, recorder, script, NightScannerScript.Start,
                step => step.Unit == NightScannerScript.BoilerUnit || step.Unit == plan.RespondingUnit ? NightScannerScript.ReplyDeadline : NightScannerScript.ProbeDeadline, ct);

        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e.function_code != 3) throw new InvalidOperationException($"Night Scanner entry {i + 1} is not FC03");
            string expected; ushort[] values = Array.Empty<ushort>(); int? code = null;
            if (e.client == NightScannerScript.PollClient)
            {
                if (e.unit_id != NightScannerScript.BoilerUnit || e.address != NightScannerScript.Register) throw new InvalidOperationException($"Night Scanner poll {i + 1} has unexpected unit or address");
                expected = "response"; values = new[] { NightScannerScript.Setpoint };
            }
            else if (e.unit_id == plan.RespondingUnit) { expected = "response"; values = new[] { plan.RespondingValue }; }
            else if (e.unit_id == NightScannerScript.BoilerUnit) { expected = "exception"; code = 2; }
            else expected = "timeout";
            if (e.status != expected || !e.values.SequenceEqual(values) || e.exception_code != code)
                throw new InvalidOperationException($"Night Scanner entry {i + 1} (unit {e.unit_id}) is {e.status}, expected {expected}");
        }
        var log = new TrafficLog(NightScannerScript.ScenarioId, true, NightScannerScript.CaptureNote, entries);
        var answer = Diagnosis.AnswerEntry(log);
        if (answer == null || answer.client != plan.ScannerClient || answer.transaction_id != plan.ScanFirstTransactionId)
            throw new InvalidOperationException("Night Scanner capture does not show exactly one scanning client starting at the planned transaction");
        return log;
    }

    // Runs each step on its own loopback connection, strictly in order, and builds log entries from the bytes the
    // simulator recorded. A timeout is logged only when this client's deadline expired and the simulator sent nothing.
    private static async Task<List<TrafficLogEntry>> CaptureAsync(ModbusSimulator simulator, Recorder recorder, IReadOnlyList<IncidentStep> script,
                                                                  DateTimeOffset start, Func<IncidentStep, TimeSpan> stepDeadline, CancellationToken ct)
    {
        var ports = new int[script.Count];
        var timedOut = new bool[script.Count];
        if (script.Select(s => s.TransactionId).Distinct().Count() != script.Count) throw new InvalidOperationException("Incident script transaction IDs must be unique");
        for (int i = 0; i < script.Count; i++)
        {
            var step = script[i];
            var request = step.Function == 6
                ? ModbusCodec.EncodeWriteSingleRequest(step.TransactionId, step.Unit, step.Address, step.QuantityOrValue)
                : ModbusCodec.EncodeReadRequest(step.TransactionId, step.Unit, step.Function, step.Address, step.QuantityOrValue);
            var limit = stepDeadline(step);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(limit);
            using var tcp = new TcpClient();
            NetworkStream stream;
            try
            {
                await tcp.ConnectAsync(IPAddress.Loopback, simulator.Port, deadline.Token);
                ports[i] = ((IPEndPoint)tcp.Client.LocalEndPoint!).Port;
                stream = tcp.GetStream();
                await stream.WriteAsync(request, deadline.Token);
            }
            // Only the outer token means the 5 s startup deadline; a step deadline here is a connect or write failure.
            catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
            {
                throw new InvalidOperationException($"Incident step {step.TransactionId}: connect/write exceeded its {limit.TotalMilliseconds} ms step deadline", ex);
            }
            using var ownedStream = stream;
            try
            {
                var header = new byte[7];
                await stream.ReadExactlyAsync(header, deadline.Token);
                int length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4));
                if (length is < 2 or > 254) throw new InvalidOperationException($"Incident step {step.TransactionId}: invalid reply length {length}");
                await stream.ReadExactlyAsync(new byte[length - 1], deadline.Token);
                // The simulator records the exchange and then closes the connection, so end-of-stream means
                // this step's recording is complete and the steps stay strictly ordered.
                if (await stream.ReadAsync(new byte[1], deadline.Token) != 0) throw new InvalidOperationException($"Incident step {step.TransactionId}: unexpected extra reply bytes");
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { timedOut[i] = true; }
        }

        // A silent simulator records on receipt; wait briefly in case that record is still in flight.
        for (int waited = 0; recorder.Items.Length < script.Count && waited < 1000; waited += 5) await Task.Delay(5, ct);
        var captured = recorder.Items;
        if (captured.Length != script.Count) throw new InvalidOperationException($"Incident capture has {captured.Length} exchanges, expected {script.Count}");
        var entries = new List<TrafficLogEntry>();
        for (int i = 0; i < script.Count; i++)
        {
            var step = script[i];
            // The OS can reuse a client port within one capture, so match on the port and the transaction ID together.
            var matches = captured.Where(x => x.RemotePort == ports[i] && x.Received.Length >= 2 && BinaryPrimitives.ReadUInt16BigEndian(x.Received) == step.TransactionId).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException($"Incident capture entry {i + 1} does not match the connection opened for its step");
            var exchange = matches[0];
            if (!ModbusCodec.TryParse(exchange.Received, FrameDirection.Request, out var request, out var requestError) || request == null)
                throw new InvalidOperationException($"Incident capture entry {i + 1} request did not decode: {requestError}");
            if (request.Header.TransactionId != step.TransactionId || request.Header.UnitId != step.Unit || request.Function != step.Function)
                throw new InvalidOperationException($"Incident capture entry {i + 1} differs from the script");
            var frames = new List<DecodedFrame> { ModbusCodec.Decode(exchange.Received, FrameDirection.Request) };
            string status; ushort[] values = Array.Empty<ushort>(); int? exceptionCode = null;
            if (exchange.Sent == null)
            {
                if (!timedOut[i]) throw new InvalidOperationException($"Incident capture entry {i + 1}: the simulator sent nothing but the client did not time out");
                status = "timeout";
            }
            else
            {
                if (timedOut[i]) throw new InvalidOperationException($"Incident capture entry {i + 1}: the client timed out but the simulator sent a reply");
                if (!ModbusCodec.TryParse(exchange.Sent, FrameDirection.Response, out var response, out var responseError) || response == null)
                    throw new InvalidOperationException($"Incident capture entry {i + 1} response did not decode: {responseError}");
                if (response.Header.TransactionId != step.TransactionId || response.Header.UnitId != step.Unit || (response.Function & 0x7F) != step.Function)
                    throw new InvalidOperationException($"Incident capture entry {i + 1} reply differs from the script");
                frames.Add(ModbusCodec.Decode(exchange.Sent, FrameDirection.Response, request.Address));
                if (response.ExceptionCode.HasValue) { status = "exception"; exceptionCode = response.ExceptionCode.Value; }
                else { status = "response"; values = step.Function == 6 ? new[] { response.QuantityOrValue!.Value } : response.Registers; }
            }
            entries.Add(new(i + 1, start.AddSeconds(step.OffsetSeconds).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture), step.Client,
                request.Header.TransactionId, request.Header.UnitId, request.Function, ModbusCodec.FunctionName(request.Function), request.Address!.Value, values, status, frames, exceptionCode));
        }
        return entries;
    }
}
