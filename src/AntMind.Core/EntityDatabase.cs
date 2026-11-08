using System.Collections.Immutable;
using AntMind.Contracts;

namespace AntMind.Core;

// One canonical, immutable record per (kind, id), owned exclusively by the Runtime actor.
// This is a per-world in-memory database. A future save adapter can persist its typed records.
public sealed record EntityDatabase {
    public ImmutableDictionary<string, IWorldObject> Records { get; init; } = ImmutableDictionary<string, IWorldObject>.Empty.WithComparers(StringComparer.Ordinal);
    public IWorldObject? Find(string kind, int id) => Records.GetValueOrDefault($"{kind}:{id}");
    public ImmutableArray<T> All<T>() where T : IWorldObject => Records.Values.OfType<T>().OrderBy(x => x.Id).ToImmutableArray();
    public EntityDatabase Put(IWorldObject item) {
        if (item.Id < 1 || string.IsNullOrWhiteSpace(item.Kind)) throw new ArgumentException("Invalid object identity.");
        if (item is ResourceView food && (food.Amount < 0 || food.MaxCapacity < food.Amount)) throw new ArgumentException("Invalid food capacity.");
        return this with { Records = Records.SetItem($"{item.Kind}:{item.Id}", item) };
    }
    public EntityDatabase ReplaceAnts(ImmutableArray<AntView> ants) {
        if (ants.Select(x => x.Id).Distinct().Count() != ants.Length) throw new ArgumentException("Duplicate ant IDs.");
        var ids = ants.Select(x=>x.Id).ToHashSet();
        var result = this;
        foreach (var prior in All<AntView>().Where(x=>x.Health>0 && !ids.Contains(x.Id)))
            result = result.Put(prior with { Health=0 });
        foreach (var ant in ants) result = result.Put(ant);
        return result;
    }
    public EntityDatabase ReplaceActiveFood(ImmutableArray<ResourceView> foods) {
        if (foods.Select(x => x.Id).Distinct().Count() != foods.Length) throw new ArgumentException("Duplicate food IDs.");
        var ids = foods.Select(x => x.Id).ToHashSet();
        var result = this;
        // Depleted records retain identity, original capacity, discovery and extension attributes.
        foreach (var prior in All<ResourceView>().Where(x => x.Amount > 0 && !ids.Contains(x.Id)))
            result = result.Put(prior with { Amount = 0,Credits=[],CreditTracked=true });
        foreach (var food in foods) result = result.Put(food);
        return result;
    }
}

public sealed class ResourceDiscoveryRule : IWorldRule {
    public string Id => "core.discovery";
    public int Order => 45;
    public WorldState Apply(WorldState world) {
        var database = world.Objects;
        foreach (var food in database.All<ResourceView>()) {
            if (!world.Ants.Any(ant => {
                var range = (long)world.Config.AntType(ant.Species).VisionCells * Point.UnitsPerCell;
                return Math.Abs((long)ant.Position.X - food.Position.X) <= range && Math.Abs((long)ant.Position.Y - food.Position.Y) <= range;
            })) continue;
            database = database.Put(food with { IsDiscovered = true,
                DiscoveredTick = food.IsDiscovered ? food.DiscoveredTick : world.Tick,
                LastSeenTick = world.Tick, LastObservedTick = world.Tick, LastObservedAmount = food.Amount });
        }
        foreach(var source in database.All<SourceView>())
            if(world.Ants.Any(a=>CombatMath.Distance(a.Position,source.Position)<=world.Config.AntType(a.Species).VisionCells*1000))
                database=database.Put(source with {IsDiscovered=true,LastObservedAmount=source.Amount,LastObservedTick=world.Tick});
        var observed=world.ObservedBuildings;
        foreach(var building in database.All<BuildingView>())
            if(world.Ants.Any(a=>CombatMath.Distance(a.Position,building.Position)<=world.Config.AntType(a.Species).VisionCells*1000)) {
                var seen=building with {IsDiscovered=true,LastObservedTick=world.Tick};database=database.Put(seen);observed=observed.SetItem(seen.Id,seen);
            }
        var queen = (QueenObject)database.Find("queen",1)!;
        foreach (var enemy in database.All<EnemyView>()) {
            if (!world.Ants.Any(ant => CombatMath.Distance(ant.Position,enemy.Position) <= world.Config.AntType(ant.Species).VisionCells * Point.UnitsPerCell) &&
                CombatMath.Distance(queen.Position,enemy.Position) > 8000) continue;
            database = database.Put(enemy with { IsDiscovered=true, LastKnownPosition=enemy.Position,
                LastKnownHealth=enemy.Health, LastSeenTick=world.Tick });
        }
        return world with { Objects = database,ObservedBuildings=observed };
    }
}
