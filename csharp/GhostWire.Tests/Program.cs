using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using GhostWire;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); CheckCount.Passed++; }
static async Task<JsonElement> Post(HttpClient client, object input)
{
    using var response = await client.PostAsJsonAsync("/api/test", input);
    var text = await response.Content.ReadAsStringAsync();
    Check(response.StatusCode == HttpStatusCode.OK, $"test failed: {response.StatusCode} {text}");
    return JsonDocument.Parse(text).RootElement.Clone();
}

Environment.SetEnvironmentVariable("GHOSTWIRE_AI_BASE_URL", null);
Environment.SetEnvironmentVariable("GHOSTWIRE_AI_API_KEY", null);
Environment.SetEnvironmentVariable("GHOSTWIRE_AI_MODEL", null);

// Exercise raw TCP independently of the HTTP client: valid reads, invalid addresses and forbidden writes.
await using (var simulator = new ModbusSimulator(7, new Dictionary<int, Dictionary<int, ushort>> { [3] = new() { [10] = 39 } }))
{
    async Task<byte[]> Exchange(byte[] packet)
    {
        using var tcp = new TcpClient();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await tcp.ConnectAsync(IPAddress.Loopback, simulator.Port, deadline.Token);
        using var stream = tcp.GetStream();
        await stream.WriteAsync(packet, deadline.Token);
        byte[] header = new byte[7];
        await stream.ReadExactlyAsync(header, deadline.Token);
        byte[] body = new byte[(header[4] << 8 | header[5]) - 1];
        await stream.ReadExactlyAsync(body, deadline.Token);
        return body;
    }
    Check((await Exchange(new byte[] { 0,1,0,0,0,6,7,3,0,10,0,1 })).SequenceEqual(new byte[] { 3,2,0,39 }), "FC03 raw response");
    Check((await Exchange(new byte[] { 0,1,0,0,0,6,7,3,0,11,0,1 })).SequenceEqual(new byte[] { 0x83,2 }), "illegal address");
    Check((await Exchange(new byte[] { 0,1,0,0,0,6,7,6,0,10,0,99 })).SequenceEqual(new byte[] { 0x86,1 }), "writes must be rejected");
    Check((await ModbusClient.ReadAsync(simulator.Port, 8, 3, 10, 1, CancellationToken.None)).Status == "timeout", "unit mismatch timeout");
    var captured = await ModbusClient.ReadAsync(simulator.Port, 7, 3, 10, 1, CancellationToken.None);
    Check(captured.Exchange.Sent != null && ModbusCodec.Hex(captured.Exchange.Sent) == "00 01 00 00 00 06 07 03 00 0A 00 01" && ModbusCodec.Hex(captured.Exchange.Received!) == "00 01 00 00 00 05 07 03 02 00 27", "client returns exchanged bytes");
    var silentCapture = await ModbusClient.ReadAsync(simulator.Port, 8, 3, 10, 1, CancellationToken.None);
    Check(silentCapture.Exchange.Sent != null && silentCapture.Exchange.Received == null, "timeout keeps sent bytes only");
    bool refused = false;
    try { await ModbusClient.ReadAsync(simulator.Port, 7, 6, 10, 1, CancellationToken.None); } catch (ArgumentOutOfRangeException) { refused = true; }
    Check(refused, "client refuses function 6");
}

// A loopback fake server replies with transaction ID 2 instead of 1. The client must stop after the header
// and the response frame must say so, not report a truncated simulator reply.
{
    var fake = new TcpListener(IPAddress.Loopback, 0);
    fake.Start();
    int fakePort = ((IPEndPoint)fake.LocalEndpoint).Port;
    var serve = Task.Run(async () =>
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var peer = await fake.AcceptTcpClientAsync(deadline.Token);
        using var stream = peer.GetStream();
        await stream.ReadExactlyAsync(new byte[12], deadline.Token);
        await stream.WriteAsync(new byte[] { 0,2,0,0,0,5,7,3,2,0,39 }, deadline.Token);
        try { await stream.ReadAsync(new byte[1], deadline.Token); } catch (Exception ex) when (ex is IOException or OperationCanceledException) { }
    });
    var mismatched = await ModbusClient.ReadAsync(fakePort, 7, 3, 10, 1, CancellationToken.None);
    await serve;
    fake.Stop();
    var mismatchFrames = TrainingService.BuildFrames(mismatched.Exchange, 10);
    Check(mismatched.Status == "error" && ModbusCodec.Hex(mismatched.Exchange.Received!) == "00 02 00 00 00 05 07", "header mismatch keeps the 7 header bytes received");
    Check(mismatchFrames.Count == 2 && mismatchFrames[1].decode_error == "Client stopped after header: transaction ID mismatch. Only the 7 header bytes received were kept.", $"header mismatch decode_error: {mismatchFrames.LastOrDefault()?.decode_error}");
}

