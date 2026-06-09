using Serilog;
using StatisticsAnalysisTool.Core.Capture;
using StatisticsAnalysisTool.Web;

// Configure Serilog so the engine's static Serilog.Log calls surface in the console.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

// Pin the content root to the binary's directory so wwwroot (copied next to the binary on
// publish) is found regardless of the working directory the app is launched from (containers,
// systemd, sudo, etc.).
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// Bind to all interfaces on 8080 by default so the dashboard works in containers; override with
// ASPNETCORE_URLS (e.g. http://127.0.0.1:8080 to keep it local-only).
if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    builder.WebHost.UseUrls("http://0.0.0.0:8080");
}

builder.Services.AddSingleton<EngineService>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

var engine = app.Services.GetRequiredService<EngineService>();

app.MapGet("/api/status", () => Results.Json(engine.GetStatus()));

app.MapGet("/api/devices", () =>
{
    try
    {
        return Results.Json(engine.GetDevices());
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = DescribeLibpcap(ex) }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapPost("/api/capture/start", (StartRequest? request) =>
{
    var options = new CaptureOptions();

    if (request?.All == true)
    {
        options.PacketFilter = null;
    }
    else if (!string.IsNullOrWhiteSpace(request?.Filter))
    {
        options.PacketFilter = request!.Filter;
    }

    if (request?.DisabledDevices is { Count: > 0 } disabled)
    {
        options.NetworkDevices = disabled
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => new NetworkDeviceSelection { Identifier = id, IsSelected = false })
            .ToList();
    }

    engine.Start(options);
    return Results.Json(engine.GetStatus());
});

app.MapPost("/api/capture/stop", () =>
{
    engine.Stop();
    return Results.Json(engine.GetStatus());
});

app.MapPost("/api/replay-sample", () =>
{
    engine.ReplaySample();
    return Results.Json(new { ok = true });
});

app.MapGet("/api/combat", () => Results.Json(engine.GetCombat()));

app.MapPost("/api/combat/reset", () =>
{
    engine.ResetCombat();
    return Results.Json(engine.GetCombat());
});

app.MapPost("/api/replay-combat", () =>
{
    engine.ReplayCombatSample();
    return Results.Json(engine.GetCombat());
});

Log.Information("Albion Statistics dashboard starting. Open http://localhost:8080 in a browser.");
app.Run();

static string DescribeLibpcap(Exception ex)
{
    for (var current = ex; current is not null; current = current.InnerException)
    {
        if (current is DllNotFoundException)
        {
            return "Native libpcap could not be loaded. Install libpcap and ensure an unversioned 'libpcap.so' exists (libpcap-dev, or a symlink to libpcap.so.0.8).";
        }
    }

    return ex.Message;
}

public record StartRequest(string? Filter, bool All, List<string>? DisabledDevices);
