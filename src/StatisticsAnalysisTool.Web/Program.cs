using Serilog;
using StatisticsAnalysisTool.Core.Capture;
using StatisticsAnalysisTool.Core.Items;
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

// Bind to all interfaces on 8087 by default so the dashboard works in containers; override with
// ASPNETCORE_URLS (e.g. http://127.0.0.1:8087 to keep it local-only).
if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    builder.WebHost.UseUrls("http://0.0.0.0:8087");
}

builder.Services.AddSingleton<EngineService>();
builder.Services.AddSingleton<MarketService>();

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

app.MapGet("/api/loot", () => Results.Json(engine.GetLoot()));
app.MapPost("/api/loot/reset", () =>
{
    engine.ResetLoot();
    return Results.Json(engine.GetLoot());
});
app.MapPost("/api/replay-loot", () =>
{
    engine.ReplayLootSample();
    return Results.Json(engine.GetLoot());
});

app.MapGet("/api/map", () => Results.Json(engine.GetMap()));
app.MapPost("/api/map/reset", () =>
{
    engine.ResetMap();
    return Results.Json(engine.GetMap());
});
app.MapPost("/api/replay-map", () =>
{
    engine.ReplayMapSample();
    return Results.Json(engine.GetMap());
});

app.MapGet("/api/player", () => Results.Json(engine.GetPlayerStats()));
app.MapPost("/api/player/reset", () =>
{
    engine.ResetPlayer();
    return Results.Json(engine.GetPlayerStats());
});
app.MapPost("/api/replay-player", () =>
{
    engine.ReplayPlayerSample();
    return Results.Json(engine.GetPlayerStats());
});

app.MapGet("/api/gathering", () => Results.Json(engine.GetGathering()));
app.MapPost("/api/gathering/reset", () =>
{
    engine.ResetGathering();
    return Results.Json(engine.GetGathering());
});
app.MapPost("/api/replay-gathering", () =>
{
    engine.ReplayGatheringSample();
    return Results.Json(engine.GetGathering());
});

app.MapGet("/api/party", () => Results.Json(engine.GetParty()));
app.MapPost("/api/party/reset", () =>
{
    engine.ResetParty();
    return Results.Json(engine.GetParty());
});
app.MapPost("/api/replay-party", () =>
{
    engine.ReplayPartySample();
    return Results.Json(engine.GetParty());
});

// Item database: name search (bundled) + live market prices (AO Data Project).
app.MapGet("/api/items/search", (string? q) =>
{
    var results = ItemDatabase.Instance
        .Search(q ?? string.Empty, 50)
        .Select(i => new { i.Index, i.UniqueName, i.Name, i.Tier, i.Enchantment, i.IconUrl });
    return Results.Json(results);
});

app.MapGet("/api/items/db-info", () => Results.Json(new { count = ItemDatabase.Instance.Count }));

app.MapGet("/api/items/prices", async (string items, string? server, MarketService market) =>
{
    var names = (items ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    try
    {
        var prices = await market.GetPricesAsync(names, string.IsNullOrWhiteSpace(server) ? "west" : server);
        return Results.Json(prices);
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = $"Could not fetch prices: {ex.Message}" }, statusCode: StatusCodes.Status502BadGateway);
    }
});

// Auto-start capture on launch so the dashboard works out of the box (set SAT_AUTOSTART=false to
// require clicking "Start capture"). If capture can't start (no libpcap / no CAP_NET_RAW), the
// failure is surfaced in /api/status as lastError and shown in the dashboard banner.
var autoStart = Environment.GetEnvironmentVariable("SAT_AUTOSTART");
if (string.IsNullOrEmpty(autoStart) || autoStart is "true" or "1" || autoStart.Equals("true", StringComparison.OrdinalIgnoreCase))
{
    Log.Information("Auto-starting capture (set SAT_AUTOSTART=false to disable).");
    engine.Start(new CaptureOptions());
}

Log.Information("Albion Statistics dashboard starting. Open http://localhost:8087 in a browser.");
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
