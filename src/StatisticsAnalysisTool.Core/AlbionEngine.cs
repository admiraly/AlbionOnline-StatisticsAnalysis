using StatisticsAnalysisTool.Core.Capture;
using StatisticsAnalysisTool.Core.ServerDetection;
using System;
using System.Collections.Generic;

namespace StatisticsAnalysisTool.Core;

/// <summary>
/// Cross-platform facade over the capture + parse pipeline. Wires a libpcap capture provider to a
/// <see cref="AlbionPhotonReceiver"/> and an <see cref="AlbionServerDetectionService"/>, and exposes
/// decoded Photon events / requests / responses plus server-detection state. This is the single
/// entry point used by the console host and the web dashboard.
/// </summary>
public sealed class AlbionEngine : IDisposable
{
    private readonly AlbionPhotonReceiver _receiver = new();
    private readonly AlbionServerDetectionService _serverDetection = new();
    private readonly LibpcapPacketProvider _provider;

    public AlbionEngine(CaptureOptions? options = null)
    {
        var opts = options ?? new CaptureOptions();
        _provider = new LibpcapPacketProvider(_receiver, _serverDetection, opts);
    }

    /// <summary>Raw decoded packet feed. Subscribe to interpret events for a given feature.</summary>
    public AlbionPhotonReceiver Receiver => _receiver;

    /// <summary>Tracks which Albion server (Americas/Asia/Europe) the captured traffic belongs to.</summary>
    public AlbionServerDetectionService ServerDetection => _serverDetection;

    public bool IsRunning => _provider.IsRunning;

    /// <summary>Albion event code (parameter 252) + the raw parameter dictionary.</summary>
    public event Action<short, Dictionary<byte, object>>? EventReceived
    {
        add => _receiver.EventReceived += value;
        remove => _receiver.EventReceived -= value;
    }

    /// <summary>Albion operation code (parameter 253) of a client request + parameters.</summary>
    public event Action<short, Dictionary<byte, object>>? RequestReceived
    {
        add => _receiver.RequestReceived += value;
        remove => _receiver.RequestReceived -= value;
    }

    /// <summary>Albion operation code (parameter 253) of a server response + parameters.</summary>
    public event Action<short, Dictionary<byte, object>>? ResponseReceived
    {
        add => _receiver.ResponseReceived += value;
        remove => _receiver.ResponseReceived -= value;
    }

    /// <summary>Lists capture-capable network devices (excludes loopback / down interfaces).</summary>
    public static IReadOnlyList<NetworkDeviceInformation> GetAvailableNetworkDevices()
        => LibpcapPacketProvider.GetAvailableNetworkDevices();

    public void Start() => _provider.Start();

    public void Stop() => _provider.Stop();

    public void Dispose() => Stop();
}
