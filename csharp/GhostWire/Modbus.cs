using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace GhostWire;

public sealed record SimulatorExchange(int RemotePort, byte[] Received, byte[]? Sent);
public interface IExchangeRecorder { void Record(SimulatorExchange exchange); }

public sealed class ModbusSimulator : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stopping = new();
    private readonly SemaphoreSlim slots = new(32);
    // unit ID -> function (3 or 4) -> register offset -> value. Unit IDs with no device stay silent.
    private readonly Dictionary<int, Dictionary<int, Dictionary<int, ushort>>> devices;
    private readonly bool allowWrites;
    private readonly IExchangeRecorder? recorder;
    private readonly object registerLock = new();
    private readonly Task acceptLoop;
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    // allowWrites is true only for the private Phantom Writer simulator used by IncidentLogGenerator at startup.
    // Every learner-reachable simulator keeps the default and answers FC06 with exception 01.
    public ModbusSimulator(int unit, Dictionary<int, Dictionary<int, ushort>> registers, bool allowWrites = false, IExchangeRecorder? recorder = null)
        : this(new Dictionary<int, Dictionary<int, Dictionary<int, ushort>>> { [unit] = registers }, allowWrites, recorder) { }

    // Gateway form: several unit IDs behind one loopback port (used by The Night Scanner).
    public ModbusSimulator(Dictionary<int, Dictionary<int, Dictionary<int, ushort>>> devices, bool allowWrites = false, IExchangeRecorder? recorder = null)
    {
        this.devices = devices;
        this.allowWrites = allowWrites;
        this.recorder = recorder;
        listener.Start();
        acceptLoop = AcceptAsync();
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stopping.Token);
                if (!slots.Wait(0)) { client.Dispose(); continue; }
                _ = ServeAsync(client);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when ((ex is SocketException or ObjectDisposedException) && stopping.IsCancellationRequested) { }
    }

    private async Task ServeAsync(TcpClient client)
    {
        try
        {
            using (client)
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(1));
                var stream = client.GetStream();
                var header = new byte[7];
                await stream.ReadExactlyAsync(header, deadline.Token);
                int protocol = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2));
                int length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4));
                if (protocol != 0 || length < 2 || length > 254) return;
                var pdu = new byte[length - 1];
                await stream.ReadExactlyAsync(pdu, deadline.Token);
                var received = header.Concat(pdu).ToArray();
                int remotePort = client.Client.RemoteEndPoint is IPEndPoint remote ? remote.Port : 0;
                if (!devices.TryGetValue(header[6], out var registers)) { Record(remotePort, received, null); await Task.Delay(600, deadline.Token); return; }
                int function = pdu[0];
                byte[] reply;
                if (function == 6 && allowWrites)
                {
                    if (pdu.Length != 5) reply = new byte[] { 0x86, 3 };
                    else
                    {
                        int address = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(1));
                        ushort value = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(3));
                        bool written = false;
                        lock (registerLock)
                        {
                            if (registers.TryGetValue(3, out var holding) && holding.ContainsKey(address)) { holding[address] = value; written = true; }
                        }
                        reply = written ? (byte[])pdu.Clone() : new byte[] { 0x86, 2 };
                    }
                }
                else if (function is not (3 or 4)) reply = new byte[] { (byte)(function | 0x80), 1 };
                else if (pdu.Length != 5) reply = new byte[] { (byte)(function | 0x80), 3 };
                else
                {
                    int address = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(1));
                    int quantity = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(3));
                    lock (registerLock)
                    {
                        if (quantity is < 1 or > 125) reply = new byte[] { (byte)(function | 0x80), 3 };
                        else if (address + quantity > 65536 || !registers.TryGetValue(function, out var map) || Enumerable.Range(address, quantity).Any(i => !map.ContainsKey(i)))
                            reply = new byte[] { (byte)(function | 0x80), 2 };
                        else
                        {
                            reply = new byte[2 + 2 * quantity];
                            reply[0] = (byte)function; reply[1] = (byte)(2 * quantity);
                            for (int i = 0; i < quantity; i++) BinaryPrimitives.WriteUInt16BigEndian(reply.AsSpan(2 + 2 * i), map[address + i]);
                        }
                    }
                }
                BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4), (ushort)(reply.Length + 1));
                await stream.WriteAsync(header, deadline.Token);
                await stream.WriteAsync(reply, deadline.Token);
                Record(remotePort, received, header.Concat(reply).ToArray());
                // One request per connection. Close gracefully: unread pipelined bytes would make Dispose send a RST and the client could lose the reply.
                client.Client.Shutdown(SocketShutdown.Send);
                using var drain = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
                drain.CancelAfter(TimeSpan.FromMilliseconds(200));
                var sink = new byte[260];
                int drained = 0;
                while (drained < sink.Length)
                {
                    int n = await stream.ReadAsync(sink.AsMemory(0, sink.Length - drained), drain.Token);
                    if (n == 0) break;
                    drained += n;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { }
        finally { slots.Release(); }
    }

    // Recording must never change protocol behavior, so recorder exceptions are ignored.
    private void Record(int remotePort, byte[] received, byte[]? sent)
    {
        if (recorder == null) return;
        try { recorder.Record(new(remotePort, received, sent)); } catch { }
    }

    public async ValueTask DisposeAsync()
    {
        stopping.Cancel();
        listener.Stop();
        await acceptLoop;
        // In-flight handlers own their sockets and observe the cancellation token.
    }
}

