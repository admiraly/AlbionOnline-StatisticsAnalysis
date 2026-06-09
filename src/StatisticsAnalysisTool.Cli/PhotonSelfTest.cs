using StatisticsAnalysisTool.Core;
using StatisticsAnalysisTool.Core.Diagnostics;

namespace StatisticsAnalysisTool.Cli;

/// <summary>
/// Capture-independent proof that the Photon framing + Protocol18 deserialization pipeline works
/// on the current platform. Feeds a wire-valid Photon packet (see <see cref="PhotonSampleData"/>)
/// through the real <see cref="AlbionPhotonReceiver"/> and verifies the decoded event code and
/// parameters.
/// </summary>
public static class PhotonSelfTest
{
    public static bool Run(out string message)
    {
        byte[] packet = PhotonSampleData.EventPacket();

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

        if (decodedEventCode != PhotonSampleData.SampleEventCode)
        {
            message = $"event code mismatch: expected {PhotonSampleData.SampleEventCode}, got {decodedEventCode}";
            return false;
        }

        if (decodedParameters is null
            || !decodedParameters.TryGetValue(PhotonSampleData.SampleExtraParamKey, out var rawValue)
            || rawValue is not byte byteValue
            || byteValue != PhotonSampleData.SampleExtraParamValue)
        {
            message = $"parameter[{PhotonSampleData.SampleExtraParamKey}] mismatch: expected byte {PhotonSampleData.SampleExtraParamValue}, got {Describe(decodedParameters, PhotonSampleData.SampleExtraParamKey)}";
            return false;
        }

        message = $"decoded event code {decodedEventCode} with parameter[{PhotonSampleData.SampleExtraParamKey}]={byteValue} from a {packet.Length}-byte Photon packet";
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
}
