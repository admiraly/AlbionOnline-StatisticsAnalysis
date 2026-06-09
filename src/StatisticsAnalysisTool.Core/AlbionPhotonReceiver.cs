using StatisticsAnalysisTool.PhotonPackageParser;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace StatisticsAnalysisTool.Core;

/// <summary>
/// Cross-platform Photon receiver. Mirrors the WPF app's internal <c>AlbionParser</c>: it parses
/// the Photon protocol and surfaces decoded events / requests / responses as plain .NET events,
/// so any consumer (console logger, web hub, feature trackers) can subscribe without referencing
/// the parser internals.
/// </summary>
public sealed class AlbionPhotonReceiver : PhotonParser
{
    /// <summary>Albion event code (parameter 252) + the raw parameter dictionary.</summary>
    public event Action<short, Dictionary<byte, object>>? EventReceived;

    /// <summary>Albion operation code (parameter 253) of a client request + parameters.</summary>
    public event Action<short, Dictionary<byte, object>>? RequestReceived;

    /// <summary>Albion operation code (parameter 253) of a server response + parameters.</summary>
    public event Action<short, Dictionary<byte, object>>? ResponseReceived;

    private const byte EventCodeParameter = 252;
    private const byte OperationCodeParameter = 253;

    protected override void OnEvent(byte code, Dictionary<byte, object> parameters)
    {
        short eventCode = ParsePhotonCode(parameters, EventCodeParameter);
        if (eventCode <= -1)
        {
            return;
        }

        EventReceived?.Invoke(eventCode, parameters);
    }

    protected override void OnRequest(byte operationCode, Dictionary<byte, object> parameters)
    {
        short code = ParsePhotonCode(parameters, OperationCodeParameter);
        if (code <= -1)
        {
            return;
        }

        RequestReceived?.Invoke(code, parameters);
    }

    protected override void OnResponse(byte operationCode, short returnCode, string debugMessage, Dictionary<byte, object> parameters)
    {
        short code = ParsePhotonCode(parameters, OperationCodeParameter);
        if (code <= -1)
        {
            return;
        }

        ResponseReceived?.Invoke(code, parameters);
    }

    private static short ParsePhotonCode(Dictionary<byte, object> parameters, byte parameterKey)
    {
        if (!parameters.TryGetValue(parameterKey, out object? value))
        {
            return -1;
        }

        try
        {
            return checked((short) Convert.ToInt32(value, CultureInfo.InvariantCulture));
        }
        catch
        {
            return -1;
        }
    }
}
