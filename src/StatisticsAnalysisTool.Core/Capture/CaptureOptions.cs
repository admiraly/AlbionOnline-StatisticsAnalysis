using System.Collections.Generic;

namespace StatisticsAnalysisTool.Core.Capture;

/// <summary>
/// Cross-platform replacement for the WPF app's static <c>SettingsController.CurrentSettings</c>.
/// Holds only what the capture providers actually need, injected explicitly instead of read
/// from a global singleton.
/// </summary>
public sealed class CaptureOptions
{
    /// <summary>
    /// BPF capture filter. Defaults to the Photon UDP ports (+ IPv4 fragments) filter.
    /// Set to null/empty to capture everything (useful behind some VPNs).
    /// </summary>
    public string? PacketFilter { get; set; } = LibpcapPacketProvider.DefaultPacketFilter;

    /// <summary>
    /// Per-device capture selection. Devices not listed here are captured by default;
    /// list a device with <see cref="NetworkDeviceSelection.IsSelected"/> = false to skip it.
    /// </summary>
    public List<NetworkDeviceSelection> NetworkDevices { get; set; } = new();

    /// <summary>
    /// Which capture backend to use. On Linux only <see cref="PacketProviderKind.Npcap"/>
    /// (libpcap) is supported; the Windows raw-socket provider does not apply.
    /// </summary>
    public PacketProviderKind PacketProvider { get; set; } = PacketProviderKind.Npcap;
}

public sealed class NetworkDeviceSelection
{
    public string Identifier { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsSelected { get; set; } = true;
}