// Simulator recorder and the allowWrites option. Writes here hit only an in-memory harness simulator.
{
    var recorder = new ListRecorder();
    await using var readOnly = new ModbusSimulator(5, new Dictionary<int, Dictionary<int, ushort>> { [3] = new() { [100] = 650 } }, recorder: recorder);
    bool refused = false;
    try { await ModbusClient.ReadAsync(readOnly.Port, 5, 6, 100, 1, CancellationToken.None); } catch (ArgumentOutOfRangeException) { refused = true; }
    await Task.Delay(50);
    Check(refused && recorder.Items.Length == 0, "function-6 client call sends nothing to the simulator");
    var read = await ModbusClient.ReadAsync(readOnly.Port, 5, 3, 100, 1, CancellationToken.None);
    var recorded = await recorder.WaitForAsync(1);
    Check(recorded.Length == 1 && recorded[0].Received.SequenceEqual(read.Exchange.Sent!) && recorded[0].Sent!.SequenceEqual(read.Exchange.Received!), "recorder sees the exchanged bytes once");
    recorder.Clear();
    recorder.Throw = true;
    var stillWorks = await ModbusClient.ReadAsync(readOnly.Port, 5, 3, 100, 1, CancellationToken.None);
    Check(stillWorks.Status == "response" && stillWorks.Values[0] == 650, "recorder exceptions do not affect the protocol");
    recorder.Throw = false;

    async Task<byte[]> Raw(int port, byte[] packet)
    {
        using var tcp = new TcpClient();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await tcp.ConnectAsync(IPAddress.Loopback, port, deadline.Token);
        using var stream = tcp.GetStream();
        await stream.WriteAsync(packet, deadline.Token);
        byte[] header = new byte[7];
        await stream.ReadExactlyAsync(header, deadline.Token);
        byte[] body = new byte[(header[4] << 8 | header[5]) - 1];
        await stream.ReadExactlyAsync(body, deadline.Token);
        return body;
    }
    Check((await Raw(readOnly.Port, ModbusCodec.EncodeWriteSingleRequest(9, 5, 100, 950))).SequenceEqual(new byte[] { 0x86, 1 }), "default simulator rejects FC06");
    Check((await ModbusClient.ReadAsync(readOnly.Port, 5, 3, 100, 1, CancellationToken.None)).Values[0] == 650, "rejected FC06 leaves the register unchanged");

    await using var writable = new ModbusSimulator(5, new Dictionary<int, Dictionary<int, ushort>> { [3] = new() { [100] = 650 } }, allowWrites: true);
    Check((await Raw(writable.Port, ModbusCodec.EncodeWriteSingleRequest(9, 5, 100, 950))).SequenceEqual(new byte[] { 6, 0, 100, 0x03, 0xB6 }), "write-enabled simulator echoes FC06");
    Check((await ModbusClient.ReadAsync(writable.Port, 5, 3, 100, 1, CancellationToken.None)).Values[0] == 950, "write-enabled simulator stores the value");
    Check((await Raw(writable.Port, ModbusCodec.EncodeWriteSingleRequest(9, 5, 101, 1))).SequenceEqual(new byte[] { 0x86, 2 }), "FC06 to a missing address returns exception 02");
}

// Codec examples from the steering Modbus facts: the Giant Pressure request and reply, and an exception reply.
{
    static string FieldValue(DecodedFrame frame, string name) => frame.fields.Single(f => f.name == name).value;
    var request = ModbusCodec.EncodeReadRequest(1, 7, 3, 10, 1);
    Check(ModbusCodec.Hex(request) == "00 01 00 00 00 06 07 03 00 0A 00 01", "codec encodes the Giant Pressure request");
    var decodedRequest = ModbusCodec.Decode(request, FrameDirection.Request);
    Check(decodedRequest.decode_error == null && decodedRequest.direction == "request", "codec decodes request");
    Check(string.Join("|", decodedRequest.fields.Select(f => f.name)) == "Transaction ID|Protocol ID|Length|Unit ID|Function code|Start address|Quantity", "request fields in byte order");
    Check(FieldValue(decodedRequest, "Transaction ID") == "1 (0x0001)" && FieldValue(decodedRequest, "Unit ID") == "7" && FieldValue(decodedRequest, "Function code") == "03 · Read Holding Registers"
        && FieldValue(decodedRequest, "Start address") == "10 (0x000A) · zero-based offset" && FieldValue(decodedRequest, "Quantity") == "1 register(s)", "request field values");
    var response = ModbusCodec.EncodeReadResponse(1, 7, 3, new ushort[] { 39 });
    Check(ModbusCodec.Hex(response) == "00 01 00 00 00 05 07 03 02 00 27", "codec encodes the Giant Pressure response");
    var decodedResponse = ModbusCodec.Decode(response, FrameDirection.Response, 10);
    Check(decodedResponse.decode_error == null && FieldValue(decodedResponse, "Byte count") == "2" && FieldValue(decodedResponse, "Register 10") == "39 (0x0027)", "response register named by absolute offset");
    Check(decodedResponse.fields.Last().bytes == new ByteRange(9, 2), "register byte range");
    var exception = ModbusCodec.Decode(new byte[] { 0, 1, 0, 0, 0, 3, 3, 0x84, 0x02 }, FrameDirection.Response);
    Check(exception.decode_error == null && FieldValue(exception, "Function code") == "84 · Exception for 04" && FieldValue(exception, "Exception code") == "02 · Illegal Data Address", "84 02 decodes as exception 02");
    var truncated = ModbusCodec.Decode(new byte[] { 0, 1, 0, 0, 0, 5, 7, 3, 2, 0 }, FrameDirection.Response);
    Check(truncated.decode_error != null && truncated.fields.Count == 3 && truncated.fields[^1].name == "Length", "truncated frame reports decode_error with fields so far");
    var badProtocol = ModbusCodec.Decode(new byte[] { 0, 1, 0, 9, 0, 6, 7, 3, 0, 10, 0, 1 }, FrameDirection.Request);
    Check(badProtocol.decode_error != null && badProtocol.fields.Count == 2, "wrong protocol ID stops decoding");
    Check(ModbusCodec.Hex(ModbusCodec.EncodeWriteSingleRequest(0x0457, 5, 100, 950)) == "04 57 00 00 00 06 05 06 00 64 03 B6", "FC06 encoding");
    bool threw = false;
    try { ModbusCodec.EncodeReadRequest(1, 7, 6, 0, 1); } catch (ArgumentOutOfRangeException) { threw = true; }
    Check(threw, "read encoder rejects function 6");
}

// Phantom Writer generator: a private write-enabled simulator, real loopback exchanges, validated capture.
{
    using var generatorDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var (log, final) = await IncidentLogGenerator.GenerateAsync(generatorDeadline.Token);
    Check(log.entries.Count == 12 && final == 950, "generator captured 12 entries ending at 950");
    var write = log.entries.Single(e => e.function_code == 6);
    Check(write.sequence == 8 && write.transaction_id == 1111 && write.client == "MAINT-LAPTOP-07" && write.time == "2026-10-31T02:13:33Z" && write.values.SequenceEqual(new ushort[] { 950 }), "generator write entry");
    Check(write.frames[0].hex == "04 57 00 00 00 06 05 06 00 64 03 B6" && write.frames[1].hex == write.frames[0].hex, "captured FC06 request and echo");
    Check(log.entries.Take(7).All(e => e.values[0] == 650) && log.entries.Skip(8).All(e => e.values[0] == 950), "values before and after the write");
}

