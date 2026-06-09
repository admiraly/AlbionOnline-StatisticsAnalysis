using StatisticsAnalysisTool.Core;
using StatisticsAnalysisTool.Core.Capture;
using StatisticsAnalysisTool.Core.Diagnostics;
using System.Collections.Concurrent;

namespace StatisticsAnalysisTool.Web;

/// <summary>
/// Process-wide singleton that owns the <see cref="AlbionEngine"/> and aggregates its decoded
/// feed into a snapshot the dashboard can poll. All capture events arrive on the capture thread,
/// so aggregation is lock-free / concurrent and snapshots are built on demand.
/// </summary>
public sealed class EngineService : IDisposable
{
    private readonly object _gate = new();
    private readonly ILogger<EngineService> _logger;

    private AlbionEngine _engine;
    private CaptureOptions _options = new();
    private DateTime _startedUtc;
    private string? _lastError;

    private long _eventTotal;
    private long _requestTotal;
    private long _responseTotal;
    private readonly ConcurrentDictionary<short, long> _eventCounts = new();
    private readonly ConcurrentQueue<RecentPacket> _recent = new();
    private const int RecentCap = 100;

    public EngineService(ILogger<EngineService> logger)
    {
        _logger = logger;
        _engine = CreateEngine(_options);
    }

    public bool IsRunning
    {
        get { lock (_gate) { return _engine.IsRunning; } }
    }

    /// <summary>Starts (or restarts) live capture with the given options.</summary>
    public void Start(CaptureOptions options)
    {
        lock (_gate)
        {
            ResetAggregation();

            _engine.Dispose();
            _options = options;
            _engine = CreateEngine(options);
            _lastError = null;

            try
            {
                _engine.Start();
                _startedUtc = DateTime.UtcNow;

                if (!_engine.IsRunning)
                {
                    _lastError = "Capture did not start: no device was opened. Check that libpcap.so is present and that the process has CAP_NET_RAW (or run via sudo).";
                    _logger.LogWarning("{Error}", _lastError);
                }
            }
            catch (Exception ex)
            {
                _lastError = DescribeStartFailure(ex);
                _logger.LogError(ex, "Capture failed to start");
            }
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _engine.Stop();
        }
    }

    /// <summary>
    /// Feeds a hand-crafted, wire-valid Photon packet through the live engine receiver so the
    /// dashboard shows a decoded event without requiring live game traffic. Useful for a quick
    /// "is the dashboard wired up?" check.
    /// </summary>
    public void ReplaySample()
    {
        lock (_gate)
        {
            _engine.Receiver.ReceivePacket(PhotonSampleData.EventPacket());
        }
    }

    public IReadOnlyList<DeviceDto> GetDevices()
    {
        return AlbionEngine.GetAvailableNetworkDevices()
            .Select(d => new DeviceDto(d.Index, d.Name, d.Identifier))
            .ToList();
    }

    public StatusSnapshot GetStatus()
    {
        bool running;
        string server;
        string? error;
        DateTime started;
        string? filter;

        lock (_gate)
        {
            running = _engine.IsRunning;
            server = _engine.ServerDetection.CurrentServer.Name;
            error = _lastError;
            started = _startedUtc;
            filter = _options.PacketFilter;
        }

        var topEvents = _eventCounts
            .OrderByDescending(kv => kv.Value)
            .Take(15)
            .Select(kv => new EventCount(kv.Key, kv.Value))
            .ToList();

        var recent = _recent.Reverse().Take(50).ToList();

        var uptime = running && started != default
            ? (int) (DateTime.UtcNow - started).TotalSeconds
            : 0;

        return new StatusSnapshot(
            Running: running,
            Server: server,
            Filter: filter,
            UptimeSeconds: uptime,
            Totals: new Totals(
                Interlocked.Read(ref _eventTotal),
                Interlocked.Read(ref _requestTotal),
                Interlocked.Read(ref _responseTotal)),
            TopEvents: topEvents,
            Recent: recent,
            LastError: error);
    }

    private AlbionEngine CreateEngine(CaptureOptions options)
    {
        var engine = new AlbionEngine(options);
        engine.EventReceived += OnEvent;
        engine.RequestReceived += OnRequest;
        engine.ResponseReceived += OnResponse;
        return engine;
    }

    private void OnEvent(short code, Dictionary<byte, object> parameters)
    {
        Interlocked.Increment(ref _eventTotal);
        _eventCounts.AddOrUpdate(code, 1, (_, v) => v + 1);
        Record("event", code, parameters.Count);
    }

    private void OnRequest(short code, Dictionary<byte, object> parameters)
    {
        Interlocked.Increment(ref _requestTotal);
        Record("request", code, parameters.Count);
    }

    private void OnResponse(short code, Dictionary<byte, object> parameters)
    {
        Interlocked.Increment(ref _responseTotal);
        Record("response", code, parameters.Count);
    }

    private void Record(string kind, short code, int paramCount)
    {
        _recent.Enqueue(new RecentPacket(kind, code, paramCount, DateTime.UtcNow));
        while (_recent.Count > RecentCap && _recent.TryDequeue(out _))
        {
        }
    }

    private void ResetAggregation()
    {
        Interlocked.Exchange(ref _eventTotal, 0);
        Interlocked.Exchange(ref _requestTotal, 0);
        Interlocked.Exchange(ref _responseTotal, 0);
        _eventCounts.Clear();
        while (_recent.TryDequeue(out _))
        {
        }
    }

    private static string DescribeStartFailure(Exception ex)
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

    public void Dispose()
    {
        lock (_gate)
        {
            _engine.Dispose();
        }
    }
}

public record StatusSnapshot(
    bool Running,
    string Server,
    string? Filter,
    int UptimeSeconds,
    Totals Totals,
    IReadOnlyList<EventCount> TopEvents,
    IReadOnlyList<RecentPacket> Recent,
    string? LastError);

public record Totals(long Events, long Requests, long Responses);

public record EventCount(short Code, long Count);

public record RecentPacket(string Kind, short Code, int ParamCount, DateTime TimestampUtc);

public record DeviceDto(int Index, string Name, string Identifier);
