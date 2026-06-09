using StatisticsAnalysisTool.Core.Events;
using StatisticsAnalysisTool.Core.Items;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StatisticsAnalysisTool.Core.Features.Loot;

/// <summary>
/// Loot logger. Records <c>OtherGrabbedLoot</c> events — who looted what (item id + quantity) from
/// whom, including silver pickups.
///
/// Parameter mapping (from the upstream GrabbedLootEvent):
///   1 = looted-from (body/mob), 2 = looter (player), 3 = isSilver, 4 = itemIndex, 5 = quantity.
///
/// Item ids are not resolved to names here — that needs the game item database (a follow-up).
/// </summary>
public sealed class LootTracker
{
    private readonly object _gate = new();
    private readonly LinkedList<LootEntry> _recent = new();
    private const int Cap = 500;
    private long _itemEvents;
    private long _totalSilver;

    public void Handle(short code, Dictionary<byte, object> parameters)
    {
        if ((EventCodes) code != EventCodes.OtherGrabbedLoot)
        {
            return;
        }

        var lootedFrom = ParamReader.ToStringValue(parameters.GetValueOrDefault((byte) 1));
        var looter = ParamReader.ToStringValue(parameters.GetValueOrDefault((byte) 2));
        var isSilver = parameters.GetValueOrDefault((byte) 3) is bool b && b;
        var itemIndex = ParamReader.ToInt(parameters.GetValueOrDefault((byte) 4));
        var quantity = ParamReader.ToInt(parameters.GetValueOrDefault((byte) 5));

        if (string.IsNullOrWhiteSpace(looter) && quantity == 0)
        {
            return;
        }

        var item = isSilver ? null : ItemDatabase.Instance.ByIndex(itemIndex);

        var entry = new LootEntry(
            Looter: string.IsNullOrWhiteSpace(looter) ? "?" : looter,
            LootedFrom: string.IsNullOrWhiteSpace(lootedFrom) ? null : lootedFrom,
            ItemIndex: itemIndex,
            UniqueName: item?.UniqueName,
            ItemName: item?.Name,
            Quantity: quantity,
            IsSilver: isSilver,
            TimestampUtc: DateTime.UtcNow);

        lock (_gate)
        {
            _recent.AddFirst(entry);
            while (_recent.Count > Cap)
            {
                _recent.RemoveLast();
            }

            if (isSilver)
            {
                _totalSilver += quantity;
            }
            else
            {
                _itemEvents++;
            }
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _recent.Clear();
            _itemEvents = 0;
            _totalSilver = 0;
        }
    }

    public LootSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return new LootSnapshot(
                ItemEvents: _itemEvents,
                TotalSilver: _totalSilver,
                Recent: _recent.Take(100).ToList());
        }
    }
}

public sealed record LootEntry(
    string Looter,
    string? LootedFrom,
    int ItemIndex,
    string? UniqueName,
    string? ItemName,
    int Quantity,
    bool IsSilver,
    DateTime TimestampUtc);

public sealed record LootSnapshot(
    long ItemEvents,
    long TotalSilver,
    IReadOnlyList<LootEntry> Recent);
