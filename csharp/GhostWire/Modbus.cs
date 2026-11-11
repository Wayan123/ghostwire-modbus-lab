using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace GhostWire;

public sealed class ModbusSimulator : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stopping = new();
    private readonly SemaphoreSlim slots = new(32);
    private readonly int unit;
    private readonly Dictionary<int, Dictionary<int, ushort>> registers;
    private readonly Task acceptLoop;
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    public ModbusSimulator(int unit, Dictionary<int, Dictionary<int, ushort>> registers)
    {
        this.unit = unit;
        this.registers = registers;
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
        catch (SocketException) when (stopping.IsCancellationRequested) { }
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
                if (header[6] != unit) { await Task.Delay(600, deadline.Token); return; }
                int function = pdu[0];
                byte[] reply;
                if (function is not (3 or 4)) reply = new byte[] { (byte)(function | 0x80), 1 };
                else if (pdu.Length != 5) reply = new byte[] { (byte)(function | 0x80), 3 };
                else
                {
                    int address = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(1));
                    int quantity = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(3));
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
                BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4), (ushort)(reply.Length + 1));
                await stream.WriteAsync(header, deadline.Token);
                await stream.WriteAsync(reply, deadline.Token);
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { }
        finally { slots.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        stopping.Cancel();
        listener.Stop();
        await acceptLoop;
        // In-flight handlers own their sockets and observe the cancellation token.
    }
}

public sealed record ModbusResult(string Status, ushort[] Values, int? ExceptionCode);

public static class ModbusClient
{
    public static async Task<ModbusResult> ReadAsync(int port, int unit, int function, int address, int quantity, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMilliseconds(300));
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port, deadline.Token);
            using var stream = client.GetStream();
            var request = new byte[12] { 0, 1, 0, 0, 0, 6, (byte)unit, (byte)function, 0, 0, 0, 0 };
            BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(8), (ushort)address);
            BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(10), (ushort)quantity);
            await stream.WriteAsync(request, deadline.Token);
            var header = new byte[7];
            await stream.ReadExactlyAsync(header, deadline.Token);
            int length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4));
            if (BinaryPrimitives.ReadUInt16BigEndian(header) != 1 || BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2)) != 0 || header[6] != unit || length < 2 || length > 254)
                return new("error", Array.Empty<ushort>(), null);
            var pdu = new byte[length - 1];
            await stream.ReadExactlyAsync(pdu, deadline.Token);
            if (pdu.Length == 2 && pdu[0] == (function | 0x80)) return new("exception", Array.Empty<ushort>(), pdu[1]);
            if (pdu.Length != 2 + quantity * 2 || pdu[0] != function || pdu[1] != quantity * 2) return new("error", Array.Empty<ushort>(), null);
            var values = new ushort[quantity];
            for (int i = 0; i < quantity; i++) values[i] = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(2 + 2 * i));
            return new("response", values, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return new("timeout", Array.Empty<ushort>(), null); }
        catch (Exception ex) when (ex is IOException or SocketException) { return new("error", Array.Empty<ushort>(), null); }
    }
}
