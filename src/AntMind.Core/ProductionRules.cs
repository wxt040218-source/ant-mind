using System.Collections.Immutable;
using AntMind.Contracts;
namespace AntMind.Core;

public sealed record ProductionConfig {
    public bool Enabled {get;init;}
    public int FruitSources {get;init;}=8;
    public int FiberSources {get;init;}=0;
    public int WoodSources {get;init;}=4;
    public int WoodCapacity {get;init;}=60;
    public int WoodGrowthTicks {get;init;}=600;
    public int FruitCapacity {get;init;}=5;
    public int GrowthTicks {get;init;}=100;
    public int FiberCapacity {get;init;}=30;
    public ImmutableArray<BuildingDefinition> Buildings {get;init;}=[new("cache","中转据点",6,100,40) {CanFeedAnts=true},new("workshop","厨房",12,200,20,2)];
    public ImmutableArray<RecipeDefinition> Recipes {get;init;}=[new(1,"concentrate","水果浓缩",FoodTypes.Fruit,2,FoodTypes.Concentrate,1,100,2),new(2,"meal","蛋白餐",FoodTypes.Meat,2,FoodTypes.Meal,1,100,3),
        new RecipeDefinition(3,"balanced","均衡营养餐",FoodTypes.Concentrate,1,FoodTypes.Balanced,1,140,4) {ExtraInputs=ImmutableDictionary<string,int>.Empty.Add(FoodTypes.Meal,1)},
        new(4,"royal","蚁王盛宴",FoodTypes.Balanced,2,FoodTypes.Royal,1,200,5)];
    public void Validate(ImmutableArray<FoodDefinition> items,int maxLevel=5) {
        if(FruitSources is <1 or >32 || FiberSources is <0 or >32 || WoodSources is <1 or >32 || WoodCapacity is <1 or >1000 || WoodGrowthTicks<1 || FruitCapacity is <1 or >100 || FiberCapacity is <1 or >1000 || GrowthTicks<1 ||
            Buildings.IsDefaultOrEmpty || Buildings.Length>16 || Buildings.Select(x=>x.Id).Distinct(StringComparer.Ordinal).Count()!=Buildings.Length ||
            Buildings.Any(x=>string.IsNullOrWhiteSpace(x.Id) || x.MaterialCost<1 || x.BuildTicks<1 || x.Capacity<2 || x.RequiredNetworkLevel<1 || x.RequiredNetworkLevel>maxLevel) ||
            Recipes.IsDefaultOrEmpty || Recipes.Length>32 || Recipes.Select(x=>x.Id).Distinct(StringComparer.Ordinal).Count()!=Recipes.Length || Recipes.Select(x=>x.Code).Distinct().Count()!=Recipes.Length ||
            Recipes.Any(x=>string.IsNullOrWhiteSpace(x.Id) || x.Code<1 || x.InputCount is <1 or >20 || x.OutputCount!=1 || x.WorkTicks<1 || x.RequiredNetworkLevel<1 || x.RequiredNetworkLevel>maxLevel ||
                x.ExtraInputs is null || x.ExtraInputs.Count>7 || x.ExtraInputs.ContainsKey(x.Input) || x.ExtraInputs.Any(i=>i.Value is <1 or >20)))
            throw new ArgumentException("Invalid production configuration.");
        if(!Enabled)return;
        var registry=items.ToDictionary(x=>x.Id,StringComparer.Ordinal);
        if(Buildings.Any(b=>!registry.TryGetValue(b.MaterialType,out var item) || item.Edible || string.IsNullOrWhiteSpace(b.BuilderSpecies)))throw new ArgumentException("Missing construction material or builder.");
        foreach(var r in Recipes) {
            var building=Buildings.FirstOrDefault(x=>x.Id==r.BuildingType);
            if(building is null || r.RequiredNetworkLevel<building.RequiredNetworkLevel || r.Ingredients().Values.Sum()>building.Capacity || r.OutputCount>building.Capacity ||
                !registry.ContainsKey(r.Output) || r.Ingredients().Keys.Any(x=>!registry.ContainsKey(x)))throw new ArgumentException("Invalid recipe holders, items or technology.");
        }
        // A DAG plus a natural bootstrap path prevents infinite processing loops and dead unlocks.
        var visiting=new HashSet<string>(StringComparer.Ordinal);var done=new HashSet<string>(StringComparer.Ordinal);
        void Visit(string item) {
            if(done.Contains(item))return;
            if(!visiting.Add(item))throw new ArgumentException("Cyclic recipes are not supported.");
            foreach(var r in Recipes.Where(r=>r.Output==item))foreach(var input in r.Ingredients().Keys)Visit(input);
            visiting.Remove(item);done.Add(item);
        }
        foreach(var recipe in Recipes)Visit(recipe.Output);
        foreach(var recipe in Recipes) {
            var reachable=new HashSet<string>([FoodTypes.Fruit,FoodTypes.Meat,FoodTypes.Fiber,FoodTypes.Wood],StringComparer.Ordinal);
            bool changed;
            do {changed=false;foreach(var r in Recipes.Where(x=>x.RequiredNetworkLevel<=recipe.RequiredNetworkLevel))if(r.Ingredients().Keys.All(reachable.Contains))changed|=reachable.Add(r.Output);}while(changed);
            if(!recipe.Ingredients().Keys.All(reachable.Contains))throw new ArgumentException("Recipe inputs are unavailable at its technology level.");
        }
    }

}
public static class Economy {
    public static ImmutableArray<CreditLot> Credits(AntView a)=>a.CargoTracked?a.CargoCredits:ItemCredit.Raw(a.CarryFood,a.Carry);
    public static ImmutableArray<CreditLot> Credits(ResourceView f)=>f.CreditTracked?f.Credits:ItemCredit.Raw(f.FoodType,f.Amount);
    public static WorldState Record(WorldState w,string metric,string item,long qty,AntView? a=null)=>w with {
        EconomyEvents=w.EconomyEvents.Add(new(w.Tick,metric,item,qty,a?.Id??0,a?.Species??"",a?.GroupId??0)) };
    public static WorldState Created(WorldState w,string item,long qty)=>w with {CreatedItems=w.CreatedItems.SetItem(item,checked(w.CreatedItems.GetValueOrDefault(item)+qty))};
    public static WorldState Consumed(WorldState w,string item,long qty)=>w with {ConsumedItems=w.ConsumedItems.SetItem(item,checked(w.ConsumedItems.GetValueOrDefault(item)+qty))};
    public static WorldState Admit(WorldState w,AntView a,string item,long qty,ImmutableArray<CreditLot> credits) {
        var points=ItemCredit.Points(credits);
        w=AntProgression.Record(w,a.Id,checked((int)Math.Min(points,w.Config.Veterancy.Thresholds[2])*w.Config.Veterancy.DeliveryExperience),"deliver");
        return Record(w with { FirstAdmissionPoints=checked(w.FirstAdmissionPoints+points) },"admission",item,points,a);
    }
    public static EconomyStats Stats(WorldState w) {
        var ticks=Math.Min(1200,w.Tick);var events=w.EconomyEvents.Where(e=>e.Tick>w.Tick-ticks).ToArray();
        ImmutableDictionary<string,long> Totals(string key)=>events.Where(e=>e.Metric==key).GroupBy(e=>e.Item).ToImmutableDictionary(g=>g.Key,g=>g.Sum(e=>e.Quantity));
        var points=events.Where(e=>e.Metric=="admission").Sum(e=>e.Quantity);
        return new(ticks,points,ticks==0?0:points*1200.0/ticks,Totals("harvest"),Totals("produced"),Totals("action"),Totals("failure")) {
            ProcessingValueAdded=events.Where(e=>e.Metric=="processing_value").Sum(e=>e.Quantity),
            ValueAddedPerMinute=ticks==0?0:events.Where(e=>e.Metric=="processing_value").Sum(e=>e.Quantity)*1200.0/ticks,
            PointsBySpecies=events.Where(e=>e.Metric=="admission").GroupBy(e=>e.Species).ToImmutableDictionary(g=>g.Key,g=>g.Sum(e=>e.Quantity)),
            PointsByGroup=events.Where(e=>e.Metric=="admission").GroupBy(e=>e.GroupId).ToImmutableDictionary(g=>g.Key,g=>g.Sum(e=>e.Quantity))
        };
    }
}
public static class ProductionFactory {
    public static WorldState Create(WorldState w) {
        if(!w.Config.Production.Enabled)return w;
        var p=w.Config.Production;var rng=w.RandomState;var nest=Point.FromCells(w.Config.Width/2,w.Config.Height/2);
        for(var i=0;i<p.FruitSources+p.FiberSources;i++) {
            Point pos;
            if(i==0)pos=new(nest.X+5000,nest.Y);
            else if(i==p.FruitSources)pos=new(nest.X,nest.Y+5000);
            else {
                var attempt=0;
                do {pos=Point.FromCells((int)(RandomSource.Next(ref rng)%(uint)w.Config.Width),(int)(RandomSource.Next(ref rng)%(uint)w.Config.Height));}
                while((CombatMath.Distance(pos,nest)<4000 || w.Objects.All<SourceView>().Any(x=>x.Position==pos) || w.Resources.Any(x=>x.Position==pos)) && ++attempt<1024);
                if(attempt>=1024)throw new ArgumentException("Cannot place production sources.");
            }
            if(pos.X>(w.Config.Width-1)*1000 || pos.Y>(w.Config.Height-1)*1000)pos=new(nest.X-3000,nest.Y-3000);
            var fruit=i<p.FruitSources;var n=fruit?p.FruitCapacity:p.FiberCapacity;
            w=w with {Objects=w.Objects.Put(new SourceView(i+1,pos,fruit?FoodTypes.Fruit:FoodTypes.Fiber,n,n,fruit?p.GrowthTicks:0))};
            w=Economy.Created(w,fruit?FoodTypes.Fruit:FoodTypes.Fiber,n);
        }
        return AddWoodSources(w with {RandomState=rng});
    }
    public static WorldState AddWoodSources(WorldState w) {
        var p=w.Config.Production;var rng=w.RandomState;var nest=((QueenObject)w.Objects.Find("queen",1)!).Position;
        var existing=w.Objects.All<SourceView>().Count(x=>x.ItemType==FoodTypes.Wood);
        for(var i=existing;i<p.WoodSources;i++) {
            Point pos=new(Math.Max(0,nest.X-5000),nest.Y);
            if(i>0 || w.Objects.All<SourceView>().Any(x=>x.Position==pos)) {
                var attempts=0;
                do {pos=Point.FromCells((int)(RandomSource.Next(ref rng)%(uint)w.Config.Width),(int)(RandomSource.Next(ref rng)%(uint)w.Config.Height));}
                while((CombatMath.Distance(pos,nest)<4000 || w.Objects.All<SourceView>().Any(x=>x.Position==pos) || w.Resources.Any(x=>x.Position==pos)) && ++attempts<1024);
                if(attempts>=1024)throw new ArgumentException("Cannot place wood sources.");
            }
            var id=w.Objects.All<SourceView>().Select(x=>x.Id).DefaultIfEmpty(0).Max()+1;
            w=w with {Objects=w.Objects.Put(new SourceView(id,pos,FoodTypes.Wood,p.WoodCapacity,p.WoodCapacity,p.WoodGrowthTicks))};
            w=Economy.Created(w,FoodTypes.Wood,p.WoodCapacity);
        }return w with {RandomState=rng};
    }
}
public sealed class SourceGrowthRule : IWorldRule {
    public string Id=>"core.source-growth";public int Order=>2;
    public WorldState Apply(WorldState w) {
        foreach(var s in w.Objects.All<SourceView>().Where(s=>s.GrowthTicks>0 && w.Tick%s.GrowthTicks==0 && s.Amount<s.Capacity)) {
            w=w with {Objects=w.Objects.Put(s with {Amount=s.Amount+1})};w=Economy.Created(w,s.ItemType,1);
        }return w;
    }
}
public sealed class ProductionActionRule : IWorldRule {
    public string Id=>"core.production-actions";public int Order=>30;
    public WorldState Apply(WorldState w) {
        foreach(var original in InstitutionMechanics.ProductionOrder(w, w.Ants.Where(a=>a.Intent is AntAction.Harvest or AntAction.Deposit or AntAction.Withdraw or AntAction.Transfer or AntAction.Process))) {
            var a=original;var args=a.Production;var error="";var qty=0;
            var type=args.ItemType;
            var building=w.Objects.Find("building",args.ObjectId) as BuildingView;
            bool Near(Point p)=>CombatMath.Distance(a.Position,p)<=1000;
            if(w.Tick-a.LastInteractionTick<w.Config.InteractionCooldownTicks)error="interaction_cooldown";
            else if(a.Intent==AntAction.Process) {
                var recipe=w.Config.Production.Recipes.FirstOrDefault(x=>x.Code==args.RecipeCode);
                if(building is null || !Near(building.Position))error="building_not_in_reach";
                else if(building.Phase!="ready" || recipe is not null && building.Type!=recipe.BuildingType)error="not_a_ready_workshop";
                else if(recipe is null)error="invalid_recipe";
                else if(w.NetworkLevel<recipe.RequiredNetworkLevel || w.NetworkLevel<w.Config.Production.Buildings.Single(x=>x.Id==building.Type).RequiredNetworkLevel)error="technology_locked";
                else if(building.Recipe.Length>0)error="workshop_busy";
                else if(recipe.Ingredients().Any(x=>building.Store.Get(x.Key)<x.Value))error="missing_inputs";
                else if(building.Output.Total+recipe.OutputCount>w.Config.Production.Buildings.Single(x=>x.Id==building.Type).Capacity)error="output_full";
                else {
                    var inputs=building.Store;var reserved=new FoodStore();
                    foreach(var ingredient in recipe.Ingredients().OrderBy(x=>x.Key,StringComparer.Ordinal)) {var batch=inputs.Take(ingredient.Key,ingredient.Value);inputs=batch.Store;reserved=reserved.Put(ingredient.Key,ingredient.Value,batch.Credits);}
                    building=building with {Store=inputs,WorkInProgress=reserved,Recipe=recipe.Id,RecipeWork=0};
                    w=w with {Objects=w.Objects.Put(building)};qty=recipe.Ingredients().Values.Sum();
                }
            } else if(args.Quantity is <1 or >100)error="invalid_quantity";
            else if(a.Intent==AntAction.Harvest) {
                var source=w.Objects.Find("source",args.ObjectId) as SourceView;
                if(source is null || !Near(source.Position))error="source_not_in_reach";
                else if(a.Carry>0 && a.CarryFood!=source.ItemType)error="mixed_cargo";
                else {
                    qty=Math.Min(args.Quantity,Math.Min(source.Amount,w.CarryCapacity(a.Species)-a.Carry));
                    if(qty<=0)error=source.Amount==0?"source_empty":"capacity_full";
                    else {type=source.ItemType;a=a with {CarryFood=type,CargoCredits=ItemCredit.Merge(Economy.Credits(a),ItemCredit.Raw(type,qty)),CargoTracked=true,Carry=a.Carry+qty};
                        w=w with {Objects=w.Objects.Put(source with {Amount=source.Amount-qty})};w=Economy.Record(w,"harvest",type,qty,a);w=AntProgression.Record(w,a.Id,qty*w.Config.Veterancy.HarvestExperience,"harvest");}
                }
            } else if(a.Intent==AntAction.Transfer) {
                var other=w.Ants.FirstOrDefault(x=>x.Id==args.ObjectId && x.Id!=a.Id && Near(x.Position));
                if(other is null)error="ally_not_in_reach";
                else if(a.CarryFood!=type || a.Carry==0 || other.Carry>0 && other.CarryFood!=type)error="item_mismatch";
                else {
                    qty=Math.Min(args.Quantity,Math.Min(a.Carry,w.CarryCapacity(other.Species)-other.Carry));
                    if(qty<=0)error="capacity_full";
                    else {var credit=ItemCredit.Split(Economy.Credits(a),qty);a=a with {Carry=a.Carry-qty,CargoCredits=credit.Left,CargoTracked=true};
                        w=w with {Objects=w.Objects.Put(other with {CarryFood=type,Carry=other.Carry+qty,CargoCredits=ItemCredit.Merge(Economy.Credits(other),credit.Taken),CargoTracked=true})};}
                }
            } else {
                var nest=(QueenObject)w.Objects.Find("queen",1)!;var isNest=args.ObjectId==0;
                if(isNest && !Near(nest.Position) || !isNest && (building is null || !Near(building.Position)))error="holder_not_in_reach";
                else {
                    var store=isNest?w.Stock:building!.Store;var output=!isNest && a.Intent==AntAction.Withdraw && building!.Output.Get(type)>0;
                    if(output)store=building!.Output;
                    if(a.Intent==AntAction.Deposit) {
                        var def=isNest?null:w.Config.Production.Buildings.Single(x=>x.Id==building!.Type);
                        var cap=isNest?long.MaxValue:building!.Phase=="blueprint"?def!.MaterialCost:def!.Capacity;
                        if(a.CarryFood!=type || a.Carry==0)error="item_mismatch";
                        else if(!isNest && building!.Phase=="blueprint" && type!=(building.ConstructionItem.Length>0?building.ConstructionItem:def!.MaterialType))error="construction_material_mismatch";
                        else if(!isNest && building!.Phase=="cancelled")error="building_cancelled";
                        else {
                            qty=(int)Math.Min(args.Quantity,Math.Min(a.Carry,cap-store.Total-(isNest?0:building!.WorkInProgress.Total)));
                            if(qty<=0)error="holder_full";
                            else {var credits=ItemCredit.Split(Economy.Credits(a),qty);store=store.Put(type,qty,isNest?[]:credits.Taken);
                                if(isNest)w=Economy.Admit(w,a,type,qty,credits.Taken);
                                a=a with {Carry=a.Carry-qty,CargoCredits=credits.Left,CargoTracked=true};}
                        }
                    } else if(a.Carry>0 && a.CarryFood!=type)error="mixed_cargo";
                    else {
                        qty=(int)Math.Min(args.Quantity,Math.Min(store.Get(type),w.CarryCapacity(a.Species)-a.Carry));
                        if(isNest && w.Config.Foods.Any(x=>x.Id==type && x.Edible))qty=(int)Math.Min(qty,Math.Max(0,w.DevelopmentValue-InstitutionMechanics.EffectiveReserve(w))/w.Config.Foods.Single(x=>x.Id==type).Value);
                        if(qty<=0)error="no_items_or_capacity";
                        else {var batch=store.Take(type,qty);store=batch.Store;a=a with {Carry=a.Carry+qty,CarryFood=type,CargoCredits=ItemCredit.Merge(Economy.Credits(a),batch.Credits),CargoTracked=true};}
                    }
                    if(error.Length==0)w=isNest?w with {Stock=store}:w with {Objects=w.Objects.Put(output?building! with {Output=store}:building! with {Store=store})};
                }
            }
            a=a with {Intent=AntAction.Wait,Target=null,ActionStatus=error.Length==0?ActionStatus.Succeeded:ActionStatus.Failed,
                ActionResult=error.Length==0?$"items_moved:{qty}":error,LastInteractionTick=error.Length==0?w.Tick:a.LastInteractionTick};
            w=w with {Objects=w.Objects.Put(a)};
            w=Economy.Record(w,error.Length==0?"flow":"failure",error.Length==0?type:error,error.Length==0?qty:1,a);
        }return w;
    }
}
public sealed class WorkRule : IWorldRule {
    public string Id=>"core.work";public int Order=>31;
    public WorldState Apply(WorldState w) {
        var occupied=new HashSet<int>();
        foreach(var a0 in w.Ants.Where(a=>a.Intent==AntAction.Work || a.WorkTicksRemaining>0)) {
            var a=a0;var b=w.Objects.Find("building",a.Production.ObjectId) as BuildingView;
            var def=b is null?null:w.Config.Production.Buildings.SingleOrDefault(x=>x.Id==b.Type);
            var error=a.LastDamageTick==w.Tick?"work_interrupted":b is null || CombatMath.Distance(a.Position,b.Position)>1000?"building_not_in_reach":
                w.NetworkLevel<def!.RequiredNetworkLevel?"technology_locked":
                b.Recipe.Length>0 && w.NetworkLevel<w.Config.Production.Recipes.Single(x=>x.Id==b.Recipe).RequiredNetworkLevel?"technology_locked":
                b.Phase=="blueprint" && a.Species!=def.BuilderSpecies?"builder_required":
                b.Phase=="blueprint" && b.Store.Get(b.ConstructionItem.Length>0?b.ConstructionItem:def.MaterialType)<def.MaterialCost?"missing_materials":
                b.Phase=="cancelled" || b.Phase=="ready" && b.Recipe.Length==0?"no_work":
                !occupied.Add(b.Id)?"worker_slot_busy":null;
            if(error is null) {
                var workValue=checked(w.Config.AntType(a.Species).WorkRate*w.Config.Veterancy.WorkPercent[a.Level-1]*InstitutionMechanics.LaborWeight(w,a.Species)/100+a.WorkRateRemainder);
                var rate=workValue/100;
                a=a with {WorkRateRemainder=workValue%100};
                var remainingWork=b!.Phase=="blueprint"?def!.BuildTicks-b.ConstructionWork:w.Config.Production.Recipes.Single(r=>r.Id==b.Recipe).WorkTicks-b.RecipeWork;
                w=AntProgression.Record(w,a.Id,Math.Min(rate,remainingWork),"work",effectiveWork:true);
                if(b!.Phase=="blueprint") {
                    b=b with {ConstructionWork=Math.Min(def!.BuildTicks,b.ConstructionWork+rate)};
                    if(b.ConstructionWork>=def.BuildTicks) {var material=b.ConstructionItem.Length>0?b.ConstructionItem:def.MaterialType;b=b with {Phase="ready",Store=b.Store.Add(material,-def.MaterialCost)};w=Economy.Consumed(w,material,def.MaterialCost);}
                } else {
                    var r=w.Config.Production.Recipes.Single(x=>x.Id==b.Recipe);
                    b=b with {RecipeWork=Math.Min(r.WorkTicks,b.RecipeWork+rate)};
                    if(b.RecipeWork>=r.WorkTicks) {
                        var points=b.WorkInProgress.Credits.Values.Sum(ItemCredit.Points);
                        var inputValue=b.WorkInProgress.FoodValue(w.Config.Foods);
                        var outputValue=checked((long)w.Config.Foods.Single(x=>x.Id==r.Output).Value*r.OutputCount);
                        var added=Math.Max(0,outputValue-inputValue);
                        foreach(var item in b.WorkInProgress.Amounts)w=Economy.Consumed(w,item.Key,item.Value);
                        var credit=points>0?ImmutableArray.Create(new CreditLot(1,points)):[];
                        b=b with {Output=b.Output.Put(r.Output,r.OutputCount,credit),WorkInProgress=new(),Recipe="",RecipeWork=0};
                        w=Economy.Created(w,r.Output,r.OutputCount);
                        w=Economy.Record(w with {ProcessingValueAdded=checked(w.ProcessingValueAdded+added)},"processing_value",r.Output,added,a);
                        w=Economy.Record(w,"produced",r.Output,r.OutputCount,a);
                    }
                }
                w=w with {Objects=w.Objects.Put(b)};
                var remaining=(a.WorkTicksRemaining==0?10:a.WorkTicksRemaining)-1;
                a=a with {WorkTicksRemaining=remaining,Intent=AntAction.Wait,Target=null,ActionStatus=remaining>0?ActionStatus.Running:ActionStatus.Succeeded,ActionResult="working"};
            } else a=a with {WorkTicksRemaining=0,Intent=AntAction.Wait,Target=null,ActionStatus=ActionStatus.Failed,ActionResult=error};
            w=w with {Objects=w.Objects.Put(a)};
        }return w;
    }
}
public sealed class ActivitySamplingRule : IWorldRule {
    public string Id=>"core.activity-sampling";public int Order=>18;
    public WorldState Apply(WorldState w)=>w with {TickActions=w.Ants.ToImmutableDictionary(a=>a.Id,a=>a.WorkTicksRemaining>0?"work":a.HealUntilTick>0?"eat":a.Intent.ToString().ToLowerInvariant())};
}
public sealed class EconomyStatisticsRule : IWorldRule {
    public string Id=>"core.economy-statistics";public int Order=>49;
    public WorldState Apply(WorldState w) {
        var events=w.EconomyEvents.Where(e=>e.Tick>w.Tick-1200).ToImmutableArray().ToBuilder();
        foreach(var g in w.Ants.GroupBy(a=>(a.Species,a.GroupId,Action:w.TickActions.GetValueOrDefault(a.Id,"wait"))))events.Add(new(w.Tick,"action",g.Key.Action,g.Count(),0,g.Key.Species,g.Key.GroupId));
        return w with {EconomyEvents=events.ToImmutable()};
    }
}
