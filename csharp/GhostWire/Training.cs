using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace GhostWire;

public sealed record Setup(int unit_id, int function_code, int address, int quantity, double scale);
public sealed record TestRequest(string scenario_id, int unit_id, int function_code, int address, int quantity, double scale);
public sealed record AnswerRequest(string scenario_id, int transaction_id, string client);
public sealed record Scenario(string id, string title, string summary, Setup initial, string[] documentation, int unit, int function, int address, ushort value, double? requiredScale, string category, string difficulty, string? engineeringUnit);

public sealed class TrainingService : IAsyncDisposable
{
    private static readonly Scenario[] Cases = {
        new("pressure-scaling", "The Giant Pressure", "A pressure reading responds, but its engineering value looks wrong.", new(7,3,10,1,1), new[] { "Simulated device: unit 7, holding register offset 10.", "The simulated pressure register uses a multiplier of 0.01 bar per count." }, 7,3,10,39,0.01, "troubleshooting", "beginner", "bar"),
        new("unit-id", "The Silent Sensor", "A read times out despite a running simulated gateway.", new(1,4,0,1,1), new[] { "Simulated gateway routes this device at unit 17.", "The simulated device publishes its value at input register offset 0.", "In this simulation only, a unit mismatch is silently ignored." }, 17,4,0,73,null, "troubleshooting", "beginner", null),
        new("register-addressing", "The Wrong Register", "The device responds with an illegal data address exception.", new(3,4,21,1,1), new[] { "Simulated device: unit 3, input register offset 20.", "This exercise uses zero-based protocol offsets, not one-based register labels." }, 3,4,20,125,null, "troubleshooting", "intermediate", null),
        // The phantom-writer simulator is seeded from the captured final value, not from `value` (the documented normal setpoint).
        new(IncidentScript.ScenarioId, "Phantom Writer", "A simulated boiler setpoint changed without an approved work order.", new(5,3,100,1,0.1), new[] { "Simulated boiler controller: unit 5, holding register offset 100 is the temperature setpoint.", "The setpoint register uses 0.1 °C per count. The documented normal value is 650 (65.0 °C).", "A simulated traffic capture from the controller's network segment is available for this case." }, 5,3,100,IncidentScript.InitialSetpoint,0.1, "security", "intermediate", "°C")
    };
    private readonly Dictionary<string, ModbusSimulator> simulators = new();
    private readonly TrafficLog incidentLog;
    public static IReadOnlyList<Scenario> All => Cases;
    private static readonly HttpClient Provider = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(4) };

    public TrainingService()
    {
        try
        {
            foreach (var item in Cases.Where(c => c.category != "security"))
                simulators.Add(item.id, new ModbusSimulator(item.unit, new() { [item.function] = new() { [item.address] = item.value } }));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var (log, finalSetpoint) = IncidentLogGenerator.GenerateAsync(deadline.Token).GetAwaiter().GetResult();
            incidentLog = log;
            // Learner-facing and read-only: FC06 sent here returns exception 01.
            simulators.Add(IncidentScript.ScenarioId, new ModbusSimulator(IncidentScript.Unit, new() { [3] = new() { [IncidentScript.Register] = finalSetpoint } }));
        }
        catch (Exception ex)
        {
            foreach (var sim in simulators.Values) sim.DisposeAsync().AsTask().GetAwaiter().GetResult();
            if (ex is OperationCanceledException) throw new InvalidOperationException("Phantom Writer traffic log generation exceeded its 5 s startup deadline", ex);
            throw;
        }
    }

    // For the harness only: the port of a learner-facing, read-only simulator. Never exposed over HTTP.
    public int SimulatorPort(string scenarioId) => simulators[scenarioId].Port;

    public object Scenarios() => new { scenarios = Cases.Select(c => new { c.id, c.title, c.summary, c.initial, c.documentation, c.category, c.difficulty }).ToArray() };

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
        var diagnosis = Diagnosis.ForRead(scenario, request, response, value);
        var observations = diagnosis.Observations;
        var steps = diagnosis.NextSteps;
        var result = new Dictionary<string, object?> { ["status"] = response.Status, ["raw_registers"] = response.Values, ["engineering_value"] = value, ["observations"] = observations, ["possible_causes"] = diagnosis.PossibleCauses, ["training_hints"] = diagnosis.TrainingHints, ["next_steps"] = steps, ["verified"] = diagnosis.Verified, ["request"] = request };
        if (scenario.engineeringUnit != null) result["unit"] = scenario.engineeringUnit;
        result["frames"] = BuildFrames(response.Exchange, request.address);
        var (explanation, source) = await ExplainAsync(result, observations, steps, ct);
        result["explanation"] = explanation; result["explanation_source"] = source;
        return result;
    }

    // Frames come from the bytes actually written and read on the loopback socket, never from re-encoding the settings.
    public static List<DecodedFrame> BuildFrames(ModbusExchange exchange, int requestAddress)
    {
        var frames = new List<DecodedFrame>();
        if (exchange.Sent == null) return frames;
        frames.Add(ModbusCodec.Decode(exchange.Sent, FrameDirection.Request));
        if (exchange.Received != null) frames.Add(ModbusCodec.Decode(exchange.Received, FrameDirection.Response, requestAddress));
        return frames;
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

    public static readonly string[] Mitigations = {
        "Real-world guidance, not verified by this simulation: segment the control network into IEC 62443 zones and conduits so only approved systems can reach the controller.",
        "Real-world guidance, not verified by this simulation: allow-list write function codes (such as FC06 and FC16) per source on a Modbus-aware gateway or industrial firewall.",
        "Real-world guidance, not verified by this simulation: where devices support it, use Modbus/TCP Security (TLS with X.509 certificates that carry roles) so writes require an authorized identity.",
        "Real-world guidance, not verified by this simulation: log setpoint changes with source and time, and review them against change records."
    };

    private static bool IsSecurityCase(string? id) => id != null && Cases.Any(c => c.category == "security" && c.id == id);

    // scenario_id is matched against the fixed list of security cases; it never builds a path, host or URL.
    public TrafficLog? IncidentLog(string scenarioId) => scenarioId == IncidentScript.ScenarioId ? incidentLog : null;

    public static AnswerRequest ValidateAnswer(JsonElement payload)
    {
        string[] fields = { "scenario_id", "transaction_id", "client" };
        const string shape = "Expected exactly scenario_id, transaction_id and client";
        if (payload.ValueKind != JsonValueKind.Object) throw new ArgumentException(shape);
        var names = payload.EnumerateObject().Select(p => p.Name).ToArray();
        if (names.Length != fields.Length || names.Distinct(StringComparer.Ordinal).Count() != fields.Length || names.Except(fields, StringComparer.Ordinal).Any())
            throw new ArgumentException(shape);
        var scenario = payload.GetProperty("scenario_id");
        if (scenario.ValueKind != JsonValueKind.String || !IsSecurityCase(scenario.GetString())) throw new ArgumentException("scenario_id must be a security case");
        var tx = payload.GetProperty("transaction_id");
        if (tx.ValueKind != JsonValueKind.Number || !tx.TryGetInt32(out int transaction) || transaction is < 0 or > 65535) throw new ArgumentException("transaction_id is outside its allowed range");
        var clientElement = payload.GetProperty("client");
        string? client = clientElement.ValueKind == JsonValueKind.String ? clientElement.GetString() : null;
        if (client == null || !System.Text.RegularExpressions.Regex.IsMatch(client, "^[A-Za-z0-9._-]{1,32}$")) throw new ArgumentException("client must be 1-32 characters of A-Z, a-z, 0-9, '.', '_' or '-'");
        return new(scenario.GetString()!, transaction, client);
    }

    public async Task<Dictionary<string, object?>> AnswerAsync(AnswerRequest answer, CancellationToken ct)
    {
        var diagnosis = Diagnosis.ForAnswer(incidentLog, answer);
        var entry = incidentLog.entries.FirstOrDefault(e => e.transaction_id == answer.transaction_id);
        // An unverified answer must not reveal the writer's label, so the matched write entry is redacted.
        if (entry != null && !diagnosis.Verified && entry.function_code == 6) entry = entry with { client = "(not shown until the answer is verified)" };
        var result = new Dictionary<string, object?>
        {
            ["status"] = "answered", ["verified"] = diagnosis.Verified, ["answer"] = answer,
            ["observations"] = diagnosis.Observations, ["possible_causes"] = diagnosis.PossibleCauses, ["training_hints"] = diagnosis.TrainingHints,
            ["next_steps"] = diagnosis.NextSteps, ["mitigations"] = diagnosis.Verified ? Mitigations : Array.Empty<string>(), ["log_entry"] = entry
        };
        var (explanation, source) = await ExplainAsync(result, diagnosis.Observations, diagnosis.NextSteps, ct);
        result["explanation"] = explanation; result["explanation_source"] = source;
        return result;
    }
}

