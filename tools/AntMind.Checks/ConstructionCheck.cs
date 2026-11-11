using System.Collections.Immutable;
using System.Text.Json.Nodes;
using AntMind.Contracts;
using AntMind.Core;
using AntMind.Programs;
using AntMind.Runtime;

internal static class ConstructionCheck {
    public static async Task Run(Action<bool,string> check) {
        var config=ContentLoader.LoadDefault() with {Combat=new(),InitialResourceNodes=0};
        var world=Simulation.Create(config,1601);var nest=world.Ants[0].Position;var point=new Point(nest.X+3000,nest.Y);
        var def=config.Production.Buildings.Single(b=>b.Id=="cache");
        check(config.Foods.Single(f=>f.Id==FoodTypes.Wood) is {Edible:false,Value:0} && config.AntType(AntSpeciesIds.Builder).RequiredNetworkLevel==1,"wood is nonfood and construction profession bootstraps at technology one");
        var wood=world.Objects.All<SourceView>().First(s=>s.ItemType==FoodTypes.Wood);
        var depleted=world with {Objects=world.Objects.Put(wood with {Amount=wood.Amount-1}),Tick=600};
        var renewed=new SourceGrowthRule().Apply(depleted);
        check(((SourceView)renewed.Objects.Find("source",wood.Id)!).Amount==wood.Capacity && renewed.CreatedItems[FoodTypes.Wood]==depleted.CreatedItems[FoodTypes.Wood]+1,"wood renewal accounts for every newly created material unit");
        var site=new BuildingView(1,point,"cache") {ConstructionItem=FoodTypes.Wood,Store=new FoodStore().Add(FoodTypes.Wood,def.MaterialCost)};
        var worker=world.Ants[0] with {Species=AntSpeciesIds.Builder,Position=point,Intent=AntAction.Work,Production=new(1)};
        var work=new WorkRule();world=world with {Objects=world.Objects.Put(site),Ants=[worker],Tick=1,NextBuildingId=2};
        foreach(var species in new[]{AntSpeciesIds.Scout,AntSpeciesIds.Hauler,AntSpeciesIds.Chef,AntSpeciesIds.Soldier}) {
            var refused=work.Apply(world with {Ants=[worker with {Species=species}]});
            check(refused.Ants[0].ActionResult=="builder_required" && ((BuildingView)refused.Objects.Find("building",1)!).ConstructionWork==0,"only building profession constructs: "+species);
        }
        var partial=work.Apply(world with {Objects=world.Objects.Put(site with {Store=new FoodStore().Add(FoodTypes.Wood,def.MaterialCost-1)})});
        check(partial.Ants[0].ActionResult=="missing_materials" && partial.PendingExperience.IsEmpty,"partial material delivery cannot generate construction progress or experience");
        var wrong=new ProductionActionRule().Apply(world with {Objects=world.Objects.Put(site with {Store=new()}),Ants=[worker with {Carry=1,CarryFood=FoodTypes.Fiber,Intent=AntAction.Deposit,Production=new(1,FoodTypes.Fiber,1)}]});
        check(wrong.Ants[0].ActionResult=="construction_material_mismatch" && wrong.Ants[0].Carry==1 && ((BuildingView)wrong.Objects.Find("building",1)!).Store.Total==0,"new wood sites reject fiber without losing cargo");
        var mixed=work.Apply(world with {Ants=[worker with {Species=AntSpeciesIds.Chef},worker with {Id=2}]});
        check(mixed.Ants[0].ActionResult=="builder_required" && mixed.Ants[1].ActionResult=="working","an invalid non-builder cannot reserve the labor slot ahead of a valid builder");
        var race=work.Apply(world with {Ants=[worker,worker with {Id=2}]});
        check(((BuildingView)race.Objects.Find("building",1)!).ConstructionWork==2 && race.Ants[1].ActionResult=="worker_slot_busy","two builders cannot double occupy one construction labor slot");
        var finished=work.Apply(world with {Objects=world.Objects.Put(site with {ConstructionWork=def.BuildTicks-1})});
        var again=work.Apply(finished with {Tick=2,Ants=[finished.Ants[0] with {Intent=AntAction.Work}]});
        check(((BuildingView)again.Objects.Find("building",1)!) is {Phase:"ready",Store.Total:0} && again.ConsumedItems[FoodTypes.Wood]==def.MaterialCost && finished.PendingExperience.Single().Amount==1,"completion consumes wood once and rewards actual final work only");
        check(!BuildingPlacement.IsFree(nest,nest,128,128,[]) && !BuildingPlacement.IsFree(new(-1000,0),nest,128,128,[]) &&
            !BuildingPlacement.IsFree(point,nest,128,128,[site]) && BuildingPlacement.IsFree(point,nest,128,128,[site with {Phase="cancelled"}]),"shared map placement respects bounds, queen clearance, active sites and cancelled sites");
        var p=new Perception(1,1,point,nest,0,null,128,128,[],Species:AntSpeciesIds.Builder,QueenConnected:true) {Buildings=[site],BuildingTypes=config.Production.Buildings,FoodDefinitions=config.Foods,
            Order=new(1,"build","",point,point,point) {Parameters=ImmutableDictionary<string,int>.Empty.Add("station",1)}};
        var program=AntDslCompiler.Compile(DefaultProgramSources.Builder);var maximum=0;
        foreach(var pos in new[]{point,nest})foreach(var ready in new[]{false,true}) {
            var decision=program.Execute(p with {Position=pos,Buildings=[site with {Phase=ready?"ready":"blueprint"}]},new(64));maximum=Math.Max(maximum,decision.Spent);
            check(decision.Error is null && decision.Spent<=64,"default builder runs its communication/travel/work/idle branches within level-one budget");
        }
        var chefWaiting=AntDslCompiler.Compile(DefaultProgramSources.Chef).Execute(p with {Species=AntSpeciesIds.Chef},new(64));
        check(chefWaiting.Error is null && chefWaiting.Action==AntAction.Wait,"default chef waits for a builder to finish the kitchen");
        var query=AntDslCompiler.Compile("memory[0]=building_phase(1); memory[1]=building_material(1); memory[2]=building_cost(1); memory[3]=species_builder; wait();").Execute(p,new(64));
        check(query.Error is null && query.Memory[0]==1 && query.Memory[1]==8 && query.Memory[2]==6 && query.Memory[3]==1,"DSL exposes observed site phase, material contract, cost and own profession");
        var dir=Path.Combine(Directory.GetCurrentDirectory(),"artifacts","construction-checks",Guid.NewGuid().ToString("N"));
        await using var session=new GameSession(config,1602,savesPath:dir);
        async Task<CommandResult> Cmd(CommandKind kind,string key="",Point? location=null,int value=0,string species="scout") {
            var s=await session.ObserveAsync();return await session.ExecuteAsync(new(Guid.NewGuid(),s.Revision,kind,value,"construction check",species) {ExpectedWorldId=s.WorldId,Key=key,Location=location});
        }
        var before=await session.ObserveAsync();
        check((await Cmd(CommandKind.PlanBuild,"cache",nest)).Code=="site_occupied" && (await session.ObserveAsync()).Revision==before.Revision,"rejected placement is atomic and does not advance revision");
        check((await Cmd(CommandKind.PlanBuild,"cache",point)).Accepted && (await Cmd(CommandKind.PlanBuild,"cache",point)).Code=="site_occupied","actor owns blueprint placement and rejects overlapping plans");
        var planned=await session.ObserveAsync();
        check(planned.Inventory==before.Inventory && planned.Buildings.Single().Store.Total==0 && planned.Buildings.Single().ConstructionItem==FoodTypes.Wood,"placing only creates a wood contract, never teleports materials or spends food");
        await Cmd(CommandKind.ProduceEgg);var hatch=await Cmd(CommandKind.HatchEgg,species:AntSpeciesIds.Builder);
        check(hatch.Accepted && (await session.ObserveAsync()).Ants.Any(a=>a.Species==AntSpeciesIds.Builder),"universal eggs hatch builders through the same public actor command");
        check((await Cmd(CommandKind.CancelBuild,value:1)).Accepted && (await session.ObserveAsync()).Buildings.Single().Phase=="cancelled","public cancellation retains a separate cancelled construction record");

        // A real serialized v3 shape: old FiberCost, no builder registry, no wood definitions or sources.
        var legacy=Simulation.Create(config,1603);
        var oldConfig=config with {AntTypes=config.AntTypes.Where(a=>a.Id!=AntSpeciesIds.Builder).ToImmutableArray(),Foods=config.Foods.Where(f=>f.Id!=FoodTypes.Wood).ToImmutableArray(),
            Production=config.Production with {Buildings=config.Production.Buildings.Select(b=>b.Id=="cache"?b with {MaterialCost=9}:b).ToImmutableArray()}};
        var objects=new EntityDatabase();foreach(var obj in legacy.Objects.Records.Values.Where(o=>o is not SourceView source || source.ItemType!=FoodTypes.Wood))objects=objects.Put(obj);
        objects=objects.Put(new SourceView(50,new(nest.X-5000,nest.Y),FoodTypes.Fiber,21,30,0));
        objects=objects.Put(site with {ConstructionItem="",Store=new FoodStore().Add(FoodTypes.Fiber,9),ConstructionWork=99});
        legacy=legacy with {Config=oldConfig,Objects=objects,CreatedItems=legacy.CreatedItems.Remove(FoodTypes.Wood).SetItem(FoodTypes.Fiber,30),NextBuildingId=2};
        var catalog=new SpeciesProgramCatalog(oldConfig.AntTypes.Select(a=>a.Id));SavedGames.Write(dir,"legacy",new(3,14,legacy,catalog.Export()));
        var path=Path.Combine(dir,"legacy.json");var node=JsonNode.Parse(File.ReadAllText(path))!;
        var prod=node["World"]!["Config"]!["Production"]!.AsObject();prod.Remove("WoodSources");prod.Remove("WoodCapacity");prod.Remove("WoodGrowthTicks");
        foreach(var b in prod["Buildings"]!.AsArray().OfType<JsonObject>()) {b.Remove("MaterialCost",out var cost);b["FiberCost"]=cost;b.Remove("MaterialType");b.Remove("BuilderSpecies");}
        File.WriteAllText(path,node.ToJsonString());var bytes=File.ReadAllBytes(path);var migrated=SavedGames.Read(dir,"legacy");
        var oldSite=(BuildingView)migrated.World.Objects.Find("building",1)!;
        check(migrated.Format==SavedGames.Format && migrated.Protocol==Protocol.Version && oldSite.ConstructionItem==FoodTypes.Fiber && oldSite.Store.Get(FoodTypes.Fiber)==9 && oldSite.ConstructionWork==99 && bytes.SequenceEqual(File.ReadAllBytes(path)),"legacy migration preserves custom fiber cost, materials, work and the original save file");
        check(migrated.World.Objects.All<SourceView>().Count(s=>s.ItemType==FoodTypes.Wood)==4 && migrated.Programs.Any(p=>p.Species==AntSpeciesIds.Builder) && ItemLedger.Imbalance(migrated.World).Values.All(v=>v==0),"old colonies gain deterministic wood sources and a shared builder program with complete material accounting");
        var oldFinished=work.Apply(migrated.World with {Tick=1,Ants=[worker]});
        check(((BuildingView)oldFinished.Objects.Find("building",1)!).Phase=="ready" && oldFinished.ConsumedItems[FoodTypes.Fiber]==9,"builders can finish a legacy fiber site without converting or duplicating old materials");
        SavedGames.Write(dir,"current",migrated);var loaded=SavedGames.Read(dir,"current");
        check(loaded.World.Objects.All<SourceView>().Count(s=>s.ItemType==FoodTypes.Wood)==4 && loaded.World.Config.Production.Buildings.Single(b=>b.Id=="cache").MaterialType==FoodTypes.Wood,"save4 reload is idempotent and future plans use wood");
    }
}
