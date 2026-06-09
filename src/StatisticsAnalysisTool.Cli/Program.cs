using Serilog;
using Serilog.Events;
using StatisticsAnalysisTool.Cli;
using StatisticsAnalysisTool.Core;
using StatisticsAnalysisTool.Core.Capture;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

var command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";

try
{
    return command switch
    {
        "selftest" => RunSelfTest(),
        "combat-selftest" => RunCombatSelfTest(),
        "devices" => RunDevices(),
        "capture" => RunCapture(args),
        "help" or "-h" or "--help" => PrintUsage(),
        _ => UnknownCommand(command),
    };
}
catch (Exception ex) when (FindInner<DllNotFoundException>(ex) is not null)
{
    Log.Error("Native libpcap could not be loaded. The binding needs an unversioned 'libpcap.so'. On Linux:");
    Log.Error("  Debian/Ubuntu : sudo apt-get install libpcap0.8 && sudo ln -sf libpcap.so.0.8 /usr/lib/$(uname -m)-linux-gnu/libpcap.so");
    Log.Error("                  (or simply: sudo apt-get install libpcap-dev)");
    Log.Error("  Fedora        : sudo dnf install libpcap libpcap-devel");
    Log.Error("  Arch          : sudo pacman -S libpcap");
    Log.Error("On Windows, install Npcap (https://npcap.com).");
    return 3;
}
catch (Exception ex)
{
    Log.Error(ex, "Command '{Command}' failed", command);
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

static int RunSelfTest()
{
    Log.Information("Running Photon parse self-test (no capture required)...");

    var ok = PhotonSelfTest.Run(out var message);
    if (ok)
    {
        Log.Information("SELF-TEST PASSED: {Message}", message);
        return 0;
    }

    Log.Error("SELF-TEST FAILED: {Message}", message);
    return 1;
}

static int RunCombatSelfTest()
{
    Log.Information("Running damage-meter self-test (crafted combat packets, no capture)...");

    var ok = CombatSelfTest.Run(out var message);
    if (ok)
    {
        Log.Information("COMBAT SELF-TEST PASSED: {Message}", message);
        return 0;
    }

    Log.Error("COMBAT SELF-TEST FAILED: {Message}", message);
    return 1;
}

static int RunDevices()
{
    Log.Information("Enumerating capture devices via libpcap...");

    var devices = AlbionEngine.GetAvailableNetworkDevices();
    if (devices.Count == 0)
    {
        Log.Warning("No capture-capable devices found. On Linux this usually means missing CAP_NET_RAW; try running with sufficient privileges (e.g. via setcap or sudo).");
        return 0;
    }

    Log.Information("Found {Count} capture-capable device(s):", devices.Count);
    foreach (var device in devices)
    {
        Log.Information("  [{Index}] {Name}  (id: {Identifier})", device.Index, device.Name, device.Identifier);
    }

    return 0;
}

static int RunCapture(string[] args)
{
    var seconds = GetIntOption(args, "--seconds", 0);
    var noFilter = HasFlag(args, "--all");
    var customFilter = GetStringOption(args, "--filter");

    var options = new CaptureOptions();
    if (noFilter)
    {
        options.PacketFilter = null;
    }
    else if (!string.IsNullOrWhiteSpace(customFilter))
    {
        options.PacketFilter = customFilter;
    }

    using var engine = new AlbionEngine(options);

    long eventCount = 0;
    long requestCount = 0;
    long responseCount = 0;

    engine.EventReceived += (code, parameters) =>
    {
        Interlocked.Increment(ref eventCount);
        Log.Debug("EVENT code={Code} params={ParamCount}", code, parameters.Count);
    };
    engine.RequestReceived += (code, parameters) =>
    {
        Interlocked.Increment(ref requestCount);
        Log.Debug("REQUEST op={Code} params={ParamCount}", code, parameters.Count);
    };
    engine.ResponseReceived += (code, parameters) =>
    {
        Interlocked.Increment(ref responseCount);
        Log.Debug("RESPONSE op={Code} params={ParamCount}", code, parameters.Count);
    };
    engine.ServerDetection.ServerChanged += (_, e) =>
        Log.Information("Albion server changed: {Previous} -> {Current}", e.PreviousServer.Name, e.CurrentServer.Name);

    using var stop = new ManualResetEventSlim(false);
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        stop.Set();
    };

    Log.Information("Starting capture (filter: {Filter}). Press Ctrl+C to stop{Duration}.",
        options.PacketFilter is null ? "<none/all>" : "Photon UDP",
        seconds > 0 ? $" (auto-stop after {seconds}s)" : string.Empty);

    engine.Start();

    if (!engine.IsRunning)
    {
        Log.Error("Capture did not start. No device was opened — check capture privileges (CAP_NET_RAW) and that an interface is up.");
        return 2;
    }

    // Periodic heartbeat so the user can see traffic is flowing.
    using var heartbeat = new Timer(_ =>
    {
        Log.Information("captured: events={Events} requests={Requests} responses={Responses} | server={Server}",
            Interlocked.Read(ref eventCount),
            Interlocked.Read(ref requestCount),
            Interlocked.Read(ref responseCount),
            engine.ServerDetection.CurrentServer.Name);
    }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

    if (seconds > 0)
    {
        stop.Wait(TimeSpan.FromSeconds(seconds));
    }
    else
    {
        stop.Wait();
    }

    engine.Stop();
    Log.Information("Capture stopped. Totals: events={Events} requests={Requests} responses={Responses}",
        eventCount, requestCount, responseCount);
    return 0;
}

static int UnknownCommand(string command)
{
    Log.Error("Unknown command: {Command}", command);
    PrintUsage();
    return 64; // EX_USAGE
}

static int PrintUsage()
{
    Console.WriteLine(
        """
        sat-cli — cross-platform Albion Online capture/parse engine (headless)

        Usage:
          sat-cli selftest                 Verify the Photon parse pipeline (no capture/root needed)
          sat-cli combat-selftest          Verify the damage-meter aggregation (no capture/root needed)
          sat-cli devices                  List capture-capable network devices (libpcap)
          sat-cli capture [options]        Start live capture and decode Photon traffic
          sat-cli help                     Show this help

        capture options:
          --seconds N      Auto-stop after N seconds (default: run until Ctrl+C)
          --filter "BPF"   Use a custom libpcap/BPF filter
          --all            Capture without a filter (useful behind some VPNs)

        Notes:
          Live capture needs raw-capture privileges. On Linux grant them once with:
            sudo setcap cap_net_raw,cap_net_admin=eip "$(readlink -f $(which sat-cli))"
          or run via sudo. The game must run on (or route through) this host to be captured.
        """);
    return 0;
}

static TException? FindInner<TException>(Exception? ex) where TException : Exception
{
    for (var current = ex; current is not null; current = current.InnerException)
    {
        if (current is TException match)
        {
            return match;
        }
    }

    return null;
}

static bool HasFlag(string[] args, string name) =>
    Array.Exists(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

static string? GetStringOption(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
        {
            return args[i + 1];
        }
    }

    return null;
}

static int GetIntOption(string[] args, string name, int fallback)
{
    var raw = GetStringOption(args, name);
    return int.TryParse(raw, out var value) ? value : fallback;
}
