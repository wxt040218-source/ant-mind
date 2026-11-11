using System.Collections.Immutable;
namespace AntMind.Contracts;
// Coordinates are fixed-point: 1000 units per map cell. No floating-point world state.
public readonly record struct Point(int X, int Y) {
    public const int UnitsPerCell = 1000;
    public static Point FromCells(int x, int y) => new(checked(x * UnitsPerCell), checked(y * UnitsPerCell));
}
public static class AntSpeciesIds {
    public const string Scout = "scout";
    public const string Hauler = "hauler";
    public const string Soldier = "soldier";
    public const string Chef = "chef";
    public const string Builder = "builder";
}
public enum ActionStatus { None, Running, Succeeded, Failed }
public enum AntAction { Wait, Move, Pickup, Deliver, Drop, Attack, Eat, Follow, Harvest, Deposit, Withdraw, Transfer, Work, Process }
public sealed record AntDecisionLogEntry(long Tick, int ProgramVersion, string Branch, int Spent, string? Error);
[System.Text.Json.Serialization.JsonDerivedType(typeof(AntView), "ant")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(ResourceView), "food")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(QueenObject), "queen")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(EnemyView), "enemy")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(SourceView), "source")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(BuildingView), "building")]
public interface IWorldObject {
    int Id { get; }
    string Kind { get; }
    Point Position { get; }
    ImmutableDictionary<string, string> Properties { get; }
}
public sealed record AntView(int Id, Point Position, int Carry, Point? Target = null,
    AntAction Intent = AntAction.Wait, long NextDecisionTick = 0, long LastInteractionTick = -1000000,
    string Species = AntSpeciesIds.Scout,
    ImmutableArray<int> Memory = default, int ProgramVersion = 0, string LastDecisionBranch = "",
    int LastInstructionSpent = 0, string? LastError = null,
    ImmutableArray<AntDecisionLogEntry> RecentDecisions = default, bool QueenConnected = false,
    ImmutableArray<ResourceView> ResourceReports = default) : IWorldObject {
    public string Kind => "ant";
    public int Level {get;init;}=1;
    public int Experience {get;init;}
    public long CompletedWork {get;init;}
    public int WorkExperienceRemainder {get;init;}
    public int WorkRateRemainder {get;init;}
    public long LastExperienceTick {get;init;}=-1;
    public string LastExperienceWork {get;init;}="";
    public ImmutableArray<CreditLot> CargoCredits {get;init;}=[];
    public bool CargoTracked {get;init;}
    public int KnownTechnologyLevel {get;init;}=1;
    public ProductionIntent Production {get;init;}=new();
    public int WorkTicksRemaining {get;init;}
    public ImmutableArray<SourceView> SourceReports {get;init;}=[];
    public ImmutableArray<SourceView> QueenSources {get;init;}=[];
    public ImmutableArray<BuildingView> BuildingReports {get;init;}=[];
    public ImmutableArray<BuildingView> QueenBuildings {get;init;}=[];
    public string CarryFood { get; init; } = FoodTypes.Fruit;
    public int GroupId { get; init; }
    public GroupOrder? ReceivedOrder { get; init; }
    public ImmutableArray<ResourceClaim> QueenClaims { get; init; } = [];
    public long HealUntilTick { get; init; }
    public string EatFood { get; init; } = FoodTypes.Fruit;
    // -1 keeps legacy cargo/nest eating; 0 explicitly selects nest; positive selects a feeding building.
    public int EatHolderId {get;init;}=-1;
    public long LastDamageTick { get; init; } = -1000000;
    public int PickupFoodId { get; init; }
    public int FollowAntId { get; init; }
    public int FollowDistance { get; init; }
    public int ClaimFoodId { get; init; }
    public int ClaimQuantity { get; init; }
    public int ClaimDuration { get; init; }
    public bool ReleaseClaim { get; init; }
    public int Health { get; init; } = 12;
    public int MaxHealth { get; init; } = 12;
    public long LastAttackTick { get; init; } = -1000000;
    public int AttackTargetId { get; init; }
    public long CreatedTick { get; init; }
    public ImmutableDictionary<string, string> Properties { get; init; } = ImmutableDictionary<string, string>.Empty;
    public bool RequestQueenInfo { get; init; }
    public bool ReportDiscovery { get; init; }
    public long LastQueenCommunicationTick { get; init; } = -1000000;
    public long QueenInfoTick { get; init; } = -1;
    public ImmutableArray<ResourceView> QueenKnowledge { get; init; } = [];
    public ActionStatus QueenRequestStatus { get; init; }
    public ActionStatus QueenReportStatus { get; init; }
    public ActionStatus ActionStatus { get; init; }
    public string ActionResult { get; init; } = "none";
    public ImmutableArray<ResourceView> ResourceReports { get; init; } = ResourceReports.IsDefault ? [] : ResourceReports;
}
public enum ResourceOrigin { Natural, Dropped, Carcass }
public sealed record ResourceView(int Id, Point Position, int Amount, long LastSeenTick, int MaxCapacity = 0) : IWorldObject {
    public string Kind => "food";
    public ImmutableArray<CreditLot> Credits {get;init;}=[];
    public bool CreditTracked {get;init;}
    public string FoodType { get; init; } = FoodTypes.Fruit;
    public ResourceOrigin Origin { get; init; }
    public int MaxCapacity { get; init; } = MaxCapacity > 0 ? MaxCapacity : Math.Max(0, Amount);
    public long CreatedTick { get; init; } = LastSeenTick;
    public bool IsDiscovered { get; init; }
    public long DiscoveredTick { get; init; } = -1;
    public int LastObservedAmount { get; init; }
    public long LastObservedTick { get; init; } = -1;
    public ImmutableDictionary<string, string> Properties { get; init; } = ImmutableDictionary<string, string>.Empty;
}
public sealed record QueenObject(int Id, Point Position) : IWorldObject {
    public string Kind => "queen";
    public int Health { get; init; } = 100;
    public int MaxHealth { get; init; } = 100;
    public ImmutableDictionary<string, string> Properties { get; init; } = ImmutableDictionary<string, string>.Empty;
}
public sealed record EnemyView(int Id, Point Position, int Health, int MaxHealth, int FoodAmount) : IWorldObject {
    public string Kind => "enemy";
    public string Name { get; init; } = "掠食甲虫";
    public long CreatedTick { get; init; }
    public long LastAttackTick { get; init; } = -1000000;
    public int? CorpseFoodId { get; init; }
    public bool IsDiscovered { get; init; }
    public Point LastKnownPosition { get; init; }
    public int LastKnownHealth { get; init; }
    public long LastSeenTick { get; init; } = -1;
    public ImmutableDictionary<string, string> Properties { get; init; } = ImmutableDictionary<string, string>.Empty;
}
public sealed record AntSpeciesSnapshot(string Id, string Name, int EmptyMoveUnitsPerTick,
    int LoadedMoveUnitsPerTick, int CarryCapacity, int VisionCells, int Count, long Delivered) {
    public int RequiredNetworkLevel {get;init;}=1;
    public bool Unlocked {get;init;}=true;
    public int WorkRate {get;init;}=1;
    public int MaxHealth { get; init; }
    public int AttackDamage { get; init; }
    public int CarryLevel { get; init; } = 1;
    public int NextCarryCapacity { get; init; }
    public int NextCarryUpgradeCost { get; init; }
}
public sealed record ProgramVersionSnapshot(int Version, string Reason);
public sealed record SpeciesProgramSnapshot(string Species, int ActiveVersion, int? PendingVersion,
    string ActiveSource, ImmutableArray<ProgramVersionSnapshot> History) {
    public int GroupId { get; init; }
}
public enum Visibility { Discovered, Full }
public enum CommandKind { Pause, Resume, Step, Speed, Visibility, Recruit, UpgradeBrain, ApplyProgram, RollbackProgram, SetQueenRange, SetMinimumReserve, SetPopulationCap, ProduceEgg, HatchEgg, UpgradeCarry, UpgradeNetwork, CreateGroup, UpdateGroup, DeleteGroup, AssignAnt, ClearGroupProgram, AssignAnts, SetGroupParameter, RemoveGroupParameter, SetGroupPoint, RemoveGroupPoint, PlanBuild, CancelBuild, SaveGame, LoadGame, ConfigureGroup }
public sealed record GameCommand(Guid RequestId, long ExpectedRevision, CommandKind Kind, int Value = 0,
    string Reason = "", string Species = AntSpeciesIds.Scout, string ProgramSource = "") {
    public GroupConfiguration? GroupConfiguration {get;init;}
    public Guid? ExpectedWorldId {get;init;}
    public int GroupId { get; init; }
    public int AntId { get; init; }
    public ImmutableArray<int> AntIds { get; init; } = [];
    public string Key { get; init; } = "";
    public Point? Location { get; init; }
    public string Name { get; init; } = "";
    public string Task { get; init; } = "";
    public string? Activity { get; init; }
    public string? FoodFilter { get; init; }
    public int? RadiusCells { get; init; }
    public Point? Rally { get; init; }
    public Point? Objective { get; init; }
    public Point? Retreat { get; init; }
}
public sealed record CommandResult(Guid RequestId, bool Accepted, string Code, long Revision, string? Detail = null);
public sealed record Snapshot(int SchemaVersion, long Revision, long Tick, bool Paused, int Speed,
    Visibility Visibility, int Width, int Height, Point Nest, ImmutableArray<AntView> Ants,
    ImmutableArray<ResourceView> Resources, long Inventory = 0, long Delivered = 0, long? Generated = null,
    int BrainLevel = 1, int RecruitmentSpent = 0, int UpgradeSpent = 0,
    ImmutableArray<AntSpeciesSnapshot> Species = default,
    ImmutableArray<SpeciesProgramSnapshot> Programs = default, int QueenRangeCells = 16,
    ImmutableArray<ResourceView> QueenResources = default) {
    public ImmutableArray<AntLevelDefinition> AntLevels {get;init;}=[];
    public Guid WorldId {get;init;}
    public int TechnologyLevel=>NetworkLevel;
    public string TechnologyEra {get;init;}="";
    public ImmutableArray<TechnologyEra> TechnologyEras {get;init;}=[];
    public long StoredFoodValue {get;init;}
    public long DevelopmentChange {get;init;}
    public long DevelopmentValue {get;init;}
    public long ProcessingValueAdded {get;init;}
    public long FirstAdmissionPoints {get;init;}
    public EconomyStats? Economy {get;init;}
    public ImmutableArray<SourceView> Sources {get;init;}=[];
    public ImmutableArray<BuildingView> Buildings {get;init;}=[];
    public ImmutableArray<RecipeDefinition> Recipes {get;init;}=[];
    public ImmutableArray<BuildingDefinition> BuildingTypes {get;init;}=[];
    public ImmutableArray<ResourceView> QueenResources { get; init; } = QueenResources.IsDefault ? [] : QueenResources;
    public ImmutableDictionary<string,long> Stock { get; init; } = ImmutableDictionary<string,long>.Empty;
    public ImmutableArray<FoodDefinition> FoodTypes { get; init; } = [];
    public long FoodEaten { get; init; }
    public long HealthRestored { get; init; }
    public ImmutableArray<GroupOrder> Groups { get; init; } = [];
    public ImmutableArray<ResourceClaim> Claims { get; init; } = [];
    public ImmutableArray<EnemyView> Enemies { get; init; } = [];
    public ImmutableArray<AntView> DeadAnts { get; init; } = [];
    public bool Defeated { get; init; }
    public int EnemiesDefeated { get; init; }
    public int AntsLost { get; init; }
    public ImmutableArray<MilestoneSnapshot> Milestones { get; init; } = [];
    public bool AgentConnected { get; init; }
    public ImmutableArray<string> ConnectedAgents { get; init; } = [];
    public QueenObject? Queen { get; init; }
    public int NetworkLevel { get; init; } = 1;
    public int MaxNetworkLevel { get; init; }
    public int NextNetworkRangeCells { get; init; }
    public int NextNetworkUpgradeCost { get; init; }
    public int NetworkUpgradeSpent { get; init; }
    public ImmutableArray<TechnologySnapshot> Technologies { get; init; } = [];
    public int CarryUpgradeSpent { get; init; }
    public int Eggs { get; init; }
    public int EggsProduced { get; init; }
    public int EggsHatched { get; init; }
    public int EggProductionCost { get; init; }
    public int MinimumReserve { get; init; }
    public int PopulationCap { get; init; }
    public int RecruitmentCost { get; init; }
    public int InstructionBudget { get; init; }
    public int DecisionInterval { get; init; }
    public int NextUpgradeCost { get; init; }

}
public sealed record TechnologyEra(int Level,string Name,string Description);
public sealed record TechnologySnapshot(string Id, string Name, int RequiredNetworkLevel, bool Unlocked) {
    public string Category {get;init;}="ability";
    public string ContentId {get;init;}="";
}
public sealed record MilestoneSnapshot(string Id, string Title, string Description, string Metric, long Target,
    long Current, string Status, long? StartedTick, long? CompletedTick, long? DeadlineTick);
