namespace StatisticsAnalysisTool.Core.Diagnostics;

/// <summary>
/// Builds a hand-crafted, wire-valid Photon packet for diagnostics: proving the parse pipeline
/// works on a given platform (CLI <c>selftest</c>) and letting the web dashboard show a decoded
/// event without live game traffic (<c>replay sample</c>).
///
/// The byte layout follows the actual reader logic in PhotonParser / Protocol18Deserializer:
/// Photon header numbers are big-endian; Protocol18 'Short' values are little-endian;
/// CommandType.SendReliable = 6, MessageType.Event = 4; Protocol18Type.Short = 4, Byte = 3.
/// The Albion event code is carried in parameter 252.
/// </summary>
public static class PhotonSampleData
{
    /// <summary>The Albion event code encoded in <see cref="EventPacket"/> (parameter 252).</summary>
    public const short SampleEventCode = 26;

    /// <summary>An extra parameter key present in <see cref="EventPacket"/>.</summary>
    public const byte SampleExtraParamKey = 5;

    /// <summary>The value stored at <see cref="SampleExtraParamKey"/> in <see cref="EventPacket"/>.</summary>
    public const byte SampleExtraParamValue = 99;

    /// <summary>
    /// A 35-byte Photon packet: 12-byte header + one SendReliable command carrying an EventData
    /// with parameters { 252: (short)26, 5: (byte)99 }.
    /// </summary>
    public static byte[] EventPacket()
    {
        // Protocol18 EventData payload (operation payload of the SendReliable command).
        byte[] eventData =
        [
            0x01,                  // EventData.code (internal; distinct from the Albion event code)
            0x02,                  // parameter count = 2
            0xFC,                  // key 252 (Albion event code parameter)
            0x04,                  // Protocol18Type.Short
            0x1A, 0x00,            // (short)26, little-endian
            SampleExtraParamKey,   // key 5
            0x03,                  // Protocol18Type.Byte
            SampleExtraParamValue, // (byte)99
        ];

        // commandLength counts the whole command (12-byte header + [skip][messageType][payload]).
        int commandLengthField = 12 + 2 + eventData.Length;

        var packet = new List<byte>(35);

        // Photon header (12 bytes). Only flags + commandCount are interpreted.
        packet.AddRange([0x00, 0x00]);             // peerId (short)
        packet.Add(0x00);                          // flags (0 = not encrypted, not CRC)
        packet.Add(0x01);                          // commandCount = 1
        packet.AddRange([0x00, 0x00, 0x00, 0x00]); // timestamp (ignored)
        packet.AddRange([0x00, 0x00, 0x00, 0x00]); // challenge (ignored)

        // Command header (12 bytes).
        packet.Add(0x06);                          // CommandType.SendReliable
        packet.Add(0x00);                          // channelId
        packet.Add(0x00);                          // command flags
        packet.Add(0x00);                          // skipped byte (HandleCommand)
        packet.AddRange(BigEndian(commandLengthField));
        packet.AddRange([0x00, 0x00, 0x00, 0x01]); // reliable sequence number

        // SendReliable body.
        packet.Add(0x00);                          // skipped byte (HandleSendReliable)
        packet.Add(0x04);                          // MessageType.Event
        packet.AddRange(eventData);

        return packet.ToArray();
    }

    private static byte[] BigEndian(int value) =>
    [
        (byte) ((value >> 24) & 0xFF),
        (byte) ((value >> 16) & 0xFF),
        (byte) ((value >> 8) & 0xFF),
        (byte) (value & 0xFF),
    ];
}