public enum FrameDirection { Request, Response }
public sealed record ByteRange(int offset, int length);
public sealed record FrameField(string name, string value, ByteRange bytes);
public sealed record DecodedFrame(string direction, string hex, IReadOnlyList<FrameField> fields, string? decode_error);
public sealed record Mbap(ushort TransactionId, ushort ProtocolId, ushort Length, byte UnitId);
public sealed record ParsedAdu(Mbap Header, byte Function, ushort? Address, ushort? QuantityOrValue, byte? ByteCount, ushort[] Registers, byte? ExceptionCode);

// Pure Modbus TCP encode/decode for FC03, FC04, FC06 and exception responses. No I/O.
public static class ModbusCodec
{
    public static byte[] EncodeReadRequest(ushort tx, byte unit, byte function, ushort address, ushort quantity)
    {
        if (function is not (3 or 4)) throw new ArgumentOutOfRangeException(nameof(function), "Only FC03 and FC04 reads are supported");
        if (quantity is < 1 or > 125) throw new ArgumentOutOfRangeException(nameof(quantity));
        if (address + quantity > 65536) throw new ArgumentOutOfRangeException(nameof(address));
        var pdu = new byte[5];
        pdu[0] = function;
        BinaryPrimitives.WriteUInt16BigEndian(pdu.AsSpan(1), address);
        BinaryPrimitives.WriteUInt16BigEndian(pdu.AsSpan(3), quantity);
        return Adu(tx, unit, pdu);
    }

    public static byte[] EncodeWriteSingleRequest(ushort tx, byte unit, ushort address, ushort value)
    {
        var pdu = new byte[5];
        pdu[0] = 6;
        BinaryPrimitives.WriteUInt16BigEndian(pdu.AsSpan(1), address);
        BinaryPrimitives.WriteUInt16BigEndian(pdu.AsSpan(3), value);
        return Adu(tx, unit, pdu);
    }

    public static byte[] EncodeReadResponse(ushort tx, byte unit, byte function, ReadOnlySpan<ushort> registers)
    {
        if (function is not (3 or 4)) throw new ArgumentOutOfRangeException(nameof(function), "Only FC03 and FC04 reads are supported");
        if (registers.Length is < 1 or > 125) throw new ArgumentOutOfRangeException(nameof(registers));
        var pdu = new byte[2 + 2 * registers.Length];
        pdu[0] = function; pdu[1] = (byte)(2 * registers.Length);
        for (int i = 0; i < registers.Length; i++) BinaryPrimitives.WriteUInt16BigEndian(pdu.AsSpan(2 + 2 * i), registers[i]);
        return Adu(tx, unit, pdu);
    }

    public static byte[] EncodeWriteSingleResponse(ushort tx, byte unit, ushort address, ushort value) => EncodeWriteSingleRequest(tx, unit, address, value);

    public static byte[] EncodeException(ushort tx, byte unit, byte function, byte code)
    {
        if (function is < 1 or > 127) throw new ArgumentOutOfRangeException(nameof(function));
        return Adu(tx, unit, new[] { (byte)(function | 0x80), code });
    }

    private static byte[] Adu(ushort tx, byte unit, byte[] pdu)
    {
        var adu = new byte[7 + pdu.Length];
        BinaryPrimitives.WriteUInt16BigEndian(adu, tx);
        BinaryPrimitives.WriteUInt16BigEndian(adu.AsSpan(4), (ushort)(pdu.Length + 1));
        adu[6] = unit;
        pdu.CopyTo(adu, 7);
        return adu;
    }