Console.WriteLine($"PBT seed: {Pbt.BaseSeed} (0x{Pbt.BaseSeed:X}){(Environment.GetEnvironmentVariable("GHOSTWIRE_PBT_SEED") is { Length: > 0 } ? " from GHOSTWIRE_PBT_SEED" : " (default)")}");
// PBT_PROPERTIES
static void CheckTiles(DecodedFrame frame, int length, string context)
{
    int next = 0;
    foreach (var field in frame.fields)
    {
        Check(field.bytes.offset == next && field.bytes.length > 0, $"{context}: field '{field.name}' at {field.bytes.offset} does not follow byte {next}");
        next += field.bytes.length;
    }
    Check(next == length, $"{context}: fields cover {next} of {length} bytes");
}
static byte[] Reencode(ParsedAdu p, FrameDirection direction)
{
    var h = p.Header;
    if (p.ExceptionCode.HasValue) return ModbusCodec.EncodeException(h.TransactionId, h.UnitId, (byte)(p.Function & 0x7F), p.ExceptionCode.Value);
    if (p.Function == 6) return ModbusCodec.EncodeWriteSingleRequest(h.TransactionId, h.UnitId, p.Address!.Value, p.QuantityOrValue!.Value);
    return direction == FrameDirection.Request
        ? ModbusCodec.EncodeReadRequest(h.TransactionId, h.UnitId, p.Function, p.Address!.Value, p.QuantityOrValue!.Value)
        : ModbusCodec.EncodeReadResponse(h.TransactionId, h.UnitId, p.Function, p.Registers);
}

await Pbt.Property("P-a codec round-trip", 600, (rng, c) =>
{
    ushort tx = (ushort)rng.NextInt(0, 65535);
    byte unit = (byte)rng.NextInt(0, 255);
    string kind = rng.Pick(new[] { "read-request", "read-response", "write-request", "write-response", "exception" });
    byte[] adu; FrameDirection direction; Action<ParsedAdu> expect;
    switch (kind)
    {
        case "read-request":
        {
            byte fc = (byte)rng.Pick(new[] { 3, 4 });
            int address = rng.NextInt(0, 65535), quantity = rng.NextInt(1, Math.Min(125, 65536 - address));
            c.Input = $"{kind} tx={tx} unit={unit} fc={fc} address={address} quantity={quantity}";
            adu = ModbusCodec.EncodeReadRequest(tx, unit, fc, (ushort)address, (ushort)quantity); direction = FrameDirection.Request;
            expect = p => Check(p.Function == fc && p.Address == address && p.QuantityOrValue == quantity && p.Registers.Length == 0 && p.ExceptionCode == null, "read request fields");
            break;
        }
        case "read-response":
        {
            byte fc = (byte)rng.Pick(new[] { 3, 4 });
            var registers = Enumerable.Range(0, rng.NextInt(1, 125)).Select(_ => (ushort)rng.NextInt(0, 65535)).ToArray();
            c.Input = $"{kind} tx={tx} unit={unit} fc={fc} registers=[{string.Join(",", registers)}]";
            adu = ModbusCodec.EncodeReadResponse(tx, unit, fc, registers); direction = FrameDirection.Response;
            expect = p => Check(p.Function == fc && p.ByteCount == 2 * registers.Length && p.Registers.SequenceEqual(registers) && p.ExceptionCode == null, "read response fields");
            break;
        }
        case "write-request":
        case "write-response":
        {
            int address = rng.NextInt(0, 65535), value = rng.NextInt(0, 65535);
            c.Input = $"{kind} tx={tx} unit={unit} address={address} value={value}";
            bool isRequest = kind == "write-request";
            adu = isRequest ? ModbusCodec.EncodeWriteSingleRequest(tx, unit, (ushort)address, (ushort)value) : ModbusCodec.EncodeWriteSingleResponse(tx, unit, (ushort)address, (ushort)value);
            direction = isRequest ? FrameDirection.Request : FrameDirection.Response;
            expect = p => Check(p.Function == 6 && p.Address == address && p.QuantityOrValue == value && p.ExceptionCode == null, "FC06 fields");
            break;
        }
        default:
        {
            byte fc = (byte)rng.Pick(new[] { 3, 4, 6 });
            byte code = (byte)rng.NextInt(1, 255);
            c.Input = $"{kind} tx={tx} unit={unit} fc={fc} code={code}";
            adu = ModbusCodec.EncodeException(tx, unit, fc, code); direction = FrameDirection.Response;
            expect = p => Check(p.Function == (fc | 0x80) && p.ExceptionCode == code && p.Address == null, "exception fields");
            break;
        }
    }
    c.Input += $" hex={ModbusCodec.Hex(adu)}";
    Check(ModbusCodec.TryParse(adu, direction, out var parsed, out var error), $"TryParse failed: {error}");
    Check(parsed!.Header == new Mbap(tx, 0, (ushort)(adu.Length - 6), unit), "MBAP header round-trip");
    expect(parsed);
    var decoded = ModbusCodec.Decode(adu, direction);
    Check(decoded.decode_error == null, $"Decode error: {decoded.decode_error}");
    CheckTiles(decoded, adu.Length, "P-a");
    Check(decoded.hex.Length == 3 * adu.Length - 1, "hex length is 3n-1");
    return () => Task.CompletedTask;
});

