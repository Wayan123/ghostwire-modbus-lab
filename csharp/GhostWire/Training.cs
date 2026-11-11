using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace GhostWire;

public sealed record Setup(int unit_id, int function_code, int address, int quantity, double scale);
public sealed record TestRequest(string scenario_id, int unit_id, int function_code, int address, int quantity, double scale);
public sealed record Scenario(string id, string title, string summary, Setup initial, string[] documentation, int unit, int function, int address, ushort value, double? requiredScale);

public sealed class TrainingService : IAsyncDisposable
{
    private static readonly Scenario[] Cases = {
        new("pressure-scaling", "The Giant Pressure", "A pressure reading responds, but its engineering value looks wrong.", new(7,3,10,1,1), new[] { "Simulated device: unit 7, holding register offset 10.", "The simulated pressure register uses a multiplier of 0.01 bar per count." }, 7,3,10,39,0.01),
        new("unit-id", "The Silent Sensor", "A read times out despite a running simulated gateway.", new(1,4,0,1,1), new[] { "Simulated gateway routes this device at unit 17.", "The simulated device publishes its value at input register offset 0.", "In this simulation only, a unit mismatch is silently ignored." }, 17,4,0,73,null),
        new("register-addressing", "The Wrong Register", "The device responds with an illegal data address exception.", new(3,4,21,1,1), new[] { "Simulated device: unit 3, input register offset 20.", "This exercise uses zero-based protocol offsets, not one-based register labels." }, 3,4,20,125,null)
    };
    private readonly Dictionary<string, ModbusSimulator> simulators = new();
    private static readonly HttpClient Provider = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(4) };

    public TrainingService()
    {
        try
        {
            foreach (var item in Cases)
                simulators.Add(item.id, new ModbusSimulator(item.unit, new() { [item.function] = new() { [item.address] = item.value } }));
        }
        catch { foreach (var sim in simulators.Values) sim.DisposeAsync().AsTask().GetAwaiter().GetResult(); throw; }
    }

    public object Scenarios() => new { scenarios = Cases.Select(c => new { c.id, c.title, c.summary, c.initial, c.documentation }).ToArray() };

    public static TestRequest Validate(JsonElement payload)
    {
        string[] fields = { "scenario_id", "unit_id", "function_code", "address", "quantity", "scale" };
        if (payload.ValueKind != JsonValueKind.Object) throw new ArgumentException("Expected exactly scenario_id, unit_id, function_code, address, quantity and scale");
        var names = payload.EnumerateObject().Select(p => p.Name).ToArray();
        if (names.Length != fields.Length || names.Distinct(StringComparer.Ordinal).Count() != fields.Length || names.Except(fields, StringComparer.Ordinal).Any())
            throw new ArgumentException("Expected exactly scenario_id, unit_id, function_code, address, quantity and scale");
        var scenario = payload.GetProperty("scenario_id");
        if (scenario.ValueKind != JsonValueKind.String || !Cases.Any(c => c.id == scenario.GetString())) throw new ArgumentException("Unknown scenario_id");
        int Number(string name, int min, int max)
        {
            var n = payload.GetProperty(name);
            if (n.ValueKind != JsonValueKind.Number || !n.TryGetInt32(out int v) || v < min || v > max) throw new ArgumentException($"{name} is outside its allowed range");
            return v;
        }
        int unit = Number("unit_id", 1, 247), function = Number("function_code", 3, 4), address = Number("address", 0, 65535), quantity = Number("quantity", 1, 125);
        if (address + quantity > 65536) throw new ArgumentException("Invalid function or register range");
        var scaleElement = payload.GetProperty("scale");
        if (scaleElement.ValueKind != JsonValueKind.Number || !scaleElement.TryGetDouble(out double scale) || !double.IsFinite(scale) || scale <= 0 || scale > 1000000)
            throw new ArgumentException("scale must be finite, positive and at most 1000000");
        return new(scenario.GetString()!, unit, function, address, quantity, scale);
    }

    public async Task<Dictionary<string, object?>> TestAsync(TestRequest request, CancellationToken ct)
    {
        var scenario = Cases.Single(c => c.id == request.scenario_id);
        var response = await ModbusClient.ReadAsync(simulators[scenario.id].Port, request.unit_id, request.function_code, request.address, request.quantity, ct);
        double? value = response.Values.Length > 0 ? response.Values[0] * request.scale : null;
        bool verified = response.Status == "response" && request.unit_id == scenario.unit && request.function_code == scenario.function && request.address == scenario.address && request.quantity == 1 && (!scenario.requiredScale.HasValue || request.scale == scenario.requiredScale.Value);
        var observations = new List<string> { $"Simulated request: unit ID {request.unit_id}, function {request.function_code}, address {request.address}, quantity {request.quantity}." };
        string[] causes, steps;
        if (response.Status == "response")
        {
            observations.Add($"Simulated FC{request.function_code:D2} response returned {response.Values.Length} register(s): [{string.Join(", ", response.Values)}].");
            observations.Add($"The first raw register multiplied by the selected scale is {value!.Value.ToString("G", CultureInfo.InvariantCulture)}.");
            causes = verified ? Array.Empty<string>() : new[] { "A successful read does not by itself confirm scaling, register choice or interpretation." };
            steps = verified ? new[] { "The expected scenario settings and a successful response are verified." } : new[] { "Compare the setting and interpreted value with the simulated device documentation." };
        }
        else if (response.Status == "timeout")
        {
            observations.Add("The simulated Modbus request timed out without a response.");
            causes = new[] { "A unit ID mismatch is one possible cause; timeouts alone do not prove it.", "A connection issue or unavailable device could also cause a timeout." };
            steps = new[] { "Compare the unit ID with the simulated gateway documentation and retest." };
        }
        else if (response.Status == "exception")
        {
            observations.Add($"The simulated device returned Modbus exception code {response.ExceptionCode:D2}.");
            causes = response.ExceptionCode == 2 ? new[] { "The requested register range may not exist for this function." } : new[] { "The simulator rejected the request." };
            steps = new[] { "Compare function code and zero-based address with the simulated device documentation." };
        }
        else
        {
            observations.Add("The local Modbus transport failed or returned an invalid frame.");
            causes = new[] { "The simulator or local connection may be unavailable." };
            steps = new[] { "Restart the local application and retry." };
        }
        var result = new Dictionary<string, object?> { ["status"] = response.Status, ["raw_registers"] = response.Values, ["engineering_value"] = value, ["observations"] = observations, ["possible_causes"] = causes, ["training_hints"] = new[] { "Training hint: " + scenario.documentation[0] }, ["next_steps"] = steps, ["verified"] = verified, ["request"] = request };
        if (scenario.id == "pressure-scaling") result["unit"] = "bar";
        var (explanation, source) = await ExplainAsync(result, observations, steps, ct);
        result["explanation"] = explanation; result["explanation_source"] = source;
        return result;
    }

    private static async Task<(string, string)> ExplainAsync(Dictionary<string, object?> evidence, List<string> observations, string[] steps, CancellationToken ct)
    {
        string fallback = "This is simulated training evidence. " + string.Join(" ", observations) + " " + string.Join(" ", steps);
        string? rawBase = Environment.GetEnvironmentVariable("GHOSTWIRE_AI_BASE_URL")?.TrimEnd('/');
        string? key = Environment.GetEnvironmentVariable("GHOSTWIRE_AI_API_KEY");
        string? model = Environment.GetEnvironmentVariable("GHOSTWIRE_AI_MODEL");
        if (string.IsNullOrWhiteSpace(rawBase) || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(model) ||
            !Uri.TryCreate(rawBase, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https") || string.IsNullOrEmpty(uri.Host) || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            return (fallback, "deterministic");
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(4));
            var body = new { model, messages = new[] { new { role = "system", content = "Explain this simulated Modbus training evidence concisely. Never claim real-device access. Distinguish observed facts from hypotheses; do not change or invent protocol results." }, new { role = "user", content = JsonSerializer.Serialize(evidence) } }, temperature = 0.2 };
            using var request = new HttpRequestMessage(HttpMethod.Post, rawBase + "/chat/completions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var response = await Provider.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 65536) return (fallback, "deterministic");
            using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[4096];
            while (true)
            {
                int read = await stream.ReadAsync(chunk, deadline.Token);
                if (read == 0) break;
                if (buffer.Length + read > 65536) return (fallback, "deterministic");
                buffer.Write(chunk, 0, read);
            }
            using var document = JsonDocument.Parse(buffer.ToArray());
            var content = document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()?.Trim();
            if (!string.IsNullOrEmpty(content)) return (content[..Math.Min(content.Length, 4000)], "openai-compatible");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException or FormatException or IndexOutOfRangeException) { }
        return (fallback, "deterministic");
    }

    public async ValueTask DisposeAsync() { foreach (var simulator in simulators.Values) await simulator.DisposeAsync(); }
}