    public static string Hex(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0) return "";
        const string digits = "0123456789ABCDEF";
        var chars = new char[bytes.Length * 3 - 1];
        for (int i = 0; i < bytes.Length; i++)
        {
            if (i > 0) chars[3 * i - 1] = ' ';
            chars[3 * i] = digits[bytes[i] >> 4];
            chars[3 * i + 1] = digits[bytes[i] & 0xF];
        }
        return new string(chars);
    }

    public static string FunctionName(int function) => function switch
    {
        3 => "Read Holding Registers",
        4 => "Read Input Registers",
        6 => "Write Single Register",
        _ => "Unsupported in this simulation"
    };

    public static string ExceptionName(int code) => code switch
    {
        1 => "Illegal Function",
        2 => "Illegal Data Address",
        3 => "Illegal Data Value",
        4 => "Server Device Failure",
        5 => "Acknowledge",
        6 => "Server Device Busy",
        8 => "Memory Parity Error",
        0x0A => "Gateway Path Unavailable",
        0x0B => "Gateway Target Device Failed to Respond",
        _ => "Unknown exception code"
    };

    public static DecodedFrame Decode(ReadOnlySpan<byte> adu, FrameDirection direction) => Decode(adu, direction, null);

    public static DecodedFrame Decode(ReadOnlySpan<byte> adu, FrameDirection direction, int? requestAddress)
    {
        var fields = new List<FrameField>();
        Walk(adu, direction, requestAddress, fields, out _, out var error);
        return new(direction == FrameDirection.Request ? "request" : "response", Hex(adu), fields, error);
    }

    public static bool TryParse(ReadOnlySpan<byte> adu, FrameDirection direction, out ParsedAdu? parsed, out string? error)
    {
        Walk(adu, direction, null, null, out parsed, out error);
        return error == null;
    }

    private static string Word(ushort value) => $"{value} (0x{value:X4})";

    // Walks fields in byte order. Stops at the first failure. Never throws on input bytes.
    private static void Walk(ReadOnlySpan<byte> adu, FrameDirection direction, int? requestAddress, List<FrameField>? fields, out ParsedAdu? parsed, out string? error)
    {
        parsed = null;
        void Add(string name, string value, int offset, int length) => fields?.Add(new(name, value, new(offset, length)));
        if (adu.Length < 2) { error = $"Truncated MBAP header: {adu.Length} of 7 bytes"; return; }
        ushort tx = BinaryPrimitives.ReadUInt16BigEndian(adu);
        Add("Transaction ID", Word(tx), 0, 2);
        if (adu.Length < 4) { error = $"Truncated MBAP header: {adu.Length} of 7 bytes"; return; }
        ushort protocol = BinaryPrimitives.ReadUInt16BigEndian(adu[2..]);
        Add("Protocol ID", Word(protocol) + (protocol == 0 ? " · Modbus" : " · not Modbus"), 2, 2);
        if (protocol != 0) { error = $"Protocol ID {protocol} is not 0 (Modbus)"; return; }
        if (adu.Length < 6) { error = $"Truncated MBAP header: {adu.Length} of 7 bytes"; return; }
        ushort length = BinaryPrimitives.ReadUInt16BigEndian(adu[4..]);
        Add("Length", $"{length} bytes follow", 4, 2);
        if (length is < 2 or > 254) { error = $"Length {length} is outside 2–254"; return; }
        if (adu.Length - 6 < length) { error = $"Truncated frame: length says {length} bytes follow but {adu.Length - 6} are present"; return; }
        if (adu.Length - 6 > length) { error = $"Frame has {adu.Length - 6 - length} byte(s) beyond the declared length"; return; }
        byte unit = adu[6];
        Add("Unit ID", unit.ToString(CultureInfo.InvariantCulture), 6, 1);
        var header = new Mbap(tx, protocol, length, unit);
        byte fc = adu[7];
        var data = adu[8..];
        if ((fc & 0x80) != 0)
        {
            int baseFc = fc & 0x7F;
            Add("Function code", $"{fc:X2} · Exception for {baseFc:X2}", 7, 1);
            if (direction == FrameDirection.Request) { error = "An exception function code is not valid in a request"; return; }
            if (baseFc == 0) { error = "Function code 0 is not valid"; return; }
            if (data.Length != 1) { error = $"Exception response must have 1 exception code byte, found {data.Length}"; return; }
            Add("Exception code", $"{data[0]:X2} · {ExceptionName(data[0])}", 8, 1);
            parsed = new(header, fc, null, null, null, Array.Empty<ushort>(), data[0]);
            error = null; return;
        }
        if (fc is 3 or 4)
        {
            Add("Function code", $"{fc:D2} · {FunctionName(fc)}", 7, 1);
            if (direction == FrameDirection.Request)
            {
                if (data.Length != 4) { error = $"FC{fc:D2} request must have 4 data bytes, found {data.Length}"; return; }
                ushort address = BinaryPrimitives.ReadUInt16BigEndian(data), quantity = BinaryPrimitives.ReadUInt16BigEndian(data[2..]);
                Add("Start address", Word(address) + " · zero-based offset", 8, 2);
                Add("Quantity", $"{quantity} register(s)", 10, 2);
                if (quantity is < 1 or > 125) { error = $"Quantity {quantity} is outside 1–125"; return; }
                if (address + quantity > 65536) { error = "Start address plus quantity exceeds 65536"; return; }
                parsed = new(header, fc, address, quantity, null, Array.Empty<ushort>(), null);
                error = null; return;
            }
            if (data.Length < 1) { error = "Truncated response: byte count missing"; return; }
            byte count = data[0];
            Add("Byte count", count.ToString(CultureInfo.InvariantCulture), 8, 1);
            if (count != data.Length - 1) { error = $"Byte count {count} does not match the {data.Length - 1} data bytes present"; return; }
            if (count % 2 != 0 || count is < 2 or > 250) { error = $"Byte count {count} must be even and 2–250"; return; }
            var registers = new ushort[count / 2];
            for (int i = 0; i < registers.Length; i++)
            {
                registers[i] = BinaryPrimitives.ReadUInt16BigEndian(data[(1 + 2 * i)..]);
                string name = requestAddress.HasValue ? $"Register {requestAddress.Value + i}" : $"Register {i + 1}";
                Add(name, Word(registers[i]), 9 + 2 * i, 2);
            }
            parsed = new(header, fc, null, null, count, registers, null);
            error = null; return;
        }
        if (fc == 6)
        {
            Add("Function code", $"06 · {FunctionName(6)}", 7, 1);
            if (data.Length != 4) { error = $"FC06 must have 4 data bytes, found {data.Length}"; return; }
            ushort address = BinaryPrimitives.ReadUInt16BigEndian(data), value = BinaryPrimitives.ReadUInt16BigEndian(data[2..]);
            Add("Register address", Word(address) + " · zero-based offset", 8, 2);
            Add("Register value", Word(value), 10, 2);
            parsed = new(header, fc, address, value, null, Array.Empty<ushort>(), null);
            error = null; return;
        }
        Add("Function code", $"{fc:X2} · {FunctionName(fc)}", 7, 1);
        error = $"Function code {fc:X2} is not supported in this simulation";
    }
}

