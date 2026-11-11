using System.Net;
using System.Text.Json;
using System.Threading.RateLimiting;
using GhostWire;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.FileProviders;

var app = GhostWireHost.Create(args);
await app.RunAsync();

namespace GhostWire
{
    public static class GhostWireHost
    {
        public static WebApplication Create(string[] args, int? port = null)
        {
            int selectedPort = port ?? PortFromEnvironment();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Listen(IPAddress.Loopback, selectedPort);
                options.Limits.MaxRequestBodySize = 16384;
                options.Limits.MaxConcurrentConnections = 64;
                options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
            });
            builder.Services.AddSingleton<TrainingService>();
            builder.Services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    RateLimitPartition.GetFixedWindowLimiter("local", _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
                    }));
            });
            var app = builder.Build();
            // Resolve eagerly so a Phantom Writer generation failure stops startup instead of failing the first request.
            app.Services.GetRequiredService<TrainingService>();
            var concurrentTests = new SemaphoreSlim(8);
            app.Use(async (context, next) =>
            {
                var host = context.Request.Host;
                bool validHost = (string.Equals(host.Host, "localhost", StringComparison.OrdinalIgnoreCase) || host.Host == "127.0.0.1") && host.Port == context.Connection.LocalPort;
                bool local = context.Connection.RemoteIpAddress != null && IPAddress.IsLoopback(context.Connection.RemoteIpAddress);
                if (!validHost || !local)
                {
                    context.Response.StatusCode = 403;
                    await context.Response.WriteAsJsonAsync(new { error = "Forbidden host" });
                    return;
                }
                if (context.Request.Headers.TryGetValue("Origin", out var origins))
                {
                    string expected = $"http://{host.Host}:{host.Port}";
                    if (origins.Count != 1 || !string.Equals(origins[0], expected, StringComparison.OrdinalIgnoreCase))
                    {
                        context.Response.StatusCode = 403;
                        await context.Response.WriteAsJsonAsync(new { error = "Cross-origin requests are not allowed" });
                        return;
                    }
                }
                context.Response.Headers.XContentTypeOptions = "nosniff";
                if (context.Request.Path.StartsWithSegments("/api")) context.Response.Headers.CacheControl = "no-store";
                await next();
            });
            app.UseRateLimiter();
            app.MapGet("/api/scenarios", (TrainingService service) => Results.Json(service.Scenarios()));
            // Shared by POST /api/test and POST /api/incident/answer: content type, 16 KiB body, JSON parsing and the 8-slot gate.
            async Task<IResult> ReadJsonBodyAsync(HttpContext context, Func<JsonElement, Task<IResult>> handle)
            {
                if (!System.Net.Http.Headers.MediaTypeHeaderValue.TryParse(context.Request.ContentType, out var type) || !string.Equals(type.MediaType, "application/json", StringComparison.OrdinalIgnoreCase))
                    return Results.Json(new { error = "Content-Type must be application/json" }, statusCode: 415);
                if (context.Request.ContentLength is 0 or > 16384) return Results.Json(new { error = "Invalid request size" }, statusCode: 400);
                if (!await concurrentTests.WaitAsync(0, context.RequestAborted)) return Results.Json(new { error = "Too many concurrent tests" }, statusCode: 429);
                try
                {
                    using var buffer = new MemoryStream();
                    var bytes = new byte[4096];
                    while (true)
                    {
                        int count = await context.Request.Body.ReadAsync(bytes, context.RequestAborted);
                        if (count == 0) break;
                        if (buffer.Length + count > 16384) return Results.Json(new { error = "Invalid request size" }, statusCode: 400);
                        buffer.Write(bytes, 0, count);
                    }
                    if (buffer.Length == 0) return Results.Json(new { error = "Invalid request size" }, statusCode: 400);
                    using var document = JsonDocument.Parse(buffer.ToArray());
                    return await handle(document.RootElement);
                }
                catch (JsonException) { return Results.Json(new { error = "Invalid JSON" }, statusCode: 400); }
                catch (ArgumentException ex) { return Results.Json(new { error = ex.Message }, statusCode: 400); }
                finally { concurrentTests.Release(); }
            }
            app.MapPost("/api/test", (HttpContext context, TrainingService service) => ReadJsonBodyAsync(context, async payload =>
            {
                var request = TrainingService.Validate(payload);
                return Results.Json(await service.TestAsync(request, context.RequestAborted));
            }));
            app.MapGet("/api/incident/{scenario_id}/log", (string scenario_id, TrainingService service) =>
                service.IncidentLog(scenario_id) is { } log ? Results.Json(log) : Results.Json(new { error = "Not found" }, statusCode: 404));
            app.MapPost("/api/incident/answer", (HttpContext context, TrainingService service) => ReadJsonBodyAsync(context, async payload =>
            {
                var answer = TrainingService.ValidateAnswer(payload);
                return Results.Json(await service.AnswerAsync(answer, context.RequestAborted));
            }));
            app.Map("/api/{**path}", () => Results.Json(new { error = "Not found" }, statusCode: 404));
            var frontend = Path.Combine(AppContext.BaseDirectory, "frontend");
            if (!Directory.Exists(frontend)) throw new DirectoryNotFoundException($"Frontend assets not found: {frontend}");
            var files = new PhysicalFileProvider(frontend);
            app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
            app.UseStaticFiles(new StaticFileOptions { FileProvider = files });
            return app;
        }

        private static int PortFromEnvironment()
        {
            var value = Environment.GetEnvironmentVariable("GHOSTWIRE_PORT");
            if (string.IsNullOrEmpty(value)) return 8000;
            if (!int.TryParse(value, out int port) || port is < 1 or > 65535) throw new ArgumentException("GHOSTWIRE_PORT must be between 1 and 65535.");
            return port;
        }
    }
}
