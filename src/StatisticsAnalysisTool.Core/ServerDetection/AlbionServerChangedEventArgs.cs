using System;

namespace StatisticsAnalysisTool.Core.ServerDetection;

public class AlbionServerChangedEventArgs : EventArgs
{
    public AlbionServerChangedEventArgs(AlbionServerInfo previousServer, AlbionServerInfo currentServer)
    {
        PreviousServer = previousServer;
        CurrentServer = currentServer;
    }

    public AlbionServerInfo PreviousServer { get; }
    public AlbionServerInfo CurrentServer { get; }
}
