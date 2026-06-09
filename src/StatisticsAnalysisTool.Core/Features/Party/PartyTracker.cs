using StatisticsAnalysisTool.Core.Events;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace StatisticsAnalysisTool.Core.Features.Party;

/// <summary>
/// Tracks the current party membership (guid → name).
///
/// Parameter mapping (from the upstream party event models):
///   PartyJoined (212):       5 = member guids, 6 = member names (parallel)
///   PartyPlayerJoined (214): 1 = guid, 2 = name
///   PartyPlayerLeft (216):   1 = guid
///   PartyDisbanded (213):    clears the party
/// </summary>
public sealed class PartyTracker
{
    private const int GuidByteLength = 16;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, string> _members = new();

    public void Handle(short code, Dictionary<byte, object> parameters)
    {
        switch ((EventCodes) code)
        {
            case EventCodes.PartyJoined:
                HandlePartyJoined(parameters);
                break;
            case EventCodes.PartyPlayerJoined:
                AddMember(ParamReader.ToGuid(parameters.GetValueOrDefault((byte) 1)),
                          ParamReader.ToStringValue(parameters.GetValueOrDefault((byte) 2)));
                break;
            case EventCodes.PartyPlayerLeft:
                RemoveMember(ParamReader.ToGuid(parameters.GetValueOrDefault((byte) 1)));
                break;
            case EventCodes.PartyDisbanded:
                Reset();
                break;
        }
    }

    private void HandlePartyJoined(Dictionary<byte, object> parameters)
    {
        var guids = ParseGuids(parameters.GetValueOrDefault((byte) 5));
        var names = ParseStrings(parameters.GetValueOrDefault((byte) 6));
        var count = Math.Min(guids.Count, names.Count);

        lock (_gate)
        {
            _members.Clear();
            for (var i = 0; i < count; i++)
            {
                if (guids[i] != Guid.Empty && !string.IsNullOrEmpty(names[i]))
                {
                    _members[guids[i]] = names[i];
                }
            }
        }
    }

    private void AddMember(Guid? guid, string? name)
    {
        if (guid is null || guid == Guid.Empty || string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        lock (_gate)
        {
            _members[guid.Value] = name;
        }
    }

    private void RemoveMember(Guid? guid)
    {
        if (guid is null)
        {
            return;
        }

        lock (_gate)
        {
            _members.Remove(guid.Value);
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _members.Clear();
        }
    }

    public PartySnapshot GetSnapshot()
    {
        lock (_gate)
        {
            var members = _members.Values.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            return new PartySnapshot(members.Count, members);
        }
    }

    private static List<Guid> ParseGuids(object? value)
    {
        var result = new List<Guid>();

        switch (value)
        {
            case byte[] flat:
                for (var i = 0; i + GuidByteLength <= flat.Length; i += GuidByteLength)
                {
                    result.Add(new Guid(flat[i..(i + GuidByteLength)]));
                }
                break;
            case IEnumerable enumerable and not string:
                foreach (var element in enumerable)
                {
                    var guid = ParamReader.ToGuid(element);
                    if (guid is not null)
                    {
                        result.Add(guid.Value);
                    }
                }
                break;
        }

        return result;
    }

    private static List<string> ParseStrings(object? value)
    {
        switch (value)
        {
            case string[] strings:
                return strings.ToList();
            case IEnumerable enumerable and not string:
                return enumerable.Cast<object?>().Select(e => e?.ToString() ?? string.Empty).ToList();
            default:
                return new List<string>();
        }
    }
}

public sealed record PartySnapshot(int Count, IReadOnlyList<string> Members);