await Pbt.Property("P-b decoder never throws", 600, (rng, c) =>
{
    bool prefixed = rng.NextInt(0, 1) == 1;
    int length = prefixed ? rng.NextInt(8, 260) : rng.NextInt(0, 300);
    var bytes = rng.NextBytes(length);
    if (prefixed)
    {
        bytes[2] = 0; bytes[3] = 0;
        int declared = length - 6;
        bytes[4] = (byte)(declared >> 8); bytes[5] = (byte)declared;
        bytes[7] = (byte)rng.Pick(new[] { 3, 4, 6, 0x83, 0x84, 0x86, 1 });
        if (bytes[7] is 3 or 4 && length > 8 && rng.NextInt(0, 1) == 1) bytes[8] = (byte)(length - 9);
    }
    c.Input = $"length={length} prefixed={prefixed} hex={ModbusCodec.Hex(bytes)}";
    foreach (var direction in new[] { FrameDirection.Request, FrameDirection.Response })
    {
        var decoded = ModbusCodec.Decode(bytes, direction, rng.NextInt(0, 1) == 1 ? rng.NextInt(0, 65535) : null);
        Check(decoded.hex == ModbusCodec.Hex(bytes), "hex mirrors the input");
        foreach (var field in decoded.fields)
            Check(field.bytes.offset >= 0 && field.bytes.length > 0 && field.bytes.offset + field.bytes.length <= length, $"field '{field.name}' range outside the array");
        bool ok = ModbusCodec.TryParse(bytes, direction, out var parsed, out var error);
        Check(ok == (decoded.decode_error == null) && error == decoded.decode_error, "TryParse and Decode agree");
        if (ok) Check(Reencode(parsed!, direction).SequenceEqual(bytes), $"{direction}: a frame without decode_error re-encodes to the same bytes");
    }
    return () => Task.CompletedTask;
});

// Transport properties against one harness simulator: unit 9, FC03 registers 0–199, FC04 registers 0–49.
{
    var transportRecorder = new ListRecorder();
    await using var transport = new ModbusSimulator(9, new Dictionary<int, Dictionary<int, ushort>>
    {
        [3] = Enumerable.Range(0, 200).ToDictionary(i => i, i => (ushort)(i * 7 + 1)),
        [4] = Enumerable.Range(0, 50).ToDictionary(i => i, i => (ushort)(60000 - i)),
    }, recorder: transportRecorder);
    (int unit, int fc, int address, int quantity) RandomRead(Rng rng)
    {
        int unit = rng.NextInt(1, 10) <= 9 ? 9 : rng.NextInt(1, 247);
        int fc = rng.Pick(new[] { 3, 4 });
        int address = rng.NextInt(0, 260);
        int quantity = rng.NextInt(1, Math.Min(125, 65536 - address));
        return (unit, fc, address, quantity);
    }

    // P-e runs one case at a time so the single recorder entry belongs to this case.
    await Pbt.Property("P-e frames equal recorded wire bytes", 200, (rng, c) =>
    {
        var (unit, fc, address, quantity) = RandomRead(rng);
        c.Input = $"unit={unit} fc={fc} address={address} quantity={quantity}";
        return async () =>
        {
            transportRecorder.Clear();
            var result = await ModbusClient.ReadAsync(transport.Port, unit, fc, address, quantity, CancellationToken.None);
            var frames = TrainingService.BuildFrames(result.Exchange, address);
            var recorded = await transportRecorder.WaitForAsync(1);
            Check(recorded.Length == 1, $"simulator recorded {recorded.Length} exchanges");
            Check(frames.Count >= 1 && frames[0].hex == ModbusCodec.Hex(recorded[0].Received), "request frame hex equals the bytes the simulator received");
            if (result.Status == "timeout") Check(recorded[0].Sent == null && frames.Count == 1, "timeout: simulator sent nothing and only the request frame is shown");
            else Check(frames.Count == 2 && recorded[0].Sent != null && frames[1].hex == ModbusCodec.Hex(recorded[0].Sent), "response frame hex equals the bytes the simulator sent");
        };
    });

    await Pbt.Property("P-c transport status partition", 200, (rng, c) =>
    {
        var (unit, fc, address, quantity) = RandomRead(rng);
        c.Input = $"unit={unit} fc={fc} address={address} quantity={quantity}";
        return async () =>
        {
            var result = await ModbusClient.ReadAsync(transport.Port, unit, fc, address, quantity, CancellationToken.None);
            Check(result.Status is "response" or "exception" or "timeout", $"status '{result.Status}' outside the partition");
            int mapSize = fc == 3 ? 200 : 50;
            bool inMap = address + quantity <= mapSize;
            string expected = unit != 9 ? "timeout" : inMap ? "response" : "exception";
            Check(result.Status == expected, $"expected {expected}, got {result.Status}");
            if (result.Status == "response") Check(result.Values.Length == quantity, "response register count equals quantity");
            if (result.Status == "exception") Check(result.ExceptionCode == 2, $"exception code {result.ExceptionCode}, expected 02");
        };
    }, batch: 8);
}

