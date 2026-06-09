using System;
using System.Collections.Generic;
using System.Text;

namespace StatisticsAnalysisTool.Core.Diagnostics;

/// <summary>
/// Builds wire-valid Photon packets carrying a single Albion <c>Event</c> with arbitrary
/// parameters, encoded exactly as <c>Protocol18Deserializer</c> reads them. Used for diagnostics
/// and for the web dashboard's "replay" features so the trackers can be exercised without live
/// game traffic. Supported value types: byte, short, int/long (compressed), double, bool, string.
/// </summary>
public static class PhotonPacketBuilder
{
    private const byte EventCodeParameter = 252;
    private const byte OperationCodeParameter = 253;
    private const byte MessageTypeEvent = 0x04;
    private const byte MessageTypeOperationResponse = 0x03;

    /// <summary>
    /// Builds a Photon packet with one Event: the given parameters plus parameter 252 = eventCode.
    /// </summary>
    public static byte[] BuildEvent(short eventCode, IReadOnlyList<(byte Key, object Value)> parameters)
    {
        var body = new List<byte>();
        body.Add(0x01); // EventData inner code (not the Albion event code)
        body.Add((byte) (parameters.Count + 1)); // parameter count (+1 for the event-code parameter)

        foreach (var (key, value) in parameters)
        {
            body.Add(key);
            WriteValue(body, value);
        }

        // The Albion event code lives in parameter 252.
        body.Add(EventCodeParameter);
        WriteValue(body, eventCode);

        return WrapInSendReliable(body, MessageTypeEvent);
    }

    /// <summary>
    /// Builds a Photon packet with one OperationResponse: the given parameters plus parameter 253 =
    /// operationCode (returnCode 0, null debug message).
    /// </summary>
    public static byte[] BuildResponse(short operationCode, IReadOnlyList<(byte Key, object Value)> parameters)
    {
        var body = new List<byte>();
        body.Add((byte) (operationCode & 0xFF)); // operation code byte (real code is in parameter 253)
        body.Add(0x00);                          // returnCode (short, little-endian) = 0
        body.Add(0x00);
        body.Add(0x08);                          // debug message: Protocol18Type.Null

        body.Add((byte) (parameters.Count + 1)); // parameter count (+1 for the operation-code parameter)
        foreach (var (key, value) in parameters)
        {
            body.Add(key);
            WriteValue(body, value);
        }

        // The Albion operation code lives in parameter 253.
        body.Add(OperationCodeParameter);
        WriteValue(body, operationCode);

        return WrapInSendReliable(body, MessageTypeOperationResponse);
    }

    private static byte[] WrapInSendReliable(List<byte> eventData, byte messageType)
    {
        // commandLength counts the whole command: 12-byte header + [skip][messageType][payload].
        int commandLengthField = 12 + 2 + eventData.Count;

        var packet = new List<byte>(24 + eventData.Count);

        // Photon header (12 bytes).
        packet.AddRange([0x00, 0x00]);             // peerId
        packet.Add(0x00);                          // flags (not encrypted / not CRC)
        packet.Add(0x01);                          // commandCount = 1
        packet.AddRange([0x00, 0x00, 0x00, 0x00]); // timestamp
        packet.AddRange([0x00, 0x00, 0x00, 0x00]); // challenge

        // Command header (12 bytes).
        packet.Add(0x06);                          // CommandType.SendReliable
        packet.Add(0x00);                          // channelId
        packet.Add(0x00);                          // command flags
        packet.Add(0x00);                          // skipped byte
        packet.AddRange(BigEndian(commandLengthField));
        packet.AddRange([0x00, 0x00, 0x00, 0x01]); // reliable sequence number

        // SendReliable body.
        packet.Add(0x00);                          // skipped byte
        packet.Add(messageType);                   // MessageType.Event / OperationResponse
        packet.AddRange(eventData);

        return packet.ToArray();
    }

    private static void WriteValue(List<byte> output, object value)
    {
        switch (value)
        {
            case byte b:
                output.Add(0x03);                  // Protocol18Type.Byte
                output.Add(b);
                break;
            case bool flag:
                output.Add(flag ? (byte) 0x1C : (byte) 0x1B); // BooleanTrue / BooleanFalse
                break;
            case short s:
                output.Add(0x04);                  // Protocol18Type.Short
                output.Add((byte) (s & 0xFF));     // little-endian
                output.Add((byte) ((s >> 8) & 0xFF));
                break;
            case int i:
                WriteCompressedLong(output, i);
                break;
            case long l:
                WriteCompressedLong(output, l);
                break;
            case double d:
                output.Add(0x06);                  // Protocol18Type.Double
                WriteDoubleLittleEndian(output, d);
                break;
            case string str:
                output.Add(0x07);                  // Protocol18Type.String
                WriteString(output, str);
                break;
            default:
                throw new NotSupportedException($"PhotonPacketBuilder cannot encode {value.GetType().Name}.");
        }
    }

    private static void WriteCompressedLong(List<byte> output, long value)
    {
        output.Add(0x0A); // Protocol18Type.CompressedLong (zig-zag varint)
        var zigzag = (ulong) ((value << 1) ^ (value >> 63));
        WriteVarint(output, zigzag);
    }

    private static void WriteString(List<byte> output, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteVarint(output, (uint) bytes.Length); // length is a plain (non-zigzag) varint
        output.AddRange(bytes);
    }

    private static void WriteVarint(List<byte> output, ulong value)
    {
        while (value >= 0x80)
        {
            output.Add((byte) (value | 0x80));
            value >>= 7;
        }

        output.Add((byte) value);
    }

    private static void WriteDoubleLittleEndian(List<byte> output, double value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (!BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        output.AddRange(bytes);
    }

    private static byte[] BigEndian(int value) =>
    [
        (byte) ((value >> 24) & 0xFF),
        (byte) ((value >> 16) & 0xFF),
        (byte) ((value >> 8) & 0xFF),
        (byte) (value & 0xFF),
    ];
}
