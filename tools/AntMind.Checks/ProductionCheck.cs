using System.Collections.Immutable;
using System.Text.Json;
using AntMind.Contracts;
using AntMind.Core;
using AntMind.Programs;
using AntMind.Runtime;
internal static class ProductionCheck {
    public static async Task Run(Action<bool,string> check) {
        var config=ContentLoader.LoadDefault() with {InitialResourceNodes=0,Combat=new()};
        var world=Simulation.Create(config,42);
        check(world.Objects.All<SourceView>().Length==12 && world.Objects.All<SourceView>().Count(x=>x.ItemType==FoodTypes.Fiber)==4,"sources are deterministic independent fruit and fiber records");
        var source=world.Objects.All<SourceView>()[0];
        var grown=new SourceGrowthRule().Apply(world with {Tick=100});
        check(grown.Objects.All<SourceView>()[0].Amount==source.Capacity,"full sources do not bank growth or overflow");
        var empty=source with {Amount=0};world=world with {Objects=world.Objects.Put(empty)};
        grown=new SourceGrowthRule().Apply(world with {Tick=200});
        check(((SourceView)grown.Objects.Find("source",source.Id)!).Amount==1 && grown.CreatedItems[FoodTypes.Fruit]==world.CreatedItems[FoodTypes.Fruit]+1,"renewal creates exactly one physical raw unit");
        check(new FoodStore().Add(FoodTypes.Fiber,100).FoodTotal(config.Foods)==0,"construction fiber cannot finance eggs, upgrades or food reserves");
        var action=new ProductionActionRule();var work=new WorkRule();
        var point=Point.FromCells(64,64);
        var chef=new AntView(1,point,0,Species:AntSpeciesIds.Chef,Memory:Enumerable.Repeat(0,16).ToImmutableArray(),RecentDecisions:[]) {Health=10,MaxHealth=16};
        world=Simulation.Create(config,43);
        world=world with {Ants=[chef],Objects=world.Objects.Put(new BuildingView(1,point,"workshop")),NextBuildingId=2};
        world=world with {Ants=[chef],Tick=1};
        WorldState Act(AntAction intent,int id,string type="",int qty=1,int recipe=0) {
            var a=world.Ants[0] with {Intent=intent,Production=new(id,type,qty,recipe),LastInteractionTick=-1000000};
            world=action.Apply(world with {Tick=world.Tick+1,Ants=[a]});return world;
        }
        world=work.Apply(world with {Ants=[world.Ants[0] with {Intent=AntAction.Work,Production=new(1)}]});
        check(world.Ants[0].ActionResult=="missing_materials" && ((BuildingView)world.Objects.Find("building",1)!).ConstructionWork==0,"blueprints cannot build without delivered fiber");
        // Move real fiber from its natural holder through a hauler to construction.
        var fiber=world.Objects.All<SourceView>().First(x=>x.ItemType==FoodTypes.Fiber);
        world=world with {HaulerCarryLevel=10,Ants=[world.Ants[0] with {Species=AntSpeciesIds.Hauler,Position=fiber.Position}]};
        Act(AntAction.Harvest,fiber.Id,qty:10);
        check(world.Ants[0].Carry==10 && ((SourceView)world.Objects.Find("source",fiber.Id)!).Amount==20,"harvest transfers physical fiber with capacity checks");
        world=world with {Ants=[world.Ants[0] with {Position=point}]};Act(AntAction.Deposit,1,FoodTypes.Fiber,10);
        world=world with {Ants=[world.Ants[0] with {Position=fiber.Position}]};Act(AntAction.Harvest,fiber.Id,qty:2);
        world=world with {Ants=[world.Ants[0] with {Position=point}]};Act(AntAction.Deposit,1,FoodTypes.Fiber,2);
        var site=(BuildingView)world.Objects.Find("building",1)!;
        check(site.Store.Get(FoodTypes.Fiber)==12 && world.Stock.Get(FoodTypes.Fiber)==0,"construction materials are held at the site, never teleported from stock");
        world=world with {Ants=[world.Ants[0] with {Species=AntSpeciesIds.Chef}]};
        for(var t=0;t<100;t++)world=work.Apply(world with {Tick=world.Tick+1,Ants=[world.Ants[0] with {Intent=AntAction.Work,Production=new(1)}]});
        site=(BuildingView)world.Objects.Find("building",1)!;
        check(site.Phase=="ready" && site.Store.Total==0 && world.ConsumedItems[FoodTypes.Fiber]==12,"chef completes 200 effective construction ticks in 100 ticks and consumes materials once");
        // Two newly harvested fruit -> one concentrate -> first nest admission = two raw points.
        var fruit=world.Objects.All<SourceView>().First(x=>x.ItemType==FoodTypes.Fruit);
        world=world with {Ants=[world.Ants[0] with {Carry=0,Position=fruit.Position,WorkTicksRemaining=0}]};
        Act(AntAction.Harvest,fruit.Id,qty:2);world=world with {Ants=[world.Ants[0] with {Position=point}]};
        Act(AntAction.Deposit,1,FoodTypes.Fruit,2);Act(AntAction.Process,1,recipe:1);
        check(((BuildingView)world.Objects.Find("building",1)!).WorkInProgress.Get(FoodTypes.Fruit)==2,"processing reserves real inputs in a separate WIP holder");
        var stalled=world;world=work.Apply(world with {Tick=world.Tick+5,Ants=[world.Ants[0] with {Intent=AntAction.Wait,WorkTicksRemaining=0}]});
        check(((BuildingView)world.Objects.Find("building",1)!).RecipeWork==0,"workshop makes no progress without explicit labor");
        for(var t=0;t<50;t++)world=work.Apply(world with {Tick=world.Tick+1,Ants=[world.Ants[0] with {Intent=AntAction.Work,Production=new(1)}]});
        site=(BuildingView)world.Objects.Find("building",1)!;
        check(site.Output.Get(FoodTypes.Concentrate)==1 && site.WorkInProgress.Total==0 && site.Recipe=="","one recipe completes once and leaves output at the station");
        Act(AntAction.Withdraw,1,FoodTypes.Concentrate);Act(AntAction.Deposit,0,FoodTypes.Concentrate);
        check(world.Stock.Get(FoodTypes.Concentrate)==1 && world.FirstAdmissionPoints==2,"processed food inherits two raw first-admission points");
        Act(AntAction.Withdraw,0,FoodTypes.Concentrate);world=new InteractionRule().Apply(world with {Ants=[world.Ants[0] with {Intent=AntAction.Drop,LastInteractionTick=-1000000}]});
        world=new InteractionRule().Apply(world with {Tick=world.Tick+10,Ants=[world.Ants[0] with {Intent=AntAction.Pickup}]});
        world=new InteractionRule().Apply(world with {Tick=world.Tick+10,Ants=[world.Ants[0] with {Intent=AntAction.Deliver}]});
        check(world.FirstAdmissionPoints==2,"nest withdrawal, drop, pickup and redelivery cannot mint admission points");
        // Mixed credit survives a partial transfer; credited units remain implicit.
        var mixed=new FoodStore().Put(FoodTypes.Fruit,2,[new(1,1)]).Take(FoodTypes.Fruit,1);
        check(ItemCredit.Points(mixed.Credits)==1 && ItemCredit.Points(mixed.Store.Credits[FoodTypes.Fruit])==0,"partial mixed-credit split preserves total provenance");
        var eaten=new EatingRule().Apply(world with {Tick=2000,Ants=[world.Ants[0] with {Health=5,Carry=1,CarryFood=FoodTypes.Concentrate,CargoTracked=true,CargoCredits=[],EatFood=FoodTypes.Concentrate,HealUntilTick=2000}]});
        check(eaten.Ants[0].Health==11 && eaten.Ants[0].Carry==0 && eaten.FirstAdmissionPoints==2,"concentrated food heals through the existing explicit eating channel without minting income");
        var meal=world.Config.Production.Recipes.Single(x=>x.Input==FoodTypes.Meat);
        check(meal.Output==FoodTypes.Meal && world.Config.Foods.Single(x=>x.Id==meal.Output).HealAmount==12,"meat processing is a separate registered edible recipe");
        var p=new Perception(1,1,point,point,0,null,128,128,[],Memory:Enumerable.Repeat(0,16).ToImmutableArray()) {FoodDefinitions=config.Foods,Sources=[fruit],Buildings=[site]};
        var dsl=AntDslCompiler.Compile("let s=source_id_at(0); if(source_amount(s)>0){harvest(s,1);} else {withdraw(building_id_at(0),food_concentrate,1);}").Execute(p,new(64));
        check(dsl.Error is null && dsl.Action==AntAction.Harvest,"DSL queries local sources and emits explicit production intentions within budget");
        var chefScript=AntDslCompiler.Compile(DefaultProgramSources.Chef).Execute(p with {Order=new(1,"kitchen","",point,point,point) {Parameters=ImmutableDictionary<string,int>.Empty.Add("station",1).Add("recipe",1)}},new(64));
        check(chefScript.Error is null && chefScript.Action==AntAction.Process,"shared chef program respects level-one instruction budget");
        check(ItemLedger.Imbalance(world).Values.All(x=>x==0),"every item type balances across natural sources, cargo, nest, construction, WIP, output and consumption");
        // Independent caches, advisory transfer and concurrent last-unit resolution.
        var cache=new BuildingView(2,point,"cache") {Phase="ready",Store=new FoodStore().Put(FoodTypes.Fruit,1,[new(1,1)])};
        var a1=chef with {Carry=0,Intent=AntAction.Withdraw,Production=new(2,FoodTypes.Fruit,1)};
        var a2=a1 with {Id=2};
        var race=action.Apply(world with {Tick=999,Objects=world.Objects.Put(cache),Ants=[a1,a2]});
        check(race.Ants.Sum(a=>a.Carry)==1 && ((BuildingView)race.Objects.Find("building",2)!).Store.Total==0,"two ants cannot withdraw the same last cache item");
        var transfer=action.Apply(race with {Tick=1010,Ants=[race.Ants[0] with {Intent=AntAction.Transfer,Production=new(2,FoodTypes.Fruit,1)},race.Ants[1]]});
        check(transfer.Ants[0].Carry==0 && transfer.Ants[1].Carry==1 && ItemCredit.Points(transfer.Ants[1].CargoCredits)==1,"direct ant transfer preserves quantity and admission provenance");
        var interrupted=work.Apply(stalled with {Tick=stalled.Tick+1,Ants=[stalled.Ants[0] with {Intent=AntAction.Work,Production=new(1),LastDamageTick=stalled.Tick+1}]});
        check(interrupted.Ants[0].ActionResult=="work_interrupted" && ((BuildingView)interrupted.Objects.Find("building",1)!).WorkInProgress.Total==2,"damage interrupts labor while preserving station inputs and progress");
        var emptied=new InteractionRule().Apply(world with {Tick=2100,Resources=[new ResourceView(99,point,1,0) {Credits=[new(1,1)],CreditTracked=true}],Ants=[chef with {Intent=AntAction.Pickup}]});
        check(((ResourceView)emptied.Objects.Find("food",99)!).Credits.IsEmpty,"depleted ground tombstones release their remaining provenance metadata");
        check(AntDslCompiler.Compile("harvest(1,item_code(\"fiber\"));").Execute(p,new(64)).Production.Quantity==3,"stable item IDs resolve independently of hardcoded numeric literals");
        // Public API discovery and real save/restore through actor.
        var saves=Path.Combine(Directory.GetCurrentDirectory(),"artifacts","check-saves",Guid.NewGuid().ToString("N"));
        await using var session=new GameSession(config,42,savesPath:saves);
        var initial=await session.ObserveAsync();
        check(initial.Sources.Length<12 && initial.Sources.All(x=>x.LastObservedTick==0),"public source projection hides undiscovered locations");
        var hidden=await Gateway.DispatchAsync(session,new(Protocol.Version,"inspect_object",ObjectKind:"source",ObjectId:999),default);
        check(hidden.Error=="object_not_found","unknown source queries use the common not-found response");
        async Task<CommandResult> Cmd(CommandKind kind,string key="",int value=0) {var v=await session.ObserveAsync();return await session.ExecuteAsync(new(Guid.NewGuid(),v.Revision,kind,value,"production checks") {Key=key,ExpectedWorldId=v.WorldId});}
        var saved=await Cmd(CommandKind.SaveGame,"checkpoint");
        check(saved.Accepted && session.ListSaves().Length==1,"atomic save writes a full actor-owned checkpoint and lists metadata");
        var evalBefore=await session.ObserveAsync();
        var evaluated=session.EvaluateSave(new("checkpoint",40));
        check(JsonSerializer.Serialize(evaluated.Baseline)==JsonSerializer.Serialize(evaluated.Candidate) && (await session.ObserveAsync()).Revision==evalBefore.Revision,"same-checkpoint evaluation is deterministic and does not mutate the live actor");
        // Saving midway through an active batch retains WIP and pending program versions.
        var pendingCatalog=new SpeciesProgramCatalog(config.AntTypes.Select(x=>x.Id));
        pendingCatalog.TryQueueApply(AntSpeciesIds.Chef,"memory[0]=7; wait();","saved pending update",out _,out _);
        SavedGames.Write(saves,"in-progress",new(SavedGames.Format,Protocol.Version,stalled,pendingCatalog.Export()));
        var restoredBatch=SavedGames.Read(saves,"in-progress");
        check(((BuildingView)restoredBatch.World.Objects.Find("building",1)!).WorkInProgress.Get(FoodTypes.Fruit)==2 && restoredBatch.Programs.Single(x=>x.Species==AntSpeciesIds.Chef).Pending==1,"checkpoint retains work-in-progress inputs and pending species programs");
        var corruptPrograms=restoredBatch with {Programs=restoredBatch.Programs.Select(slot=>slot.Species==AntSpeciesIds.Chef?slot with {Versions=slot.Versions.SetItem(0,slot.Versions[0] with {Source="while(true){}"})}:slot).ToImmutableArray()};
        SavedGames.Write(saves,"bad-program",corruptPrograms);
        var validRevision=(await session.ObserveAsync()).Revision;
        check(!(await Cmd(CommandKind.LoadGame,"bad-program")).Accepted && (await session.ObserveAsync()).Revision==validRevision,"saved invalid programs are rejected before replacing world or program catalog");
        var before=await session.ObserveAsync();
        for(var i=0;i<40;i++)await Cmd(CommandKind.Step);
        var branchA=await session.ObserveAsync();var restored=await Cmd(CommandKind.LoadGame,"checkpoint");
        var loaded=await session.ObserveAsync();
        check(restored.Accepted && loaded.Paused && loaded.Tick==before.Tick && loaded.WorldId!=before.WorldId,"load restores paused state and rotates the world instance");
        var old=await session.ExecuteAsync(new(Guid.NewGuid(),loaded.Revision,CommandKind.Step) {ExpectedWorldId=before.WorldId});
        check(old.Code=="world_conflict" && (await session.ObserveAsync()).Tick==loaded.Tick,"a pre-load world command cannot write the restored world");
        for(var i=0;i<40;i++)await Cmd(CommandKind.Step);
        var branchB=await session.ObserveAsync();
        check(JsonSerializer.Serialize(branchA.Ants)==JsonSerializer.Serialize(branchB.Ants) && JsonSerializer.Serialize(branchA.Resources)==JsonSerializer.Serialize(branchB.Resources) && branchA.FirstAdmissionPoints==branchB.FirstAdmissionPoints,"restored RNG, programs and memories reproduce the same continuation");
        await File.WriteAllTextAsync(Path.Combine(saves,"broken.json"),"{bad");var revision=(await session.ObserveAsync()).Revision;
        check(!(await Cmd(CommandKind.LoadGame,"broken")).Accepted && (await session.ObserveAsync()).Revision==revision,"corrupt saves leave the current world unchanged");
        check(!(await Cmd(CommandKind.SaveGame,"../escape")).Accepted,"save slot IDs cannot escape the save directory");
    }
}
