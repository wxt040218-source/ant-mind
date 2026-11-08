using System.Collections.Immutable;
using AntMind.Contracts;
namespace AntMind.Core;
public sealed record TechnologyConfig(string Id, string Name, int RequiredNetworkLevel);
public sealed record AntTypeConfig(string Id, string Name, int InitialCount, int EmptyMoveUnitsPerTick,
    int LoadedMoveUnitsPerTick, int CarryCapacity, int VisionCells) {
    public int WorkRate {get;init;}=1;
    public int MaxHealth { get; init; } = 12;
    public int AttackDamage { get; init; }
    public int AttackCooldownTicks { get; init; } = 10;
}

public sealed record WorldConfig(int Width = 64, int Height = 64, int InitialAnts = 10, int TicksPerSecond = 20,
    int InitialStock = 100, int PopulationCap = 100, int RecruitmentCost = 20, int MinimumReserve = 40,
    int InitialResourceNodes = 8, int ResourceMin = 1, int ResourceMax = 5,
    int ResourceNodeCap = 20, int GroundResourceCap = 1200, int ResourceRefreshTicks = 100,
    int NestExclusionCells = 4, int InteractionCooldownTicks = 10)
{
    public ProductionConfig Production {get;init;}=new();
    public CombatConfig Combat { get; init; } = new();
    public int EatingTicks { get; init; } = 40;
    public ImmutableArray<FoodDefinition> Foods { get; init; } = [new(FoodTypes.Fruit,"水果",3), new(FoodTypes.Meat,"肉",6)];
    public ImmutableArray<MilestoneDefinition> Milestones { get; init; } = MilestoneRule.Defaults;
    public ImmutableArray<int> CarryMultipliers { get; init; } = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
    public ImmutableArray<int> CarryUpgradeCosts { get; init; } = [0, 80, 120, 180, 260, 360, 480, 620, 800, 1000];
    public int QueenCommunicationCooldownTicks { get; init; } = 10;
    public ImmutableArray<int> NetworkRanges { get; init; } = [16, 24, 32, 48, 64];
    public ImmutableArray<int> NetworkUpgradeCosts { get; init; } = [0, 60, 160, 400, 1000];
    public ImmutableArray<TechnologyConfig> Technologies { get; init; } = [new("queen.communication", "蚁后信息收发", 1)];
    public ImmutableArray<AntTypeConfig> AntTypes { get; init; } = [
        new(AntMind.Contracts.AntSpeciesIds.Scout, "侦察蚁", 5, 200, 20, 1, 6),
        new(AntMind.Contracts.AntSpeciesIds.Hauler, "搬运蚁", 5, 120, 60, 1, 3),
        new(AntSpeciesIds.Soldier, "兵蚁", 0, 180, 50, 1, 8) { MaxHealth = 40, AttackDamage = 6 }
    ];
    public ImmutableArray<int> BrainBudgets { get; init; } = [64, 128, 256, 512, 1024];
    public ImmutableArray<int> BrainIntervals { get; init; } = [10, 8, 6, 4, 2];
    public ImmutableArray<int> BrainCosts { get; init; } = [0, 100, 250, 600, 1500];
    public void Validate() {
        MilestoneRule.Validate(Milestones);
        Combat.Validate();
        Production.Validate(Foods);
        if (EatingTicks < 1 || EatingTicks > 1200 || Foods.IsDefaultOrEmpty || Foods.Length > 32 ||
            Foods.Any(x=>x is null || string.IsNullOrWhiteSpace(x.Id) || x.Id.Length>40 || string.IsNullOrWhiteSpace(x.Name) || x.HealAmount is <0 or >1000 || x.Edible && x.HealAmount<1 || x.UnitMass!=1) ||
            Foods.Select(x=>x.Id).Distinct(StringComparer.Ordinal).Count()!=Foods.Length ||
            !Foods.Any(x=>x.Id==FoodTypes.Fruit) || !Foods.Any(x=>x.Id==FoodTypes.Meat)) throw new ArgumentException("Invalid food configuration.");
        if (NetworkRanges.IsDefaultOrEmpty || NetworkUpgradeCosts.IsDefaultOrEmpty ||
            NetworkRanges.Length > 16 || NetworkRanges.Length != NetworkUpgradeCosts.Length ||
            NetworkRanges.Any(x => x is < 1 or > 1024) || NetworkRanges.Zip(NetworkRanges.Skip(1)).Any(x => x.Second <= x.First) ||
            NetworkUpgradeCosts[0] != 0 || NetworkUpgradeCosts.Skip(1).Any(x => x < 1) ||
            Technologies.IsDefaultOrEmpty || Technologies.Length > 128 ||
            Technologies.Any(x => x is null || string.IsNullOrWhiteSpace(x.Id) || string.IsNullOrWhiteSpace(x.Name) || x.Id.Length > 80 || x.Name.Length > 80 || x.RequiredNetworkLevel < 1 || x.RequiredNetworkLevel > NetworkRanges.Length) ||
            Technologies.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != Technologies.Length ||
            !Technologies.Any(x => x.Id == "queen.communication") ||
            CarryMultipliers.IsDefaultOrEmpty || CarryUpgradeCosts.IsDefaultOrEmpty || CarryMultipliers.Length != CarryUpgradeCosts.Length ||
            CarryMultipliers.Length > 16 || CarryMultipliers[0] != 1 || CarryUpgradeCosts[0] != 0 ||
            CarryMultipliers.Any(x => x is < 1 or > 100) || CarryMultipliers.Zip(CarryMultipliers.Skip(1)).Any(x => x.Second <= x.First) ||
            CarryUpgradeCosts.Skip(1).Any(x => x < 1) || QueenCommunicationCooldownTicks < 1 || Width is < 8 or > 1024 || Height is < 8 or > 1024 || InitialAnts is < 1 or > 100 || TicksPerSecond != 20 ||
            InitialStock < 0 || PopulationCap is < 1 or > 100 || InitialAnts > PopulationCap || RecruitmentCost <= 0 || MinimumReserve < 0 ||
            InitialResourceNodes is < 0 or > 20 || ResourceNodeCap is < 1 or > 100 || InitialResourceNodes > ResourceNodeCap ||
            ResourceMin < 1 || ResourceMax < ResourceMin || ResourceMax > 5 ||
            GroundResourceCap < InitialResourceNodes * ResourceMin || GroundResourceCap > 1000000000 ||
            ResourceRefreshTicks <= 0 || NestExclusionCells < 1 || InteractionCooldownTicks < 1 ||
            BrainBudgets.Length != 5 || BrainIntervals.Length != 5 || BrainCosts.Length != 5 ||
            BrainBudgets.Any(x => x < 1) || BrainIntervals.Any(x => x < 1) ||
            BrainCosts[0] != 0 || BrainCosts.Skip(1).Any(x => x <= 0) ||
            AntTypes.IsDefaultOrEmpty || AntTypes.Length > 32 ||
            AntTypes.Sum(x => x.InitialCount) != InitialAnts ||
            !AntTypes.Any(x => x.Id == AntMind.Contracts.AntSpeciesIds.Scout) ||
            !AntTypes.Any(x => x.Id == AntMind.Contracts.AntSpeciesIds.Hauler) ||
            AntTypes.Any(x => string.IsNullOrWhiteSpace(x.Id) || string.IsNullOrWhiteSpace(x.Name) ||
                x.WorkRate is <1 or >10 || x.InitialCount < 0 || x.EmptyMoveUnitsPerTick is < 1 or > 1000 ||
                x.LoadedMoveUnitsPerTick is < 1 or > 1000 || x.LoadedMoveUnitsPerTick > x.EmptyMoveUnitsPerTick ||
                x.MaxHealth is < 1 or > 10000 || x.AttackDamage is < 0 or > 1000 || x.AttackCooldownTicks < 1 ||
                x.CarryCapacity is < 1 or > 100 || x.VisionCells is < 1 or > 16) ||
            AntTypes.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != AntTypes.Length)
            throw new ArgumentException("Unsupported world configuration.");
    }
    public AntTypeConfig AntType(string id) => AntTypes.FirstOrDefault(x => x.Id == id)
        ?? throw new ArgumentException($"Unknown ant species '{id}'.", nameof(id));
}
public sealed record WorldState {
    public WorldState() : this(0,1,new WorldConfig(),[],[],0) {}
    public WorldState(long tick, ulong randomState, WorldConfig config,
        ImmutableArray<AntView> ants, ImmutableArray<ResourceView> resources, long inventory = 100) {
        Tick = tick; RandomState = randomState; Config = config; Inventory = inventory;
        Objects = new EntityDatabase().Put(new QueenObject(1, Point.FromCells(config.Width / 2, config.Height / 2)) { Health = config.Combat.QueenHealth, MaxHealth = config.Combat.QueenHealth })
            .ReplaceAnts(ants).ReplaceActiveFood(resources);
    }
    public long Tick { get; init; }
    public ulong RandomState { get; init; }
    public WorldConfig Config { get; init; }
    public EntityDatabase Objects { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public ImmutableArray<AntView> Ants { get => Objects.All<AntView>().Where(x => x.Health > 0).ToImmutableArray(); init => Objects = Objects.ReplaceAnts(value); }
    [System.Text.Json.Serialization.JsonIgnore]
    public ImmutableArray<ResourceView> Resources { get => Objects.All<ResourceView>().Where(x => x.Amount > 0).ToImmutableArray(); init => Objects = Objects.ReplaceActiveFood(value); }
    public ImmutableArray<EnemyView> Enemies => Objects.All<EnemyView>().Where(x => x.Health > 0).ToImmutableArray();
    public int NextEnemyId { get; init; } = 1;
    public int EnemiesDefeated { get; init; }
    public int AntsLost { get; init; }
    public bool IsDefeated => Objects.Find("queen", 1) is QueenObject { Health: <= 0 };
    public FoodStore Stock { get; init; } = new();
    [System.Text.Json.Serialization.JsonIgnore]
    public long Inventory { get => Stock.FoodTotal(Config.Foods); init => Stock = FoodStore.Fruit(value); }
    public ImmutableDictionary<int,BuildingView> ObservedBuildings {get;init;}=ImmutableDictionary<int,BuildingView>.Empty;
    public ImmutableArray<SourceView> QueenSources {get;init;}=[];
    public ImmutableArray<BuildingView> QueenBuildings {get;init;}=[];
    public ImmutableDictionary<int,string> TickActions {get;init;}=ImmutableDictionary<int,string>.Empty;
    public int NextBuildingId {get;init;}=1;
    public long FirstAdmissionPoints {get;init;}
    public ImmutableArray<EconomyEvent> EconomyEvents {get;init;}=[];
    public ImmutableDictionary<string,long> CreatedItems {get;init;}=ImmutableDictionary<string,long>.Empty;
    public ImmutableDictionary<string,long> ConsumedItems {get;init;}=ImmutableDictionary<string,long>.Empty;
    public long FoodEaten { get; init; }
    public long HealthRestored { get; init; }
    public ImmutableArray<GroupOrder> Groups { get; init; } = [];
    public int NextGroupId { get; init; } = 1;
    public ImmutableArray<ResourceClaim> Claims { get; init; } = [];
    public long Delivered { get; init; }
    public long Generated { get; init; }
    public int NextResourceId { get; init; } = 1;
    public int BrainLevel { get; init; } = 1;
    public int RecruitmentSpent { get; init; }
    public int UpgradeSpent { get; init; }
    public ImmutableDictionary<string, MilestoneProgress> MilestoneProgress { get; init; } = ImmutableDictionary<string, MilestoneProgress>.Empty;
    public int NetworkLevel { get; init; } = 1;
    public int NetworkUpgradeSpent { get; init; }
    public int QueenRangeCells => Config.NetworkRanges[NetworkLevel - 1];
    public bool IsTechnologyUnlocked(string id) => Config.Technologies.Any(x => x.Id == id && NetworkLevel >= x.RequiredNetworkLevel);
    public int HaulerCarryLevel { get; init; } = 1;
    public int CarryUpgradeSpent { get; init; }
    public int CarryCapacity(string species) => checked(Config.AntType(species).CarryCapacity *
        (species == AntSpeciesIds.Hauler ? Config.CarryMultipliers[HaulerCarryLevel - 1] : 1));
    public int Eggs { get; init; }
    public int EggsProduced { get; init; }
    public int EggsHatched { get; init; }
    public ImmutableArray<ResourceView> QueenResources { get; init; } = [];
    public ImmutableDictionary<string, long> DeliveredBySpecies { get; init; } =
        ImmutableDictionary<string, long>.Empty.WithComparers(StringComparer.Ordinal);
    public ImmutableDictionary<string, int> ActiveProgramVersions { get; init; } =
        ImmutableDictionary<string, int>.Empty.WithComparers(StringComparer.Ordinal);
}
public interface IWorldRule {
    string Id { get; }
    int Order { get; }
    WorldState Apply(WorldState world);
}
public static class RandomSource {
    public static ulong Next(ref ulong state) { state ^= state << 13; state ^= state >> 7; state ^= state << 17; return state; }
}
public sealed class Simulation {
    private readonly ImmutableArray<IWorldRule> rules;
    public Simulation(IEnumerable<IWorldRule> rules) {
        var array = rules.OrderBy(x => x.Order).ThenBy(x => x.Id, StringComparer.Ordinal).ToImmutableArray();
        if (array.Any(x => string.IsNullOrWhiteSpace(x.Id)) || array.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != array.Length)
            throw new ArgumentException("Rule IDs must be nonempty and unique.");
        this.rules = array;
    }
    public WorldState Step(WorldState world) {
        if (world.IsDefeated) return world;
        var next = world with { Tick = checked(world.Tick + 1) };
        foreach (var rule in rules) next = rule.Apply(next);
        return next;
    }
    public static WorldState Create(WorldConfig config, ulong seed) {
        config.Validate();
        var rng = seed == 0 ? 1UL : seed;
        var ants = config.AntTypes
            .SelectMany(type => Enumerable.Range(0, type.InitialCount)
                .Select(_ => new AntView(0, Point.FromCells(config.Width / 2, config.Height / 2), 0, Species: type.Id,
                    Memory: Enumerable.Repeat(0, 16).ToImmutableArray(),
                    RecentDecisions: ImmutableArray<AntDecisionLogEntry>.Empty) { Health = type.MaxHealth, MaxHealth = type.MaxHealth }))
            .Select((ant, index) => ant with { Id = index + 1 }).ToImmutableArray();
        var world = new WorldState(0, rng, config, ants, [], config.InitialStock) {
            DeliveredBySpecies = config.AntTypes.ToImmutableDictionary(x => x.Id, _ => 0L, StringComparer.Ordinal),
            ActiveProgramVersions = config.AntTypes.ToImmutableDictionary(x => x.Id, _ => 0, StringComparer.Ordinal)
            };
        world=Economy.Created(world,FoodTypes.Fruit,config.InitialStock);
        for (int i = 0; i < config.InitialResourceNodes; i++) world = ResourcePlacement.TryAdd(world);
        if (world.Resources.Length != config.InitialResourceNodes)
            throw new ArgumentException("Cannot place all initial resources with this seed and configuration.");
        return new ResourceDiscoveryRule().Apply(ProductionFactory.Create(world));
    }
}
