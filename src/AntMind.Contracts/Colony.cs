using System.Collections.Immutable;
namespace AntMind.Contracts;

public static class FoodTypes {
    public const string Fruit = "fruit";
    public const string Meat = "meat";
    public const string Fiber = "fiber";
    public const string Wood = "wood";
    public const string Concentrate = "fruit_concentrate";
    public const string Meal = "protein_meal";
    public const string Balanced = "balanced_meal";
    public const string Royal = "royal_feast";
}
public sealed record FoodDefinition(string Id, string Name, int HealAmount) {
    public bool Edible {get;init;}=true;
    public int UnitMass {get;init;}=1;
    public int Value {get;init;}=1;
}
public static class GroupActivities {
    public const string Collect="collect";
    public const string Explore="explore";
    public const string Guard="guard";
}
public sealed record GroupOrder(int Id, string Name, string Task, Point Rally, Point Objective, Point Retreat, int Version = 1) {
    public ImmutableDictionary<string,int> Parameters { get; init; } = ImmutableDictionary<string,int>.Empty;
    public ImmutableDictionary<string,Point> Points { get; init; } = ImmutableDictionary<string,Point>.Empty;
    public string Activity { get; init; } = GroupActivities.Collect;
    public string FoodFilter { get; init; } = "";
    public int RadiusCells { get; init; } = 8;
}
public sealed record ResourceClaim(int AntId, int FoodId, int Quantity, long ExpiresTick);
public sealed record AllyView(int Id, string Species, int GroupId, Point Position, int Health, int MaxHealth);

// Value object: one authoritative map, totals are always derived. Missing types have zero stock.
public sealed record FoodStore {
    public ImmutableDictionary<string,long> Amounts { get; init; } = ImmutableDictionary<string,long>.Empty;
    public ImmutableDictionary<string,ImmutableArray<CreditLot>> Credits {get;init;}=ImmutableDictionary<string,ImmutableArray<CreditLot>>.Empty;
    public long Total => Amounts.Values.Sum();
    public long Get(string type) => Amounts.GetValueOrDefault(type);
    public FoodStore Add(string type, long amount) {
        var next = checked(Get(type) + amount);
        if (next < 0) throw new InvalidOperationException("Negative food stock.");
        var credits=Credits.GetValueOrDefault(type,[]);
        if(amount<0)credits=ItemCredit.Split(credits,-amount).Left;
        return this with { Amounts = Amounts.SetItem(type,next),Credits=Credits.SetItem(type,credits) };
    }
    public FoodStore Put(string type,long amount,ImmutableArray<CreditLot> credits) {
        if(amount<0 || credits.Any(x=>x.Quantity<1 || x.PointsPerUnit<1) || credits.Sum(x=>x.Quantity)>amount)throw new ArgumentException("Invalid item batch.");
        var result=Add(type,amount);
        return result with {Credits=result.Credits.SetItem(type,ItemCredit.Merge(result.Credits.GetValueOrDefault(type,[]),credits))};
    }
    public (FoodStore Store,ImmutableArray<CreditLot> Credits) Take(string type,long quantity) {
        if(quantity<0 || quantity>Get(type))throw new InvalidOperationException("Insufficient items.");
        var taken=ItemCredit.Split(Credits.GetValueOrDefault(type,[]),quantity).Taken;
        return (Add(type,-quantity),taken);
    }
    public long FoodTotal(IEnumerable<FoodDefinition> definitions)=>definitions.Where(x=>x.Edible).Sum(x=>Get(x.Id));
    public long FoodValue(IEnumerable<FoodDefinition> definitions)=>definitions.Where(x=>x.Edible).Sum(x=>checked(Get(x.Id)*x.Value));
    // Whole items are consumed; excess value becomes change, never a lost fraction or a physical item.
    public (FoodStore Store,long Change) PayValue(long cost,long change,IEnumerable<FoodDefinition> definitions) {
        var items=definitions.Where(x=>x.Edible).OrderBy(x=>x.Value).ThenBy(x=>x.Id,StringComparer.Ordinal).ToArray();
        if(cost<0 || change<0 || cost>checked(FoodValue(items)+change))throw new InvalidOperationException("Insufficient food value.");
        var fromChange=Math.Min(cost,change);cost-=fromChange;change-=fromChange;
        var next=this;
        foreach(var item in items) {
            if(cost==0)break;
            var quantity=Math.Min(next.Get(item.Id),checked((cost-1)/item.Value+1));
            var paid=checked(quantity*item.Value);next=next.Add(item.Id,-quantity);
            if(paid>=cost){change=checked(change+paid-cost);cost=0;}else cost-=paid;
        }
        return (next,change);
    }
    public FoodStore SpendFood(long amount,IEnumerable<FoodDefinition> definitions) {
        if(amount<0 || amount>FoodTotal(definitions))throw new InvalidOperationException("Insufficient food.");
        var next=this;
        foreach(var item in definitions.Where(x=>x.Edible).OrderBy(x=>x.Id==FoodTypes.Fruit?0:1).ThenBy(x=>x.Id,StringComparer.Ordinal)) {
            var take=Math.Min(amount,next.Get(item.Id));next=next.Add(item.Id,-take);amount-=take;
        }
        return next;
    }
    public FoodStore Spend(long amount) {
        if (amount < 0 || amount > Total) throw new InvalidOperationException("Insufficient food stock.");
        var next=this;
        foreach(var key in Amounts.Keys.OrderBy(x=>x==FoodTypes.Fruit ? 0 : 1).ThenBy(x=>x,StringComparer.Ordinal)) {
            var take=Math.Min(amount,next.Get(key)); next=next.Add(key,-take); amount-=take;
        }
        return next;
    }
    public static FoodStore Fruit(long amount) => new FoodStore().Add(FoodTypes.Fruit,amount);
}
