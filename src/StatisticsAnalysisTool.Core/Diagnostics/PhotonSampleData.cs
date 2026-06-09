using StatisticsAnalysisTool.Core.Events;

namespace StatisticsAnalysisTool.Core.Diagnostics;

/// <summary>
/// Wire-valid Photon packets for diagnostics: proving the parse pipeline on a given platform
/// (CLI <c>selftest</c>) and exercising the dashboard trackers without live game traffic
/// (<c>replay</c> features). Packets are built with <see cref="PhotonPacketBuilder"/>.
/// </summary>
public static class PhotonSampleData
{
    /// <summary>The Albion event code encoded in <see cref="EventPacket"/> (parameter 252).</summary>
    public const short SampleEventCode = 26;

    /// <summary>An extra parameter key present in <see cref="EventPacket"/>.</summary>
    public const byte SampleExtraParamKey = 5;

    /// <summary>The value stored at <see cref="SampleExtraParamKey"/> in <see cref="EventPacket"/>.</summary>
    public const byte SampleExtraParamValue = 99;

    /// <summary>A Photon packet with one Event carrying { 252: (short)26, 5: (byte)99 }.</summary>
    public static byte[] EventPacket()
        => PhotonPacketBuilder.BuildEvent(SampleEventCode, [(SampleExtraParamKey, SampleExtraParamValue)]);

    /// <summary>A <c>NewCharacter</c> event registering a player id → name (+ optional guild).</summary>
    public static byte[] NewCharacterPacket(long objectId, string name, string? guild = null)
        => PhotonPacketBuilder.BuildEvent((short) EventCodes.NewCharacter,
        [
            ((byte) 0, objectId),
            ((byte) 1, name),
            ((byte) 8, guild ?? string.Empty),
        ]);

    /// <summary>A <c>HealthUpdate</c> event: causer affects target by healthChange (&lt;0 = damage).</summary>
    public static byte[] HealthUpdatePacket(long causerId, long targetId, double healthChange)
        => PhotonPacketBuilder.BuildEvent((short) EventCodes.HealthUpdate,
        [
            ((byte) 0, targetId),
            ((byte) 2, healthChange),
            ((byte) 6, causerId),
        ]);
}
