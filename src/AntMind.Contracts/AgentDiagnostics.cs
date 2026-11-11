using System.Collections.Immutable;
namespace AntMind.Contracts;

public static class AgentEventKinds {
    public static readonly ImmutableArray<string> All=["enemy_seen","enemy_entered_zone","enemy_defeated","queen_attacked","ant_attacked","ant_died","building_ready","production_completed","task_received","program_error","program_recovered","action_failed","stock_below"];
    public static readonly ImmutableArray<string> Defaults=["enemy_entered_zone","queen_attacked","ant_died","building_ready","program_error","program_recovered","action_failed"];
}
public sealed record AgentWatch(string Id,string Name) {
    public ImmutableArray<string> Kinds {get;init;}=AgentEventKinds.Defaults;
    public Point? Center {get;init;}
    public int RadiusCells {get;init;}=8;
    public int GroupId {get;init;}
    public string Species {get;init;}="";
    public int FailureThreshold {get;init;}=3;
    public string StockItem {get;init;}=FoodTypes.Fruit;
    public long StockThreshold {get;init;}
    public bool FireInitial {get;init;}=true;
}
public sealed record WatchRequest(Guid WorldId,string Action="list",AgentWatch? Watch=null,string Id="");
public sealed record WatchResult(Guid WorldId,long Cursor,ImmutableArray<AgentWatch> Watches,string? Error=null);
public sealed record EventReadRequest(Guid WorldId,long After=0,string WatchId="",int Limit=50);
public sealed record AgentEvent(long Sequence,Guid WorldId,long Tick,long Revision,string WatchId,string Kind,string Severity,string ObjectKind,int ObjectId,Point? Position,string Detail,int Count=1,long ObservedTick=-1) {
    public long FirstTick {get;init;}=-1;
    public long LastTick {get;init;}=-1;
    public int? ProgramVersion {get;init;}
}
public sealed record AgentEventPage(Guid WorldId,long Tick,long Revision,bool Paused,long Cursor,long OldestCursor,bool Gap,bool HasMore,ImmutableArray<AgentEvent> Items,string? Error=null);
public sealed record GroupConfiguration(string Name,string Task) {
    public string Activity {get;init;}="collect";
    public string FoodFilter {get;init;}="";
    public int RadiusCells {get;init;}=8;
    public Point? Rally {get;init;}
    public Point? Objective {get;init;}
    public Point? Retreat {get;init;}
    public ImmutableDictionary<string,int> Parameters {get;init;}=ImmutableDictionary<string,int>.Empty;
    public ImmutableDictionary<string,Point> Points {get;init;}=ImmutableDictionary<string,Point>.Empty;
    public ImmutableArray<int> Members {get;init;}=[];
}
public sealed record TaskReceipt(int AntId,int GroupId,int RequestedVersion,int ReceivedVersion,bool Connected,string Status);
public sealed record GroupDiagnostics(int Id,string Name,int Version,int Members,int Acknowledged,int Pending,ImmutableArray<TaskReceipt> Receipts,ImmutableDictionary<string,int> Actions,ImmutableDictionary<string,int> Failures);
public sealed record AntDiagnostic(int Id,string Species,int GroupId,int Health,int MaxHealth,string Action,string Result,string? ProgramError,int ConsecutiveFailures);
public sealed record BuildingDiagnostic(int Id,string Type,string Phase,string Material,long DeliveredMaterial,int RequiredMaterial,long RemainingMaterial,int Work,int RequiredWork,string Recipe,int RecipeWork,ImmutableDictionary<string,long> Inputs,ImmutableDictionary<string,long> Outputs,long ObservedTick);
public sealed record AgentSummary(Guid WorldId,long Revision,long Tick,bool Paused,int Speed,int QueenHealth,int Ants,int AntsLost,int TechnologyLevel,long Inventory,long DevelopmentValue,long NewFoodPoints,long ProcessingValueAdded,EconomyStats Economy,ImmutableDictionary<string,int> Species,ImmutableArray<GroupDiagnostics> Groups,ImmutableArray<AntDiagnostic> Problems,ImmutableArray<BuildingDiagnostic> Buildings,ImmutableArray<EnemyView> KnownEnemies,long EventCursor);
public sealed record PreviewRequest(string Species,string Source,int AntId=0);
public sealed record ProgramPreview(Guid WorldId,long Revision,int AntId,string Species,int Budget,int Spent,string Action,Point? Target,string? Error,ProductionIntent Production,bool RequestQueenInfo,bool ReportDiscovery,ImmutableArray<int> Memory,ImmutableArray<string> Warnings) {
    public int EatHolderId {get;init;}=-1;
    public string EatFood {get;init;}=FoodTypes.Fruit;
}
public sealed record AntProgramPreflight(int AntId,int GroupId,ProgramPreview Current,ProgramPreview Activation);
public sealed record ProgramBatchPreview(Guid WorldId,long Revision,string Species,int Budget,int AntCount,int EvaluatedDecisions,int MaxSpent,bool Passed,ImmutableArray<int> FailedAntIds,ImmutableArray<AntProgramPreflight> Results,ImmutableArray<string> Warnings);