public sealed record AgentPresence(Guid SessionId, string Name, bool Connected = true);
public sealed record ObjectSnapshot(string Kind, int Id, AntView? Ant = null, ResourceView? Food = null, QueenObject? Queen = null, EnemyView? Enemy = null, SourceView? Source = null, BuildingView? Building = null);
public sealed record WireRequest(int ProtocolVersion, string Method, GameCommand? Command = null,
    string? ObjectKind = null, int ObjectId = 0, AgentPresence? Agent = null, EvaluationRequest? Evaluation = null, WatchRequest? Watch=null, EventReadRequest? Events=null, PreviewRequest? Preview=null);
public sealed record WireResponse(int ProtocolVersion, Snapshot? Snapshot = null, CommandResult? Result = null, string? Error = null, ObjectSnapshot? Object = null, ImmutableArray<SaveMetadata>? Saves = null, EvaluationReport? Evaluation = null, WatchResult? Watches=null, AgentEventPage? Events=null, AgentSummary? Summary=null, ProgramPreview? Preview=null, ProgramBatchPreview? PreviewBatch=null);
public static class Protocol { public const int Version = 18; public const int MaxBytes = 1_048_576; }

public sealed record SaveMetadata(string Slot,long Bytes,DateTime ModifiedUtc);

public sealed record AntLevelDefinition(int Level,int RequiredExperience,int MovePercent,int WorkPercent,int DamagePercent,int HealthPercent);
