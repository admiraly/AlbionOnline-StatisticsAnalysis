using StatisticsAnalysisTool.Core.Events;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StatisticsAnalysisTool.Core.Features.Combat;

/// <summary>
/// Live damage meter. Consumes the combat-relevant Photon events and aggregates damage / healing
/// per causer, with per-entity DPS/HPS. Player names come from <c>NewCharacter</c>; other causers
/// are tracked by id and flagged as non-players. A fight resets automatically after an idle gap.
///
/// Event/parameter mapping (from the upstream WPF event models):
///   HealthUpdate  (code 6): 0=target, 2=healthChange (&lt;0 damage, &gt;0 heal), 6=causer
///   HealthUpdates (code 7): 0=target, 2=healthChange[], 6=causer[]   (parallel arrays)
///   NewCharacter  (code 29): 0=objectId, 1=name, 8=guildName
/// </summary>
public sealed class CombatTracker
{
    private readonly object _gate = new();
    private readonly Dictionary<long, PlayerIdentity> _players = new();
    private readonly Dictionary<long, Stats> _stats = new();
    private DateTime _sessionStartUtc = DateTime.UtcNow;
    private DateTime _lastActivityUtc = DateTime.MinValue;

    /// <summary>Auto-start a new fight after this many idle seconds. 0 disables auto-reset.</summary>
    public int IdleResetSeconds { get; set; } = 30;

    public void Handle(short code, Dictionary<byte, object> parameters)
    {
        switch ((EventCodes) code)
        {
            case EventCodes.NewCharacter:
                RegisterPlayer(parameters);
                break;
            case EventCodes.HealthUpdate:
                HandleHealthUpdate(parameters);
                break;
            case EventCodes.HealthUpdates:
                HandleHealthUpdates(parameters);
                break;
        }
    }

    private void RegisterPlayer(Dictionary<byte, object> parameters)
    {
        var id = ParamReader.ToLong(parameters.GetValueOrDefault((byte) 0));
        if (id is null)
        {
            return;
        }

        var name = ParamReader.ToStringValue(parameters.GetValueOrDefault((byte) 1));
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var guild = ParamReader.ToStringValue(parameters.GetValueOrDefault((byte) 8));

        lock (_gate)
        {
            _players[id.Value] = new PlayerIdentity(name, guild);
        }
    }

    private void HandleHealthUpdate(Dictionary<byte, object> parameters)
    {
        var target = ParamReader.ToLong(parameters.GetValueOrDefault((byte) 0));
        var causer = ParamReader.ToLong(parameters.GetValueOrDefault((byte) 6));
        var change = ParamReader.ToDouble(parameters.GetValueOrDefault((byte) 2));
        Apply(causer, target, change);
    }

    private void HandleHealthUpdates(Dictionary<byte, object> parameters)
    {
        var target = ParamReader.ToLong(parameters.GetValueOrDefault((byte) 0));
        var changes = ParamReader.ToDoubleList(parameters.GetValueOrDefault((byte) 2));
        var causers = ParamReader.ToLongList(parameters.GetValueOrDefault((byte) 6));

        var count = Math.Min(changes.Count, causers.Count);
        for (var i = 0; i < count; i++)
        {
            Apply(causers[i], target, changes[i]);
        }
    }

    private void Apply(long? causerId, long? targetId, double healthChange)
    {
        if (causerId is null || healthChange == 0)
        {
            return;
        }

        // Ignore self-inflicted health changes (fall damage, self-heal ticks) so the meter reflects
        // output against other entities.
        if (targetId is not null && targetId == causerId)
        {
            return;
        }

        var now = DateTime.UtcNow;

        lock (_gate)
        {
            MaybeAutoReset(now);

            if (!_stats.TryGetValue(causerId.Value, out var stats))
            {
                stats = new Stats { FirstUtc = now };
                _stats[causerId.Value] = stats;
            }

            if (healthChange < 0)
            {
                stats.Damage += -healthChange;
            }
            else
            {
                stats.Healing += healthChange;
            }

            stats.LastUtc = now;
            _lastActivityUtc = now;
        }
    }

    private void MaybeAutoReset(DateTime now)
    {
        if (IdleResetSeconds > 0
            && _lastActivityUtc != DateTime.MinValue
            && (now - _lastActivityUtc).TotalSeconds > IdleResetSeconds)
        {
            _stats.Clear();
            _sessionStartUtc = now;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _stats.Clear();
            _sessionStartUtc = DateTime.UtcNow;
            _lastActivityUtc = DateTime.MinValue;
        }
    }

    public CombatSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            var totalDamage = _stats.Values.Sum(s => s.Damage);
            var totalHealing = _stats.Values.Sum(s => s.Healing);
            var duration = Math.Max(1, (int) (DateTime.UtcNow - _sessionStartUtc).TotalSeconds);

            var entries = _stats
                .Select(kv =>
                {
                    var id = kv.Key;
                    var s = kv.Value;
                    var activeSeconds = Math.Max(1.0, (s.LastUtc - s.FirstUtc).TotalSeconds);
                    var known = _players.TryGetValue(id, out var identity);
                    return new CombatEntry(
                        Id: id,
                        Name: known ? identity!.Name : $"#{id}",
                        Guild: known ? identity!.Guild : null,
                        IsPlayer: known,
                        Damage: Math.Round(s.Damage),
                        Dps: Math.Round(s.Damage / activeSeconds, 1),
                        Healing: Math.Round(s.Healing),
                        Hps: Math.Round(s.Healing / activeSeconds, 1),
                        DamagePercent: totalDamage > 0 ? Math.Round(s.Damage / totalDamage * 100, 1) : 0);
                })
                .OrderByDescending(e => e.Damage)
                .ToList();

            return new CombatSnapshot(duration, Math.Round(totalDamage), Math.Round(totalHealing), entries);
        }
    }

    private sealed record PlayerIdentity(string Name, string? Guild);

    private sealed class Stats
    {
        public double Damage;
        public double Healing;
        public DateTime FirstUtc;
        public DateTime LastUtc;
    }
}

public sealed record CombatEntry(
    long Id,
    string Name,
    string? Guild,
    bool IsPlayer,
    double Damage,
    double Dps,
    double Healing,
    double Hps,
    double DamagePercent);

public sealed record CombatSnapshot(
    int DurationSeconds,
    double TotalDamage,
    double TotalHealing,
    IReadOnlyList<CombatEntry> Entries);
