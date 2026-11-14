// VERSION=0.1.0
using System.Collections.Immutable;
using AntMind.Contracts;
using static AntMind.Core.CombatMath;
namespace AntMind.Core;

// Per-tick institution projections. With no actor snapshot every rule keeps its pre-S1 behavior,
// so the existing 460 architectural checks remain byte-for-byte deterministic.
public static class InstitutionMechanics {
    public static int EffectiveReserve(WorldState world) =>
        world.ActiveParameters?.MinimumReserve ?? world.Config.MinimumReserve;

    // Tick-boundary projection. While the reserve tier is still the default, an explicitly
    // configured config baseline (SetMinimumReserve) stays authoritative; strict/loose override it.
    public static InstitutionParameters Resolve(WorldState world) {
        var p = InstitutionCatalog.Resolve(world.Institution, world.Config.InitialStock);
        return world.Institution.ReservePolicy == InstitutionCatalog.Default("reserve_policy") &&
               world.Config.MinimumReserve != p.MinimumReserve
            ? p with { MinimumReserve = world.Config.MinimumReserve } : p;
    }

    public static bool ThreatActive(WorldState world) {
        var p = world.ActiveParameters;
        if (p is null || p.ThreatSwitchPercent > 100) return false;
        var cap = Math.Max(1, world.Config.Combat.EnemyCap);
        var threat = (int)Math.Min(100, (long)world.Enemies.Length * 100 / cap);
        return threat >= p.ThreatSwitchPercent;
    }

    public static int DefenseCooldown(WorldState world) =>
        ThreatActive(world) ? Math.Max(1, world.Config.QueenCommunicationCooldownTicks / 2) :
        world.Config.QueenCommunicationCooldownTicks;

    public static int LaborWeight(WorldState world, string species) =>
        world.ActiveParameters?.Weight(species) ?? 100;

    public static IEnumerable<AntView> EligibleAnts(WorldState world) {
        var p = world.ActiveParameters;
        return p is null ? world.Ants :
            world.Ants.OrderBy(a => RationKey(world, a, p.RationRank)).ThenBy(a => a.Id);
    }

    public static IEnumerable<AntView> ClaimOrder(WorldState world) {
        var p = world.ActiveParameters;
        return p is null ? world.Ants :
            world.Ants.OrderBy(a => RationKey(world, a, p.RationRank))
                .ThenBy(a => p.AdmissionRank).ThenBy(a => a.Id);
    }

    // Chef weight decides which process intents are admitted first; every intent is still
    // processed once, so no action is silently dropped and conservation stays intact.
    public static IEnumerable<AntView> ProductionOrder(WorldState world, IEnumerable<AntView> ants) {
        var p = world.ActiveParameters;
        return p is null ? ants :
            ants.OrderBy(a => a.Intent == AntAction.Process && a.Species == AntSpeciesIds.Chef ? 0 : 1).ThenBy(a => a.Id);
    }

    public static IEnumerable<AntView> GuardFirst(WorldState world, IEnumerable<AntView> ants) =>
        !ThreatActive(world) ? ants :
            ants.OrderBy(a => IsGuard(world, a) ? 0 : 1).ThenBy(a => a.Id);

    // Returns a permutation of ant indices: storage order never changes, only the order in which
    // Deliver intents are admitted. Default / no-snapshot yields [0,1,2,...] exactly.
    public static int[] InteractionIndices(WorldState world, ImmutableArray<AntView> ants) {
        var all = Enumerable.Range(0, ants.Length).ToList();
        var p = world.ActiveParameters;
        if (p is null || p.AdmissionRank == 0 ||
            all.Count(idx => ants[idx].Intent == AntAction.Deliver) < 2) return all.ToArray();
        var nest = Point.FromCells(world.Config.Width / 2, world.Config.Height / 2);
        var ordered = all.Where(idx => ants[idx].Intent == AntAction.Deliver)
            .OrderBy(idx => AdmissionKey(world, ants[idx], nest, p.AdmissionRank))
            .ThenBy(idx => ants[idx].Id).ToArray();
        var cursor = 0;
        for (var pos = 0; pos < all.Count; pos++)
            if (ants[all[pos]].Intent == AntAction.Deliver) all[pos] = ordered[cursor++];
        return all.ToArray();
    }

    public static bool ClaimCrunch(WorldState world) =>
        world.ActiveParameters is { } && world.DevelopmentValue < EffectiveReserve(world) + 10;

    private static bool IsGuard(WorldState world, AntView ant) =>
        world.Groups.FirstOrDefault(g => g.Id == ant.GroupId)?.Activity == GroupActivities.Guard;

    private static long RationKey(WorldState world, AntView ant, int rank) => rank switch {
        0 => Distance(ant.Position, Point.FromCells(world.Config.Width / 2, world.Config.Height / 2)) <= Point.UnitsPerCell ? 0 : 1,
        1 => ant.Health,
        2 => -world.Config.AntType(ant.Species).WorkRate,
        _ => 0
    };

    private static long AdmissionKey(WorldState world, AntView ant, Point nest, int rank) => rank switch {
        1 => -ant.Carry,
        2 => Distance(ant.Position, nest),
        3 => -(world.Config.Foods.FirstOrDefault(x => x.Id == ant.CarryFood)?.Value ?? 0),
        _ => 0
    };
}