var certainty = new System.Text.RegularExpressions.Regex(@"(?<!not |n't |never )\b(proves?|proven|confirms?|confirmed|certainly|definitely|the cause is)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
await Pbt.Property("P-d timeout diagnosis is never certain", 400, (rng, c) =>
{
    var scenario = rng.Pick(TrainingService.All.ToArray());
    int address = rng.NextInt(0, 65535);
    var request = new TestRequest(scenario.id, rng.NextInt(1, 247), rng.NextInt(3, 4), address, rng.NextInt(1, Math.Min(125, 65536 - address)), rng.Pick(new[] { 1, 0.1, 0.01, 1000000, 1e-9 }));
    c.Input = request.ToString();
    var sent = ModbusCodec.EncodeReadRequest(1, (byte)request.unit_id, (byte)request.function_code, (ushort)request.address, (ushort)request.quantity);
    var d = Diagnosis.ForRead(scenario, request, new ModbusResult("timeout", Array.Empty<ushort>(), null, new ModbusExchange(sent, null)), null);
    Check(!d.Verified, "timeout is never verified");
    Check(d.PossibleCauses.Length >= 2, "timeout lists at least two possible causes");
    Check(d.PossibleCauses.Any(cause => !cause.Contains("unit ID", StringComparison.OrdinalIgnoreCase)), "at least one cause does not mention the unit ID");
    foreach (var statement in d.Observations.Concat(d.PossibleCauses).Concat(d.NextSteps))
        Check(!certainty.IsMatch(statement), $"statement asserts a certain cause: {statement}");
    return () => Task.CompletedTask;
});

await using var app = GhostWireHost.Create(Array.Empty<string>(), 0);
await app.StartAsync();
var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
using var client = new HttpClient { BaseAddress = new Uri(address) };
var scenarios = await client.GetFromJsonAsync<JsonElement>("/api/scenarios");
Check(scenarios.GetProperty("scenarios").GetArrayLength() == 4, "four scenarios");
foreach (var (id, category, difficulty) in new[] { ("pressure-scaling", "troubleshooting", "beginner"), ("unit-id", "troubleshooting", "beginner"), ("register-addressing", "troubleshooting", "intermediate"), ("phantom-writer", "security", "intermediate") })
{
    var entry = scenarios.GetProperty("scenarios").EnumerateArray().Single(s => s.GetProperty("id").GetString() == id);
    Check(entry.GetProperty("category").GetString() == category && entry.GetProperty("difficulty").GetString() == difficulty, $"{id} category and difficulty");
    foreach (var name in new[] { "title", "summary", "initial", "documentation" }) Check(entry.TryGetProperty(name, out _), $"{id} keeps {name}");
}
Check((await client.GetAsync("/")).StatusCode == HttpStatusCode.OK, "frontend served");
object P(string id, int unit, int fc, int addr, int qty, double scale) => new { scenario_id = id, unit_id = unit, function_code = fc, address = addr, quantity = qty, scale };
var pressure = await Post(client, P("pressure-scaling",7,3,10,1,1));
Check(pressure.GetProperty("raw_registers")[0].GetInt32() == 39 && !pressure.GetProperty("verified").GetBoolean(), "initial pressure");
pressure = await Post(client, P("pressure-scaling",7,3,10,1,0.01));
Check(pressure.GetProperty("verified").GetBoolean() && Math.Abs(pressure.GetProperty("engineering_value").GetDouble() - 0.39) < 1e-10 && pressure.GetProperty("unit").GetString() == "bar", "pressure correction");
var pressureFrames = pressure.GetProperty("frames");
Check(pressureFrames.GetArrayLength() == 2 && pressureFrames[0].GetProperty("direction").GetString() == "request" && pressureFrames[0].GetProperty("hex").GetString() == "00 01 00 00 00 06 07 03 00 0A 00 01", "Giant Pressure request frame hex");
Check(pressureFrames[1].GetProperty("direction").GetString() == "response" && pressureFrames[1].GetProperty("hex").GetString() == "00 01 00 00 00 05 07 03 02 00 27" && pressureFrames[1].GetProperty("decode_error").ValueKind == JsonValueKind.Null, "Giant Pressure response frame hex");
string FrameField(JsonElement frame, string name) => frame.GetProperty("fields").EnumerateArray().Single(f => f.GetProperty("name").GetString() == name).GetProperty("value").GetString()!;
Check(FrameField(pressureFrames[0], "Unit ID") == "7" && FrameField(pressureFrames[1], "Register 10") == "39 (0x0027)", "Giant Pressure decoded fields");
var registerField = pressureFrames[1].GetProperty("fields").EnumerateArray().Last().GetProperty("bytes");
Check(registerField.GetProperty("offset").GetInt32() == 9 && registerField.GetProperty("length").GetInt32() == 2, "frame field byte range shape");
var silent = await Post(client, P("unit-id",1,4,0,1,1));
Check(silent.GetProperty("status").GetString() == "timeout" && !silent.GetProperty("verified").GetBoolean(), "initial timeout");
Check(silent.GetProperty("frames").GetArrayLength() == 1 && silent.GetProperty("frames")[0].GetProperty("direction").GetString() == "request", "timeout has only the request frame");
silent = await Post(client, P("unit-id",17,4,0,1,1));
Check(silent.GetProperty("verified").GetBoolean() && silent.GetProperty("raw_registers")[0].GetInt32() == 73, "unit correction");
var wrong = await Post(client, P("register-addressing",3,4,21,1,1));
Check(wrong.GetProperty("status").GetString() == "exception", "initial exception");
Check(wrong.GetProperty("frames").GetArrayLength() == 2 && FrameField(wrong.GetProperty("frames")[1], "Exception code") == "02 · Illegal Data Address", "Wrong Register response decodes exception 02");
wrong = await Post(client, P("register-addressing",3,4,20,1,1));
Check(wrong.GetProperty("verified").GetBoolean() && wrong.GetProperty("raw_registers")[0].GetInt32() == 125, "address correction");
Check(wrong.GetProperty("explanation_source").GetString() == "deterministic", "fallback without provider");
var phantom = await Post(client, P("phantom-writer",5,3,100,1,0.1));
Check(phantom.GetProperty("status").GetString() == "response" && phantom.GetProperty("raw_registers")[0].GetInt32() == 950 && Math.Abs(phantom.GetProperty("engineering_value").GetDouble() - 95.0) < 1e-10
    && phantom.GetProperty("unit").GetString() == "°C" && phantom.GetProperty("verified").GetBoolean(), "Phantom Writer read returns the captured setpoint");
Check(phantom.GetProperty("training_hints").EnumerateArray().Any(h => h.GetString()!.Contains("traffic log")), "Phantom Writer hint points to the traffic log");
Console.WriteLine($"Observed Phantom Writer read: raw {phantom.GetProperty("raw_registers")[0].GetInt32()}, value {phantom.GetProperty("engineering_value").GetDouble()} {phantom.GetProperty("unit").GetString()}, request {phantom.GetProperty("frames")[0].GetProperty("hex").GetString()}, response {phantom.GetProperty("frames")[1].GetProperty("hex").GetString()}");
var service = app.Services.GetRequiredService<TrainingService>();
{
    int phantomPort = service.SimulatorPort("phantom-writer");
    using var tcp = new TcpClient();
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
    await tcp.ConnectAsync(IPAddress.Loopback, phantomPort, deadline.Token);
    using var stream = tcp.GetStream();
    await stream.WriteAsync(ModbusCodec.EncodeWriteSingleRequest(1, 5, 100, 1), deadline.Token);
    byte[] header = new byte[7];
    await stream.ReadExactlyAsync(header, deadline.Token);
    byte[] body = new byte[(header[4] << 8 | header[5]) - 1];
    await stream.ReadExactlyAsync(body, deadline.Token);
    Check(body.SequenceEqual(new byte[] { 0x86, 1 }), "learner-facing Phantom Writer simulator rejects FC06 with 86 01");
    Check((await ModbusClient.ReadAsync(phantomPort, 5, 3, 100, 1, CancellationToken.None)).Values[0] == 950, "register 100 still reads 950 after the rejected write");
}

// Incident traffic log endpoint.
var trafficLog = await client.GetFromJsonAsync<JsonElement>("/api/incident/phantom-writer/log");
var logEntries = trafficLog.GetProperty("entries").EnumerateArray().ToArray();
Check(trafficLog.GetProperty("simulated").GetBoolean() && trafficLog.GetProperty("capture_note").GetString()!.Contains("carry no client identity"), "log is labeled as a simulated capture");
Check(logEntries.Length == 12, "log has 12 entries");
Check(logEntries.Select(e => e.GetProperty("transaction_id").GetInt32()).Distinct().Count() == 12, "log transaction IDs are unique");
Check(logEntries.Count(e => e.GetProperty("function_code").GetInt32() == 6) == 1, "log has exactly one FC06 entry");
Check(logEntries.Select(e => e.GetProperty("client").GetString()).Distinct().Count() == 2, "log has two client labels");
foreach (var entry in logEntries)
{
    var frames = entry.GetProperty("frames");
    var requestBytes = Convert.FromHexString(frames[0].GetProperty("hex").GetString()!.Replace(" ", ""));
    var responseBytes = Convert.FromHexString(frames[1].GetProperty("hex").GetString()!.Replace(" ", ""));
    bool requestOk = ModbusCodec.TryParse(requestBytes, FrameDirection.Request, out var rq, out _);
    bool responseOk = ModbusCodec.TryParse(responseBytes, FrameDirection.Response, out var rs, out _);
    Check(requestOk && responseOk, "log frames decode");
    int fc = entry.GetProperty("function_code").GetInt32();
    var values = entry.GetProperty("values").EnumerateArray().Select(v => (ushort)v.GetInt32()).ToArray();
    Check(rq!.Header.UnitId == entry.GetProperty("unit_id").GetInt32() && rq.Function == fc && rq.Address == entry.GetProperty("address").GetInt32() && rq.Header.TransactionId == entry.GetProperty("transaction_id").GetInt32(), $"log entry {entry.GetProperty("sequence").GetInt32()} request matches its fields");
    Check(fc == 6 ? rs!.QuantityOrValue == values.Single() && rs.Address == rq.Address : rs!.Registers.SequenceEqual(values), $"log entry {entry.GetProperty("sequence").GetInt32()} response matches its values");
}
{
    await using var first = new TrainingService();
    await using var second = new TrainingService();
    Check(JsonSerializer.Serialize(first.IncidentLog("phantom-writer")) == JsonSerializer.Serialize(second.IncidentLog("phantom-writer")), "traffic log is identical across independent starts");
}
using (var response = await client.GetAsync("/api/incident/unit-id/log")) Check(response.StatusCode == HttpStatusCode.NotFound, "log 404 for a troubleshooting case");
using (var response = await client.GetAsync("/api/incident/../log")) Check(response.StatusCode == HttpStatusCode.NotFound, "log 404 for a path-traversal id");
using (var response = await client.GetAsync("/api/incident/%2E%2E%2Fscenarios/log")) Check(response.StatusCode == HttpStatusCode.NotFound, "log 404 for an encoded traversal id");

// Incident answer endpoint.
async Task<(HttpStatusCode Status, JsonElement Body)> Answer(HttpContent content)
{
    using var response = await client.PostAsync("/api/incident/answer", content);
    var body = await response.Content.ReadAsStringAsync();
    return (response.StatusCode, JsonDocument.Parse(body).RootElement.Clone());
}
HttpContent AnswerJson(int tx, string label) => JsonContent.Create(new { scenario_id = "phantom-writer", transaction_id = tx, client = label });
var correct = await Answer(AnswerJson(1111, "MAINT-LAPTOP-07"));
Check(correct.Status == HttpStatusCode.OK && correct.Body.GetProperty("verified").GetBoolean() && correct.Body.GetProperty("mitigations").GetArrayLength() == 4, "correct answer verifies with four mitigations");
var correctText = string.Join(" ", correct.Body.GetProperty("observations").EnumerateArray().Select(o => o.GetString()));
foreach (var needle in new[] { "function 06", "unit 5", "register 100", "650", "950", "2026-10-31T02:13:33Z", "MAINT-LAPTOP-07" }) Check(correctText.Contains(needle), $"verified observations cite {needle}");
Check(correct.Body.GetProperty("mitigations").EnumerateArray().All(m => m.GetString()!.StartsWith("Real-world guidance, not verified by this simulation")), "mitigations are labeled guidance");
foreach (var (tx, label, name) in new[] { (1111, "HMI-01", "right transaction, wrong client"), (3, "HMI-01", "read transaction"), (4242, "X", "missing transaction") })
{
    var wrongAnswer = await Answer(AnswerJson(tx, label));
    Check(wrongAnswer.Status == HttpStatusCode.OK && !wrongAnswer.Body.GetProperty("verified").GetBoolean() && wrongAnswer.Body.GetProperty("mitigations").GetArrayLength() == 0, $"{name} is not verified and has no mitigations");
    Check(!wrongAnswer.Body.GetRawText().Contains("MAINT-LAPTOP-07"), $"{name} does not reveal the writer's label");
    Check(wrongAnswer.Body.GetProperty("observations").GetArrayLength() >= 1, $"{name} has an observation");
}
Check((await Answer(AnswerJson(4242, "X"))).Body.GetProperty("observations")[0].GetString() == "No transaction 4242 appears in this simulated capture.", "missing transaction observation");
foreach (var (body, name) in new[]
{
    ("{\"scenario_id\":\"phantom-writer\",\"transaction_id\":1111,\"client\":\"MAINT-LAPTOP-07\",\"host\":\"example.com\"}", "extra field"),
    ("{\"scenario_id\":\"phantom-writer\",\"transaction_id\":1111,\"transaction_id\":1111,\"client\":\"MAINT-LAPTOP-07\"}", "duplicate field"),
    ("{\"scenario_id\":\"phantom-writer\",\"transaction_id\":1111}", "missing field"),
    ("{\"scenario_id\":\"unit-id\",\"transaction_id\":1111,\"client\":\"MAINT-LAPTOP-07\"}", "non-security scenario"),
    ("{\"scenario_id\":\"phantom-writer\",\"transaction_id\":70000,\"client\":\"MAINT-LAPTOP-07\"}", "transaction_id out of range"),
    ("{\"scenario_id\":\"phantom-writer\",\"transaction_id\":\"1111\",\"client\":\"MAINT-LAPTOP-07\"}", "transaction_id as string"),
    ("{\"scenario_id\":\"phantom-writer\",\"transaction_id\":1111,\"client\":\"a b\"}", "client with a space"),
    ("{\"scenario_id\":\"phantom-writer\",\"transaction_id\":1111,\"client\":\"" + new string('A', 33) + "\"}", "33-character client"),
    ("not json", "invalid JSON"),
})
{
    var invalidAnswer = await Answer(new StringContent(body, Encoding.UTF8, "application/json"));
    Check(invalidAnswer.Status == HttpStatusCode.BadRequest && invalidAnswer.Body.TryGetProperty("error", out _), $"answer rejects {name}");
}
Check((await Answer(new StringContent("{}", Encoding.UTF8, "text/plain"))).Status == HttpStatusCode.UnsupportedMediaType, "answer rejects text/plain");
using (var request = new HttpRequestMessage(HttpMethod.Post, "/api/incident/answer") { Content = AnswerJson(1111, "MAINT-LAPTOP-07") })
{
    request.Headers.Add("Origin", "http://evil.example");
    using var response = await client.SendAsync(request);
    Check(response.StatusCode == HttpStatusCode.Forbidden, "answer rejects cross-origin");
}
using (var request = new HttpRequestMessage(HttpMethod.Get, "/api/incident/phantom-writer/log"))
{
    request.Headers.Host = "evil.example";
    using var response = await client.SendAsync(request);
    Check(response.StatusCode == HttpStatusCode.Forbidden, "log rejects foreign host");
}
using (var oversizedClient = new HttpClient { BaseAddress = new Uri(address) })
{
    // Kestrel may reset the connection before the client finishes sending 16 KiB+; that is also a rejection.
    bool rejected;
    try { using var response = await oversizedClient.PostAsync("/api/incident/answer", new StringContent(new string('x', 16385), Encoding.UTF8, "application/json")); rejected = (int)response.StatusCode is 400 or 413; }
    catch (HttpRequestException) { rejected = true; }
    Check(rejected, "answer rejects oversized request");
}
using (var invalid = await client.PostAsJsonAsync("/api/test", new { scenario_id = "unit-id", unit_id = 17, function_code = 4, address = 0, quantity = 1, scale = 1, host = "example.com" })) Check(invalid.StatusCode == HttpStatusCode.BadRequest, "reject unknown target");
using (var invalid = await client.PostAsJsonAsync("/api/test", P("unit-id",17,4,65535,2,1))) Check(invalid.StatusCode == HttpStatusCode.BadRequest, "reject overflow");
using (var invalid = await client.PostAsJsonAsync("/api/test", P("unit-id",17,6,0,1,1))) Check(invalid.StatusCode == HttpStatusCode.BadRequest, "reject write function");
using (var invalid = await client.PostAsync("/api/test", new StringContent("{\"scenario_id\":\"unit-id\",\"unit_id\":17,\"unit_id\":17,\"function_code\":4,\"address\":0,\"quantity\":1,\"scale\":1}", Encoding.UTF8, "application/json"))) Check(invalid.StatusCode == HttpStatusCode.BadRequest, "reject duplicate fields");
using (var invalid = await client.PostAsync("/api/test", new StringContent("{}", Encoding.UTF8, "text/plain"))) Check(invalid.StatusCode == HttpStatusCode.UnsupportedMediaType, "reject content type");
using (var request = new HttpRequestMessage(HttpMethod.Post, "/api/test") { Content = JsonContent.Create(P("unit-id",17,4,0,1,1)) })
{
    request.Headers.Add("Origin", "http://evil.example");
    using var response = await client.SendAsync(request);
    Check(response.StatusCode == HttpStatusCode.Forbidden, "reject cross-origin");
}
using (var request = new HttpRequestMessage(HttpMethod.Get, "/api/scenarios"))
{
    request.Headers.Host = "evil.example";
    using var response = await client.SendAsync(request);
    Check(response.StatusCode == HttpStatusCode.Forbidden, "reject host");
}
using (var response = await client.PostAsync("/api/test", new StringContent(new string('x', 16385), Encoding.UTF8, "application/json"))) Check((int)response.StatusCode is 400 or 413, "reject oversized request");
var providerBuilder = WebApplication.CreateBuilder();
providerBuilder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, 0));
await using var provider = providerBuilder.Build();
bool oversizedProviderResponse = false;
string lastProviderBody = "";
provider.Run(async context =>
{
    Check(context.Request.Path == "/chat/completions", "provider path");
    Check(context.Request.Headers.Authorization == "Bearer test-key", "provider auth");
    using (var reader = new StreamReader(context.Request.Body)) lastProviderBody = await reader.ReadToEndAsync();
    if (oversizedProviderResponse) await context.Response.WriteAsync(new string('x', 65537));
    else await context.Response.WriteAsJsonAsync(new { choices = new[] { new { message = new { content = "Advisory text: verified is false and the value is 1234." } } } });
});
await provider.StartAsync();
Environment.SetEnvironmentVariable("GHOSTWIRE_AI_BASE_URL", provider.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
Environment.SetEnvironmentVariable("GHOSTWIRE_AI_API_KEY", "test-key");
Environment.SetEnvironmentVariable("GHOSTWIRE_AI_MODEL", "test-model");
var advisory = await Post(client, P("pressure-scaling",7,3,10,1,0.01));
Check(advisory.GetProperty("explanation_source").GetString() == "openai-compatible" && advisory.GetProperty("raw_registers")[0].GetInt32() == 39, "provider cannot change protocol evidence");
Check(lastProviderBody.Contains("frames") && lastProviderBody.Contains("00 01 00 00 00 05 07 03 02 00 27"), "provider request body contains frames");
Check(advisory.GetProperty("verified").GetBoolean() && advisory.GetProperty("frames").GetRawText() == pressure.GetProperty("frames").GetRawText(), "provider text leaves verified and frames unchanged");
var advisoryAnswer = await Answer(AnswerJson(1111, "MAINT-LAPTOP-07"));
Check(advisoryAnswer.Status == HttpStatusCode.OK && advisoryAnswer.Body.GetProperty("explanation_source").GetString() == "openai-compatible", "answer uses the provider when configured");
Check(advisoryAnswer.Body.GetProperty("verified").GetBoolean() && advisoryAnswer.Body.GetProperty("mitigations").GetRawText() == correct.Body.GetProperty("mitigations").GetRawText()
    && advisoryAnswer.Body.GetProperty("log_entry").GetProperty("frames").GetRawText() == correct.Body.GetProperty("log_entry").GetProperty("frames").GetRawText(), "provider text leaves answer verified, mitigations and frames unchanged");
Check(lastProviderBody.Contains("log_entry") && lastProviderBody.Contains("frames"), "provider answer body contains the matched log entry evidence");
oversizedProviderResponse = true;
advisory = await Post(client, P("pressure-scaling",7,3,10,1,0.01));
Check(advisory.GetProperty("explanation_source").GetString() == "deterministic", "oversized provider fallback");
await provider.StopAsync();
bool rateLimited = false;
for (int i = 0; i < 125; i++)
{
    using var response = await client.GetAsync("/api/scenarios");
    if ((int)response.StatusCode == 429) { rateLimited = true; break; }
}
Check(rateLimited, "request rate is bounded");
await app.StopAsync();
foreach (var line in Pbt.Summary) Console.WriteLine(line);
Console.WriteLine($"PBT seed used: {Pbt.BaseSeed}");
Console.WriteLine($"Check calls passed: {CheckCount.Passed} (property checks count once per assertion per case)");
Console.WriteLine("All GhostWire protocol and HTTP checks passed.");

// SplitMix64: a small, fully specified PRNG so generated sequences do not depend on System.Random across .NET versions.
sealed class Rng
{
    private ulong state;
    public Rng(ulong seed) { state = seed; }
    public ulong Next()
    {
        ulong z = state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
    public int NextInt(int min, int maxInclusive) => (int)(min + (long)(Next() % (ulong)((long)maxInclusive - min + 1)));
    public byte[] NextBytes(int length) { var bytes = new byte[length]; for (int i = 0; i < length; i++) bytes[i] = (byte)Next(); return bytes; }
    public T Pick<T>(T[] items) => items[NextInt(0, items.Length - 1)];
}

sealed class PbtCase
{
    public int Index { get; init; }
    public string Input { get; set; } = "(input not recorded)";
}

static class Pbt
{
    public const ulong DefaultSeed = 0x6057_2026;
    public static readonly ulong BaseSeed = ReadSeed();
    public static readonly List<string> Summary = new();

    private static ulong ReadSeed()
    {
        var text = Environment.GetEnvironmentVariable("GHOSTWIRE_PBT_SEED");
        if (string.IsNullOrWhiteSpace(text)) return DefaultSeed;
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && ulong.TryParse(text[2..], System.Globalization.NumberStyles.HexNumber, null, out var hex)) return hex;
        if (ulong.TryParse(text, out var dec)) return dec;
        throw new Exception($"GHOSTWIRE_PBT_SEED must be a decimal or 0x-prefixed hex unsigned integer, got '{text}'");
    }

    private static ulong Fnv1a(string name)
    {
        ulong hash = 0xCBF29CE484222325UL;
        foreach (byte b in System.Text.Encoding.UTF8.GetBytes(name)) { hash ^= b; hash *= 0x100000001B3UL; }
        return hash;
    }

    // generate draws the case input synchronously from the shared Rng (so batching cannot change the sequence)
    // and returns the async check. Cases in a batch run concurrently.
    public static async Task Property(string name, int cases, Func<Rng, PbtCase, Func<Task>> generate, int batch = 1)
    {
        ulong seed = BaseSeed ^ Fnv1a(name);
        var rng = new Rng(seed);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (int start = 0; start < cases; start += batch)
        {
            var pending = new List<(PbtCase Case, Task Task)>();
            for (int i = start; i < Math.Min(cases, start + batch); i++)
            {
                var c = new PbtCase { Index = i };
                Task task;
                try { var check = generate(rng, c); task = check(); }
                catch (Exception ex) { task = Task.FromException(ex); }
                pending.Add((c, task));
            }
            foreach (var (c, task) in pending)
            {
                try { await task; }
                catch (Exception ex)
                {
                    Console.WriteLine($"Property '{name}' failed at case {c.Index} with seed {BaseSeed} (0x{BaseSeed:X}; property seed 0x{seed:X16}): {c.Input}");
                    Console.WriteLine($"Reproduce with GHOSTWIRE_PBT_SEED={BaseSeed}. Failure: {ex.Message}");
                    throw;
                }
            }
        }
        string line = $"PBT {name}: {cases} cases passed in {watch.ElapsedMilliseconds} ms";
        Summary.Add(line);
        Console.WriteLine(line);
    }
}

sealed class ListRecorder : IExchangeRecorder
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<SimulatorExchange> items = new();
    public bool Throw { get; set; }
    public void Record(SimulatorExchange exchange) { items.Enqueue(exchange); if (Throw) throw new InvalidOperationException("recorder failure"); }
    public SimulatorExchange[] Items => items.ToArray();
    public void Clear() => items.Clear();
    public async Task<SimulatorExchange[]> WaitForAsync(int count, int milliseconds = 1000)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (items.Count < count && watch.ElapsedMilliseconds < milliseconds) await Task.Delay(2);
        return Items;
    }
}

static class CheckCount { public static int Passed; }
