using StatisticsAnalysisTool.Core.Events;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StatisticsAnalysisTool.Core.Features.Map;

/// <summary>
/// Map / zone history. Each <c>ChangeCluster</c> operation response (sent when the player loads a
/// new zone) records the cluster the player entered and when.
///
/// Parameter mapping (from the upstream ChangeClusterResponse):
///   0 = cluster identifier, 2 = island name.
///
/// The cluster identifier is the raw zone id; mapping it to a friendly display name needs the
/// game world data (a follow-up).
/// </summary>
public sealed class MapTracker
{
    private readonly object _gate = new();
    private readonly List<Visit> _visits = new();
    private const int Cap = 200;

    public void HandleResponse(short code, Dictionary<byte, object> parameters)
    {
        if ((OperationCodes) code != OperationCodes.ChangeCluster)
        {
            return;
        }

        var clusterId = ParamReader.ToStringValue(parameters.GetValueOrDefault((byte) 0));
        if (string.IsNullOrWhiteSpace(clusterId))
        {
            return;
        }

        var island = ParamReader.ToStringValue(parameters.GetValueOrDefault((byte) 2));

        lock (_gate)
        {
            // Ignore a repeated response for the cluster we're already in.
            if (_visits.Count > 0 && _visits[^1].ClusterId == clusterId)
            {
                return;
            }

            _visits.Add(new Visit(clusterId, string.IsNullOrWhiteSpace(island) ? null : island, DateTime.UtcNow));
            if (_visits.Count > Cap)
            {
                _visits.RemoveAt(0);
            }
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _visits.Clear();
        }
    }

    public MapSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            var history = new List<MapVisit>(_visits.Count);

            for (var i = 0; i < _visits.Count; i++)
            {
                var v = _visits[i];
                var end = i + 1 < _visits.Count ? _visits[i + 1].EnteredUtc : now;
                history.Add(new MapVisit(v.ClusterId, v.Island, v.EnteredUtc, Math.Max(0, (int) (end - v.EnteredUtc).TotalSeconds)));
            }

            history.Reverse(); // newest first

            return new MapSnapshot(
                CurrentCluster: history.Count > 0 ? history[0].ClusterId : null,
                CurrentIsland: history.Count > 0 ? history[0].Island : null,
                VisitCount: _visits.Count,
                History: history);
        }
    }

    private sealed record Visit(string ClusterId, string? Island, DateTime EnteredUtc);
}

public sealed record MapVisit(string ClusterId, string? Island, DateTime EnteredUtc, int SecondsInZone);

public sealed record MapSnapshot(
    string? CurrentCluster,
    string? CurrentIsland,
    int VisitCount,
    IReadOnlyList<MapVisit> History);