// ClientStop is set when the client deliberately stopped reading, so Received holds only the bytes read so far.
public sealed record ModbusExchange(byte[]? Sent, byte[]? Received, string? ClientStop = null);
public sealed record ModbusResult(string Status, ushort[] Values, int? ExceptionCode, ModbusExchange Exchange);

public static class ModbusClient
{
    // Learner read path: FC03/FC04 only. Any other function is refused before a socket is opened.
    public static async Task<ModbusResult> ReadAsync(int port, int unit, int function, int address, int quantity, CancellationToken cancellationToken)
    {
        if (function is not (3 or 4)) throw new ArgumentOutOfRangeException(nameof(function), "ModbusClient sends only FC03 and FC04 reads");
        var request = ModbusCodec.EncodeReadRequest(1, (byte)unit, (byte)function, (ushort)address, (ushort)quantity);
        byte[]? sent = null, received = null;
        string? clientStop = null;
        ModbusResult Result(string status, ushort[] values, int? code) => new(status, values, code, new(sent, received, clientStop));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMilliseconds(300));
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port, deadline.Token);
            using var stream = client.GetStream();
            await stream.WriteAsync(request, deadline.Token);
            sent = request;
            var header = new byte[7];
            await stream.ReadExactlyAsync(header, deadline.Token);
            received = header;
            int length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4));
            var mismatches = new List<string>();
            if (BinaryPrimitives.ReadUInt16BigEndian(header) != 1) mismatches.Add("transaction ID");
            if (BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2)) != 0) mismatches.Add("protocol ID");
            if (header[6] != unit) mismatches.Add("unit ID");
            if (length < 2 || length > 254) mismatches.Add("length");
            if (mismatches.Count > 0)
            {
                clientStop = $"Client stopped after header: {string.Join(", ", mismatches)} mismatch. Only the 7 header bytes received were kept.";
                return Result("error", Array.Empty<ushort>(), null);
            }
            var pdu = new byte[length - 1];
            await stream.ReadExactlyAsync(pdu, deadline.Token);
            received = header.Concat(pdu).ToArray();
            if (pdu.Length == 2 && pdu[0] == (function | 0x80)) return Result("exception", Array.Empty<ushort>(), pdu[1]);
            if (pdu.Length != 2 + quantity * 2 || pdu[0] != function || pdu[1] != quantity * 2) return Result("error", Array.Empty<ushort>(), null);
            var values = new ushort[quantity];
            for (int i = 0; i < quantity; i++) values[i] = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(2 + 2 * i));
            return Result("response", values, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { received = null; return Result("timeout", Array.Empty<ushort>(), null); }
        catch (Exception ex) when (ex is IOException or SocketException) { return Result("error", Array.Empty<ushort>(), null); }
    }
}
