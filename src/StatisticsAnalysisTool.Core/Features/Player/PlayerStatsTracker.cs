using StatisticsAnalysisTool.Core.Events;
using System;
using System.Collections.Generic;

namespace StatisticsAnalysisTool.Core.Features.Player;

/// <summary>
/// Session counters for the local player: fame, silver, might, favor and re-spec points gained
/// since the session (or last reset). Albion fame/silver/re-spec are fixed-point (internal value
/// = display × 10000).
///
/// Parameter mapping (from the upstream event models):
///   UpdateFame (72):            2 = gained fame (×10000)
///   TakeSilver (55):            3 = silver yield (×10000)
///   MightAndFavorReceived (470):1 = might, 4 = favor
///   UpdateReSpecPoints (78):    2 = gained re-spec (×10000)
/// </summary>
public sealed class PlayerStatsTracker
{
    private const double FixPoint = 10000.0;
    private readonly object _gate = new();
    private double _fame;
    private double _silver;
    private long _might;
    private long _favor;
    private double _respec;
    private DateTime _startUtc = DateTime.UtcNow;

    public void Handle(short code, Dictionary<byte, object> parameters)
    {
        switch ((EventCodes) code)
        {
            case EventCodes.UpdateFame:
                Add(ref _fame, ParamReader.ToLong(parameters.GetValueOrDefault((byte) 2)) / FixPoint);
                break;
            case EventCodes.TakeSilver:
                Add(ref _silver, ParamReader.ToLong(parameters.GetValueOrDefault((byte) 3)) / FixPoint);
                break;
            case EventCodes.MightAndFavorReceivedEvent:
                lock (_gate)
                {
                    _might += ParamReader.ToLong(parameters.GetValueOrDefault((byte) 1)) ?? 0;
                    _favor += ParamReader.ToLong(parameters.GetValueOrDefault((byte) 4)) ?? 0;
                }
                break;
            case EventCodes.UpdateReSpecPoints:
                Add(ref _respec, ParamReader.ToLong(parameters.GetValueOrDefault((byte) 2)) / FixPoint);
                break;
        }
    }

    private void Add(ref double field, double? value)
    {
        if (value is null or 0)
        {
            return;
        }

        lock (_gate)
        {
            field += value.Value;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _fame = _silver = _respec = 0;
            _might = _favor = 0;
            _startUtc = DateTime.UtcNow;
        }
    }

    public PlayerStatsSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            var seconds = Math.Max(1, (int) (DateTime.UtcNow - _startUtc).TotalSeconds);
            var hours = seconds / 3600.0;
            return new PlayerStatsSnapshot(
                SessionSeconds: seconds,
                Fame: Math.Round(_fame),
                Silver: Math.Round(_silver),
                Might: _might,
                Favor: _favor,
                ReSpec: Math.Round(_respec),
                FamePerHour: Math.Round(_fame / hours),
                SilverPerHour: Math.Round(_silver / hours));
        }
    }
}

public sealed record PlayerStatsSnapshot(
    int SessionSeconds,
    double Fame,
    double Silver,
    long Might,
    long Favor,
    double ReSpec,
    double FamePerHour,
    double SilverPerHour);
