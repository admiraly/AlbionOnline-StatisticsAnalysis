using StatisticsAnalysisTool.Core;
using StatisticsAnalysisTool.Core.Diagnostics;
using StatisticsAnalysisTool.Core.Features.Combat;

namespace StatisticsAnalysisTool.Cli;

/// <summary>
/// End-to-end proof of the damage-meter path: builds wire-valid NewCharacter + HealthUpdate
/// packets, runs them through the real <see cref="AlbionPhotonReceiver"/> into a
/// <see cref="CombatTracker"/>, and verifies the aggregated snapshot. No capture required.
/// </summary>
public static class CombatSelfTest
{
    public static bool Run(out string message)
    {
        var receiver = new AlbionPhotonReceiver();
        var combat = new CombatTracker { IdleResetSeconds = 0 };
        receiver.EventReceived += combat.Handle;

        const long alice = 1, bob = 2, target = 99;

        receiver.ReceivePacket(PhotonSampleData.NewCharacterPacket(alice, "Alice", "Guildy"));
        receiver.ReceivePacket(PhotonSampleData.NewCharacterPacket(bob, "Bob"));

        receiver.ReceivePacket(PhotonSampleData.HealthUpdatePacket(alice, target, -500));
        receiver.ReceivePacket(PhotonSampleData.HealthUpdatePacket(alice, target, -300));
        receiver.ReceivePacket(PhotonSampleData.HealthUpdatePacket(bob, target, -200));
        receiver.ReceivePacket(PhotonSampleData.HealthUpdatePacket(alice, target, 100)); // a heal

        var snapshot = combat.GetSnapshot();

        if (snapshot.TotalDamage != 1000)
        {
            message = $"expected total damage 1000, got {snapshot.TotalDamage}";
            return false;
        }

        var top = snapshot.Entries.FirstOrDefault();
        if (top is null || top.Name != "Alice" || top.Damage != 800 || top.Healing != 100)
        {
            message = $"expected Alice 800 dmg / 100 heal on top, got {Describe(top)}";
            return false;
        }

        if (top.DamagePercent != 80)
        {
            message = $"expected Alice 80% of damage, got {top.DamagePercent}";
            return false;
        }

        var bobEntry = snapshot.Entries.FirstOrDefault(e => e.Name == "Bob");
        if (bobEntry is null || bobEntry.Damage != 200 || !bobEntry.IsPlayer)
        {
            message = $"expected Bob 200 dmg (player), got {Describe(bobEntry)}";
            return false;
        }

        message = $"aggregated {snapshot.Entries.Count} combatants from crafted packets — Alice 800 (80%), Bob 200; total {snapshot.TotalDamage}";
        return true;
    }

    private static string Describe(CombatEntry? e) =>
        e is null ? "<none>" : $"{e.Name} dmg={e.Damage} heal={e.Healing} {e.DamagePercent}% player={e.IsPlayer}";
}
