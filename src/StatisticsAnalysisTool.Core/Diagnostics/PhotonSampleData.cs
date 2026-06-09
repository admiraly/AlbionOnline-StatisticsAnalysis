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

    /// <summary>An <c>OtherGrabbedLoot</c> event: a player looted an item from a body/mob.</summary>
    public static byte[] GrabbedLootPacket(string looter, string lootedFrom, int itemIndex, int quantity)
        => PhotonPacketBuilder.BuildEvent((short) EventCodes.OtherGrabbedLoot,
        [
            ((byte) 1, lootedFrom),
            ((byte) 2, looter),
            ((byte) 3, false),
            ((byte) 4, itemIndex),
            ((byte) 5, quantity),
        ]);

    /// <summary>An <c>OtherGrabbedLoot</c> event carrying silver instead of an item.</summary>
    public static byte[] GrabbedSilverPacket(string looter, int amount)
        => PhotonPacketBuilder.BuildEvent((short) EventCodes.OtherGrabbedLoot,
        [
            ((byte) 2, looter),
            ((byte) 3, true),
            ((byte) 5, amount),
        ]);

    /// <summary>A <c>ChangeCluster</c> operation response: the player entered a new zone.</summary>
    public static byte[] ChangeClusterPacket(string clusterId, string? island = null)
        => PhotonPacketBuilder.BuildResponse((short) OperationCodes.ChangeCluster,
            island is null
                ? [((byte) 0, clusterId)]
                : [((byte) 0, clusterId), ((byte) 2, island)]);
}
