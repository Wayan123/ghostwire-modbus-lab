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

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
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
}

await using var app = GhostWireHost.Create(Array.Empty<string>(), 0);
await app.StartAsync();
var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
using var client = new HttpClient { BaseAddress = new Uri(address) };
var scenarios = await client.GetFromJsonAsync<JsonElement>("/api/scenarios");
Check(scenarios.GetProperty("scenarios").GetArrayLength() == 3, "three scenarios");
Check((await client.GetAsync("/")).StatusCode == HttpStatusCode.OK, "frontend served");
object P(string id, int unit, int fc, int addr, int qty, double scale) => new { scenario_id = id, unit_id = unit, function_code = fc, address = addr, quantity = qty, scale };
var pressure = await Post(client, P("pressure-scaling",7,3,10,1,1));
Check(pressure.GetProperty("raw_registers")[0].GetInt32() == 39 && !pressure.GetProperty("verified").GetBoolean(), "initial pressure");
pressure = await Post(client, P("pressure-scaling",7,3,10,1,0.01));
Check(pressure.GetProperty("verified").GetBoolean() && Math.Abs(pressure.GetProperty("engineering_value").GetDouble() - 0.39) < 1e-10 && pressure.GetProperty("unit").GetString() == "bar", "pressure correction");
var silent = await Post(client, P("unit-id",1,4,0,1,1));
Check(silent.GetProperty("status").GetString() == "timeout" && !silent.GetProperty("verified").GetBoolean(), "initial timeout");
silent = await Post(client, P("unit-id",17,4,0,1,1));
Check(silent.GetProperty("verified").GetBoolean() && silent.GetProperty("raw_registers")[0].GetInt32() == 73, "unit correction");
var wrong = await Post(client, P("register-addressing",3,4,21,1,1));
Check(wrong.GetProperty("status").GetString() == "exception", "initial exception");
wrong = await Post(client, P("register-addressing",3,4,20,1,1));
Check(wrong.GetProperty("verified").GetBoolean() && wrong.GetProperty("raw_registers")[0].GetInt32() == 125, "address correction");
Check(wrong.GetProperty("explanation_source").GetString() == "deterministic", "fallback without provider");
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
provider.Run(async context =>
{
    Check(context.Request.Path == "/chat/completions", "provider path");
    Check(context.Request.Headers.Authorization == "Bearer test-key", "provider auth");
    if (oversizedProviderResponse) await context.Response.WriteAsync(new string('x', 65537));
    else await context.Response.WriteAsJsonAsync(new { choices = new[] { new { message = new { content = "Advisory text" } } } });
});
await provider.StartAsync();
Environment.SetEnvironmentVariable("GHOSTWIRE_AI_BASE_URL", provider.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
Environment.SetEnvironmentVariable("GHOSTWIRE_AI_API_KEY", "test-key");
Environment.SetEnvironmentVariable("GHOSTWIRE_AI_MODEL", "test-model");
var advisory = await Post(client, P("pressure-scaling",7,3,10,1,0.01));
Check(advisory.GetProperty("explanation_source").GetString() == "openai-compatible" && advisory.GetProperty("raw_registers")[0].GetInt32() == 39, "provider cannot change protocol evidence");
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
Console.WriteLine("All GhostWire protocol and HTTP checks passed.");