public sealed record DiagnosisResult(List<string> Observations, string[] PossibleCauses, string[] TrainingHints, string[] NextSteps, bool Verified);

// Pure diagnosis rules: no sockets, no provider. `Verified` is true only when deterministic rules confirm the
// expected scenario settings and a successful simulator response.
public static class Diagnosis
{
    public static DiagnosisResult ForRead(Scenario scenario, TestRequest request, ModbusResult response, double? value)
    {
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
        var hints = scenario.category == "security"
            ? new[] { "Training hint: " + scenario.documentation[0], "Training hint: a verified read confirms the current simulated setpoint, not who changed it. Open the simulated traffic log." }
            : new[] { "Training hint: " + scenario.documentation[0] };
        return new(observations, causes, hints, steps, verified);
    }

    public static DiagnosisResult ForAnswer(TrafficLog log, AnswerRequest answer)
    {
        var entry = log.entries.FirstOrDefault(e => e.transaction_id == answer.transaction_id);
        var mismatch = new[] { "The submitted pair does not match a write in this capture." };
        var retry = new[] { "Compare the function code of each entry in the simulated traffic log, then submit a transaction ID and client label." };
        string Describe(TrafficLogEntry e) => $"FC{e.function_code:D2} · {e.function_name} at address {e.address}";
        if (entry == null)
            return new(new() { $"No transaction {answer.transaction_id} appears in this simulated capture." }, mismatch, Array.Empty<string>(), retry, false);
        bool clientMatches = string.Equals(entry.client, answer.client.Trim(), StringComparison.OrdinalIgnoreCase);
        if (entry.function_code != 6)
            return new(new() { $"In this simulated capture, transaction {entry.transaction_id} from {entry.client} is {Describe(entry)}." }, mismatch, Array.Empty<string>(), retry, false);
        if (!clientMatches)
            return new(new() { $"In this simulated capture, transaction {entry.transaction_id} is {Describe(entry)}.", "The submitted client label differs from the capture's label for this transaction." },
                new[] { "The submitted client label does not match the capture metadata for this transaction." }, Array.Empty<string>(),
                new[] { "Re-read the client label column for this transaction in the simulated traffic log." }, false);

        // Verified: every value below is read from captured log entries, not from script constants.
        var before = log.entries.Where(e => e.sequence < entry.sequence && e.function_code is 3 or 4).OrderBy(e => e.sequence).LastOrDefault();
        var after = log.entries.Where(e => e.sequence > entry.sequence && e.function_code is 3 or 4).OrderBy(e => e.sequence).FirstOrDefault();
        var observations = new List<string>
        {
            $"In this simulated capture, transaction {entry.transaction_id} at {entry.time} from client label {entry.client} is function {entry.function_code:D2} ({entry.function_name}) to unit {entry.unit_id}, register {entry.address}, value {entry.values[0]}."
        };
        if (before != null) observations.Add($"Old value: the last simulated read before the write (sequence {before.sequence}, transaction {before.transaction_id}) returned {before.values[0]} at register {before.address}.");
        observations.Add($"New value: the simulated write echo returned {entry.values[0]}" + (after != null ? $", and the next read (sequence {after.sequence}, transaction {after.transaction_id}) returned {after.values[0]}." : "."));
        var others = log.entries.Where(e => e.sequence != entry.sequence).Select(e => e.client).Distinct().ToArray();
        observations.Add($"The other entries in this simulated capture come from client label(s): {string.Join(", ", others)}.");
        return new(observations, Array.Empty<string>(),
            new[] { "Training hint: Modbus TCP has no built-in authentication, so any client that can reach the device can send FC06 in this simulation." },
            new[] { "Compare the write time with approved change records." }, true);
    }
}
