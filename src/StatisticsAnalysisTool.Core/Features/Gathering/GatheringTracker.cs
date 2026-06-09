using StatisticsAnalysisTool.Core.Events;
using StatisticsAnalysisTool.Core.Items;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StatisticsAnalysisTool.Core.Features.Gathering;

/// <summary>
/// Gathering log. Aggregates <c>HarvestFinished</c> events into per-resource totals (resolved to
/// item names/icons) plus a recent feed.
///
/// Parameter mapping (from the upstream HarvestFinishedEvent):
///   4 = itemId (resource), 5 = standard amount, 6 = collector bonus, 7 = premium bonus.
/// </summary>
public sealed class GatheringTracker
{
    private readonly object _gate = new();
    private readonly Dictionary<int, long> _totals = new();
    private readonly LinkedList<GatherEntry> _recent = new();
    private const int Cap = 200;

    public void Handle(short code, Dictionary<byte, object> parameters)
    {
        if ((EventCodes) code != EventCodes.HarvestFinished)
        {
            return;
        }

        var itemId = ParamReader.ToInt(parameters.GetValueOrDefault((byte) 4));
        var amount = ParamReader.ToInt(parameters.GetValueOrDefault((byte) 5))
                     + ParamReader.ToInt(parameters.GetValueOrDefault((byte) 6))
                     + ParamReader.ToInt(parameters.GetValueOrDefault((byte) 7));

        if (itemId <= 0 || amount <= 0)
        {
            return;
        }

        var item = ItemDatabase.Instance.ByIndex(itemId);

        lock (_gate)
        {
            _totals[itemId] = _totals.GetValueOrDefault(itemId) + amount;
            _recent.AddFirst(new GatherEntry(itemId, item?.UniqueName, item?.Name ?? $"#{itemId}", amount, DateTime.UtcNow));
            while (_recent.Count > Cap)
            {
                _recent.RemoveLast();
            }
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _totals.Clear();
            _recent.Clear();
        }
    }

    public GatheringSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            var totals = _totals
                .Select(kv =>
                {
                    var item = ItemDatabase.Instance.ByIndex(kv.Key);
                    return new GatherTotal(kv.Key, item?.UniqueName, item?.Name ?? $"#{kv.Key}", item?.Tier ?? 0, kv.Value);
                })
                .OrderByDescending(t => t.Amount)
                .ToList();

            return new GatheringSnapshot(totals, _recent.Take(50).ToList());
        }
    }
}

public sealed record GatherEntry(int ItemIndex, string? UniqueName, string Name, int Amount, DateTime TimestampUtc);

public sealed record GatherTotal(int ItemIndex, string? UniqueName, string Name, int Tier, long Amount);

public sealed record GatheringSnapshot(IReadOnlyList<GatherTotal> Totals, IReadOnlyList<GatherEntry> Recent);
