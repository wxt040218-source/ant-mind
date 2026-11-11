using System.Collections.Immutable;
namespace AntMind.Contracts;

// Quantities live in their holder; these runs carry only the remaining first-admission credit.
// Already credited units are implicit. Splitting always takes uncredited runs first.
public sealed record CreditLot(long Quantity, long PointsPerUnit);
public static class ItemCredit {
    public static bool IsRawFood(string type) => type is FoodTypes.Fruit or FoodTypes.Meat;
    public static ImmutableArray<CreditLot> Raw(string type, long quantity) => IsRawFood(type) && quantity>0 ? [new(quantity,1)] : [];
    public static long Points(ImmutableArray<CreditLot> lots) => lots.Sum(x=>checked(x.Quantity*x.PointsPerUnit));
    public static (ImmutableArray<CreditLot> Taken, ImmutableArray<CreditLot> Left) Split(ImmutableArray<CreditLot> lots,long quantity) {
        var take=ImmutableArray.CreateBuilder<CreditLot>(); var left=ImmutableArray.CreateBuilder<CreditLot>();
        foreach(var lot in lots) { var n=Math.Min(quantity,lot.Quantity); if(n>0)take.Add(lot with {Quantity=n}); if(n<lot.Quantity)left.Add(lot with {Quantity=lot.Quantity-n});quantity-=n; }
        return (take.ToImmutable(),left.ToImmutable());
    }
    public static ImmutableArray<CreditLot> Merge(ImmutableArray<CreditLot> a,ImmutableArray<CreditLot> b) => a.Concat(b).GroupBy(x=>x.PointsPerUnit).OrderByDescending(x=>x.Key).Select(x=>new CreditLot(x.Sum(y=>y.Quantity),x.Key)).ToImmutableArray();
}
public sealed record SourceView(int Id,Point Position,string ItemType,int Amount,int Capacity,int GrowthTicks) : IWorldObject {
    public string Kind=>"source";
    public bool IsDiscovered {get;init;}
    public int LastObservedAmount {get;init;}
    public long LastObservedTick {get;init;}=-1;
    public ImmutableDictionary<string,string> Properties {get;init;}=ImmutableDictionary<string,string>.Empty;
}
public sealed record RecipeDefinition(int Code,string Id,string Name,string Input,int InputCount,string Output,int OutputCount,int WorkTicks,int RequiredNetworkLevel=1) {
    public ImmutableDictionary<string,int> ExtraInputs {get;init;}=ImmutableDictionary<string,int>.Empty;
    public string BuildingType {get;init;}="workshop";
    public ImmutableDictionary<string,int> Ingredients()=>ExtraInputs.Add(Input,InputCount);
}
public sealed record BuildingDefinition(string Id,string Name,int MaterialCost,int BuildTicks,int Capacity,int RequiredNetworkLevel=1) {
    public bool CanFeedAnts {get;init;}
    public string MaterialType {get;init;}=FoodTypes.Wood;
    public string BuilderSpecies {get;init;}=AntSpeciesIds.Builder;
}
public sealed record BuildingView(int Id,Point Position,string Type) : IWorldObject {
    // A plan keeps its material contract even when future content definitions change.
    // Empty uses the definition; legacy sites explicitly keep fiber.
    public string ConstructionItem {get;init;}="";
    public string Kind=>"building";
    public string Phase {get;init;}="blueprint";
    public FoodStore Store {get;init;}=new();
    public FoodStore Output {get;init;}=new();
    public FoodStore WorkInProgress {get;init;}=new();
    public int ConstructionWork {get;init;}
    public string Recipe {get;init;}="";
    public int RecipeWork {get;init;}
    public bool IsDiscovered {get;init;}
    public long LastObservedTick {get;init;}=-1;
    public ImmutableDictionary<string,string> Properties {get;init;}=ImmutableDictionary<string,string>.Empty;
}
public sealed record ProductionIntent(int ObjectId=0,string ItemType="",int Quantity=0,int RecipeCode=0);
public sealed record EconomyEvent(long Tick,string Metric,string Item,long Quantity,int AntId,string Species,int GroupId);
public sealed record EconomyStats(long WindowTicks,long FirstAdmissionPoints,double PointsPerMinute,ImmutableDictionary<string,long> Harvested,ImmutableDictionary<string,long> Produced,ImmutableDictionary<string,long> Actions,ImmutableDictionary<string,long> Failures) {
    public long ProcessingValueAdded {get;init;}
    public double ValueAddedPerMinute {get;init;}
    public ImmutableDictionary<string,long> PointsBySpecies {get;init;}=ImmutableDictionary<string,long>.Empty;
    public ImmutableDictionary<int,long> PointsByGroup {get;init;}=ImmutableDictionary<int,long>.Empty;
}

public sealed record EvaluationRequest(string Slot,int Ticks,string Species="",string Source="");
public sealed record EvaluationBranch(long StartTick,long EndTick,long NewFoodPoints,long GrossDelivered,int AntsLost,int ProgramErrors,ImmutableDictionary<string,long> InventoryDelta) {
    public long ProcessingValueAdded {get;init;}
    public long DevelopmentValueDelta {get;init;}
}
public sealed record EvaluationReport(string Slot,int Ticks,EvaluationBranch Baseline,EvaluationBranch Candidate);
