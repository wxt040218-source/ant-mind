using System.Collections.Immutable;
using AntMind.Contracts;
namespace AntMind.Programs;
// No world handle is exposed: interpreters receive only the individual's permitted observations.
public sealed record Perception(int AntId, long Tick, Point Position, Point Nest, int Carry,
    Point? CurrentTarget, int Width, int Height, ImmutableArray<ResourceView> Nearby,
    string Species = AntMind.Contracts.AntSpeciesIds.Scout,
    int CarryCapacity = 5, int VisionCells = 4, AntAction CurrentAction = AntAction.Wait,
    ImmutableArray<int> Memory = default, bool QueenConnected = false,
    ImmutableArray<ResourceView> QueenResources = default, long QueenInfoTick = -1,
    ActionStatus ActionStatus = ActionStatus.None, ActionStatus QueenRequestStatus = ActionStatus.None,
    ActionStatus QueenReportStatus = ActionStatus.None) {
    public int Level {get;init;}=1;
    public int Experience {get;init;}
    public int ExperienceToNext {get;init;}
    public int MovePercent {get;init;}=100;
    public int WorkPercent {get;init;}=100;
    public int AttackDamage {get;init;}
    public int TechnologyLevel {get;init;}=1;
    public ImmutableArray<RecipeDefinition> Recipes {get;init;}=[];
    public ImmutableArray<SourceView> Sources {get;init;}=[];
    public ImmutableArray<BuildingDefinition> BuildingTypes {get;init;}=[];
    public ImmutableArray<BuildingView> Buildings {get;init;}=[];
    public ImmutableArray<SourceView> QueenSources {get;init;}=[];
    public ImmutableArray<BuildingView> QueenBuildings {get;init;}=[];
    public ImmutableArray<EnemyView> NearbyEnemies { get; init; } = [];
    public int Health { get; init; } = 12;
    public int MaxHealth { get; init; } = 12;
    public string CarryFood { get; init; } = FoodTypes.Fruit;
    public int GroupId { get; init; }
    public GroupOrder? Order { get; init; }
    public ImmutableArray<FoodDefinition> FoodDefinitions { get; init; } = [new(FoodTypes.Fruit,"水果",3),new(FoodTypes.Meat,"肉",6)];
    public ImmutableDictionary<string,long> NestStock { get; init; } = ImmutableDictionary<string,long>.Empty;
    public ImmutableArray<AllyView> NearbyAllies { get; init; } = [];
    public ImmutableArray<ResourceClaim> NearbyClaims { get; init; } = [];
    public ImmutableArray<ResourceClaim> QueenClaims { get; init; } = [];
}
public sealed record Decision(string Branch, int Spent, Point? Target, AntAction Action,
    ImmutableArray<int> Memory = default, string? Error = null,
    bool RequestQueenInfo = false, bool ReportDiscovery = false) {
    public ProductionIntent Production {get;init;}=new();
    public string EatFood { get; init; } = FoodTypes.Fruit;
    public int EatHolderId {get;init;}=-1;
    public int PickupFoodId { get; init; }
    public int FollowAntId { get; init; }
    public int FollowDistance { get; init; }
    public int ClaimFoodId { get; init; }
    public int ClaimQuantity { get; init; }
    public int ClaimDuration { get; init; }
    public bool ReleaseClaim { get; init; }
    public int AttackTargetId { get; init; }
}
public interface IAntProgram { Decision Execute(Perception perception, InstructionBudget budget); }
public sealed class InstructionBudget(int limit) {
    public int Initial { get; } = limit;
    public int Remaining { get; private set; } = limit > 0 ? limit : throw new ArgumentOutOfRangeException(nameof(limit));
    public bool TrySpend(int amount) {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        if (amount > Remaining) return false;
        Remaining -= amount; return true;
    }
}
