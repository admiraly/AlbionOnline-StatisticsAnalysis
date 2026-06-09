using StatisticsAnalysisTool.Core;

namespace StatisticsAnalysisTool.Cli;

/// <summary>
/// Capture-independent proof that the Photon framing + Protocol18 deserialization pipeline works
/// on the current platform. Builds a wire-valid Photon packet containing a single Event with two
/// parameters, feeds it through the real <see cref="AlbionPhotonReceiver"/>, and verifies the
/// decoded event code and parameters.
///
/// The byte layout is constructed from the actual reader logic in PhotonParser /
/// Protocol18Deserializer:
///   - Photon header numbers are big-endian (NumberDeserializer).
///   - Protocol18 'Short' values are little-endian (Protocol18Deserializer.DeserializeShort).
///   - CommandType.SendReliable = 6, MessageType.Event = 4.
///   - Protocol18Type.Short = 4, Protocol18Type.Byte = 3.
/// The Albion event code is carried in parameter 252.
/// </summary>
public static class PhotonSelfTest
{
    private const short ExpectedEventCode = 26;
    private const byte ExtraParamKey = 5;
    private const byte ExtraParamValue = 99;

    public static bool Run(out string message)
    {
        byte[] packet = BuildEventPacket();

        var receiver = new AlbionPhotonReceiver();
        short? decodedEventCode = null;
        Dictionary<byte, object>? decodedParameters = null;
        var eventCount = 0;

        receiver.EventReceived += (code, parameters) =>
        {
            eventCount++;
            decodedEventCode = code;
            decodedParameters = parameters;
        };

        receiver.ReceivePacket(packet);

        if (eventCount != 1)
        {
            message = $"expected exactly 1 decoded event, got {eventCount}";
            return false;
        }

        if (decodedEventCode != ExpectedEventCode)
        {
            message = $"event code mismatch: expected {ExpectedEventCode}, got {decodedEventCode}";
            return false;
        }

        if (decodedParameters is null
            || !decodedParameters.TryGetValue(ExtraParamKey, out var rawValue)
            || rawValue is not byte byteValue
            || byteValue != ExtraParamValue)
        {
            message = $"parameter[{ExtraParamKey}] mismatch: expected byte {ExtraParamValue}, got {Describe(decodedParameters, ExtraParamKey)}";
            return false;
        }

        message = $"decoded event code {decodedEventCode} with parameter[{ExtraParamKey}]={byteValue} from a {packet.Length}-byte Photon packet";
        return true;
    }

    private static string Describe(Dictionary<byte, object>? parameters, byte key)
    {
        if (parameters is null)
        {
            return "<no parameters>";
        }

        return parameters.TryGetValue(key, out var value)
            ? $"{value?.GetType().Name ?? "null"}={value}"
            : "<missing>";
    }

    /// <summary>
    /// Builds a 35-byte Photon packet: 12-byte header + one SendReliable command carrying an
    /// EventData with parameters { 252: (short)26, 5: (byte)99 }.
    /// </summary>
    private static byte[] BuildEventPacket()
    {
        // Protocol18 EventData payload (operation payload of the SendReliable command).
        byte[] eventData =
        [
            0x01,             // EventData.code (internal; distinct from the Albion event code)
            0x02,             // parameter count = 2
            0xFC,             // key 252 (Albion event code parameter)
            0x04,             // Protocol18Type.Short
            0x1A, 0x00,       // (short)26, little-endian
            ExtraParamKey,    // key 5
            0x03,             // Protocol18Type.Byte
            ExtraParamValue,  // (byte)99
        ];

        // SendReliable command: 12-byte command header + [skip][messageType][operation payload].
        // commandLength field counts the whole command (header + body) = 12 + 2 + eventData.Length.
        int commandLengthField = 12 + 2 + eventData.Length;

        var packet = new List<byte>();

        // Photon header (12 bytes). Numbers here are read big-endian; only flags + commandCount matter.
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
        packet.AddRange(BigEndian(commandLengthField)); // commandLength (big-endian)
        packet.AddRange([0x00, 0x00, 0x00, 0x01]); // reliable sequence number

        // SendReliable body.
        packet.Add(0x00);                          // skipped byte (HandleSendReliable)
        packet.Add(0x04);                          // MessageType.Event
        packet.AddRange(eventData);                // Protocol18 operation payload

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
