using System.Collections.Immutable;
using System.Text.Json.Nodes;
using AntMind.Contracts;
using AntMind.Core;
using AntMind.Programs;
using AntMind.Runtime;

internal static class OutpostCheck {
    public static void Run(Action<bool,string> check) {
        var config=ContentLoader.LoadDefault() with {InitialStock=0,MinimumReserve=1000,InitialResourceNodes=0,Combat=new()};
        var point=Point.FromCells(15,15);var eat=new EatingRule();
        var baseWorld=Simulation.Create(config,19001);
        var ant=baseWorld.Ants[0] with {Position=point,Carry=1,CarryFood=FoodTypes.Fruit,CargoTracked=true,CargoCredits=[new(1,1)],Health=5,MaxHealth=12,Intent=AntAction.Eat,EatHolderId=1,EatFood=FoodTypes.Fruit};
        var site=new BuildingView(1,point,"cache") {Phase="ready",Store=new FoodStore().Put(FoodTypes.Fruit,2,[new(2,1)]).Add(FoodTypes.Wood,4)};
        var world=Economy.Created(Economy.Created(baseWorld with {Objects=baseWorld.Objects.Put(site),Ants=[ant],NextBuildingId=2},FoodTypes.Fruit,3),FoodTypes.Wood,4);
        var started=eat.Apply(world with {Tick=1});var early=eat.Apply(started with {Tick=40});var done=eat.Apply(started with {Tick=41});
        check(started.Ants[0].HealUntilTick==41 && early.Ants[0].Health==5 && ((BuildingView)early.Objects.Find("building",1)!).Store.Get(FoodTypes.Fruit)==2,"outpost eating channels for 40 ticks without early healing or charging");
        check(done.Ants[0].Health==8 && done.Ants[0].Carry==1 && done.Stock.Total==0 && ((BuildingView)done.Objects.Find("building",1)!).Store.Get(FoodTypes.Fruit)==1 && done.FoodEaten==1 && done.HealthRestored==3,"outpost meals consume station stock, preserve cargo and nest stock, and ignore unrelated nest reserve");
        check(done.FirstAdmissionPoints==0 && ItemLedger.Imbalance(done).Values.All(x=>x==0) && ItemCredit.Points(((BuildingView)done.Objects.Find("building",1)!).Store.Credits[FoodTypes.Fruit])==1,"outpost consumption removes its real provenance without admission income or item imbalance");
        var rich=world with {Objects=world.Objects.Put(site with {Store=new FoodStore().Put(FoodTypes.Concentrate,1,[new(1,2)])}),Ants=[ant with {EatFood=FoodTypes.Concentrate,Carry=0}]};
        rich=eat.Apply(rich with {Tick=1});rich=eat.Apply(rich with {Tick=41});
        check(rich.Ants[0].Health==11 && ((BuildingView)rich.Objects.Find("building",1)!).Store.Total==0,"outposts use the registered per-food healing values for processed food too");
        var empty=eat.Apply(world with {Tick=1,Objects=world.Objects.Put(site with {Store=new()})});
        check(empty.Ants[0].ActionResult=="insufficient_food" && empty.Ants[0].Health==5 && empty.Ants[0].Carry==1,"empty outposts cannot silently fall back to carried or remote nest food");
        foreach(var phase in new[]{"blueprint","cancelled"}) {
            var rejected=eat.Apply(world with {Tick=1,Objects=world.Objects.Put(site with {Phase=phase})});
            check(rejected.Ants[0].ActionResult=="station_not_ready" && rejected.FoodEaten==0,"unbuilt or cancelled outposts cannot heal: "+phase);
        }
        var kitchen=eat.Apply(world with {Tick=1,Objects=world.Objects.Put(site with {Type="workshop"})});
        check(kitchen.Ants[0].ActionResult=="not_a_feeding_station","warehouses only provide meals when their actual building definition permits it");
        var full=eat.Apply(world with {Tick=1,Ants=[ant with {Health=12}]});
        var wood=eat.Apply(world with {Tick=1,Ants=[ant with {EatFood=FoodTypes.Wood}]});
        check(full.FoodEaten==0 && full.Ants[0].ActionResult=="already_healthy" && wood.Ants[0].ActionResult=="invalid_food_type","full ants and construction materials never waste supplies as healing food");
        var far=eat.Apply(world with {Tick=1,Ants=[ant with {Position=new(point.X+1001,point.Y)}]});
        var unknown=eat.Apply(world with {Tick=1,Ants=[ant with {EatHolderId=999}]});
        var edge=eat.Apply(world with {Tick=1,Ants=[ant with {Position=new(point.X+1000,point.Y+1000)}]});
        check(far.Ants[0].ActionResult==unknown.Ants[0].ActionResult && far.Ants[0].ActionResult=="feeding_station_not_in_reach" && edge.Ants[0].HealUntilTick==41,"station meals enforce the existing one-cell range and do not distinguish unknown from unreachable stations");
        var interrupted=eat.Apply(started with {Tick=2,Ants=[started.Ants[0] with {LastDamageTick=2,Health=3}]});
        check(interrupted.Ants[0].HealUntilTick==0 && interrupted.Ants[0].ActionResult=="eating_interrupted" && interrupted.FoodEaten==0 && ((BuildingView)interrupted.Objects.Find("building",1)!).Store.Total==6,"damage interrupts an outpost meal without consuming food or construction items");
        var race=eat.Apply(world with {Tick=1,Objects=world.Objects.Put(site with {Store=new FoodStore().Put(FoodTypes.Fruit,1,[new(1,1)])}),Ants=[ant,ant with {Id=2}]});
        race=eat.Apply(race with {Tick=41});
        check(race.FoodEaten==1 && race.Ants.Count(a=>a.Health==8)==1 && ((BuildingView)race.Objects.Find("building",1)!).Store.Total==0,"two outpost eaters cannot consume the same last portion twice");
        var lost=eat.Apply(started with {Tick=20,Objects=started.Objects.Put(site with {Store=new()})});
        check(lost.Ants[0].ActionResult=="insufficient_food" && lost.FoodEaten==0,"food removed by another explicit action cancels a meal rather than creating free healing");

        var local=new Perception(1,1,point,Point.FromCells(64,64),0,null,128,128,[],Memory:ant.Memory) {Health=8,MaxHealth=40,Species="soldier",BuildingTypes=config.Production.Buildings,Buildings=[site],FoodDefinitions=config.Foods};
        var decision=AntDslCompiler.Compile("eat_from(nearest_supply_id,nearest_supply_food);").Execute(local,new(64));
        check(decision.Error is null && decision.Action==AntAction.Eat && decision.EatHolderId==1 && decision.EatFood==FoodTypes.Fruit,"DSL supplies an explicit fixed holder and food choice from local station perception");
        var hidden=AntDslCompiler.Compile("memory[0]=nearest_supply_id;memory[1]=building_can_feed(1);").Execute(local with {Buildings=[]},new(64));
        check(hidden.Memory[0]==0 && hidden.Memory[1]==0,"supply lookup cannot expose an unseen outpost");
        var guard=AntDslCompiler.Compile(DefaultProgramSources.Soldier);var healing=guard.Execute(local,new(64));
        var attack=guard.Execute(local with {NearbyEnemies=[new(1,new(point.X+500,point.Y),18,18,1)]},new(64));
        var walking=guard.Execute(local with {Position=new(point.X+2000,point.Y)},new(64));
        check(healing.Error is null && healing.Action==AntAction.Eat && healing.EatHolderId==1 && walking.Error is null && walking.Target==point && attack.Action==AntAction.Attack,"default guards approach visible stocked outposts and heal, while melee threats still take priority");
        var peak=0;
        foreach(var hp in new[]{8,20,40})foreach(var enemyCount in new[]{0,1,3})foreach(var stations in new[]{0,1,3}) {
            var sense=local with {Health=hp,Buildings=Enumerable.Range(1,stations).Select(i=>site with {Id=i,Position=new(point.X+i*1000,point.Y)}).ToImmutableArray(),NearbyEnemies=Enumerable.Range(1,enemyCount).Select(i=>new EnemyView(i,new(point.X+2000+i*1000,point.Y),18,18,1)).ToImmutableArray()};
            var d=guard.Execute(sense,new(64));if(d.Error is not null)throw new Exception("Outpost guard budget: "+d.Error);peak=Math.Max(peak,d.Spent);
        }
        check(peak<=64,"outpost-aware guards fit the basic brain across health/threat/station branches (peak "+peak+")");

        var directory=Path.Combine(Directory.GetCurrentDirectory(),"artifacts","outpost-checks",Guid.NewGuid().ToString("N"));
        var catalog=new SpeciesProgramCatalog(config.AntTypes.Select(a=>a.Id));
        SavedGames.Write(directory,"mid-meal",new(SavedGames.Format,Protocol.Version,started with {Tick=20},catalog.Export()));
        var resumed=SavedGames.Read(directory,"mid-meal");resumed=resumed with {World=eat.Apply(resumed.World with {Tick=41})};
        check(resumed.World.Ants[0].EatHolderId==1 && resumed.World.Ants[0].Health==8 && resumed.World.FoodEaten==1 && resumed.World.RandomState==started.RandomState,"mid-meal checkpoint restores the exact outpost source and charges once at its saved completion tick");
        var legacyWorld=baseWorld with {Ants=[ant with {Position=Point.FromCells(64,64),Carry=0,CargoCredits=[],EatHolderId=-1,HealUntilTick=41,Intent=AntAction.Wait}],Stock=FoodStore.Fruit(1),Config=config with {MinimumReserve=0,Production=config.Production with {Buildings=config.Production.Buildings.Select(b=>b with {CanFeedAnts=false}).ToImmutableArray()}}};
        SavedGames.Write(directory,"legacy",new(4,17,legacyWorld,catalog.Export()));
        var path=Path.Combine(directory,"legacy.json");var node=JsonNode.Parse(File.ReadAllText(path))!;
        void Strip(JsonNode? n) {if(n is JsonObject o){o.Remove("EatHolderId");o.Remove("CanFeedAnts");foreach(var child in o.ToArray())Strip(child.Value);}else if(n is JsonArray a)foreach(var child in a)Strip(child);}
        Strip(node);File.WriteAllText(path,node.ToJsonString());var oldBytes=File.ReadAllBytes(path);var migrated=SavedGames.Read(directory,"legacy");
        var legacyDone=eat.Apply(migrated.World with {Tick=41});
        check(migrated.Format==SavedGames.Format && migrated.Protocol==Protocol.Version && migrated.World.Config.Production.Buildings.Single(b=>b.Id=="cache").CanFeedAnts && migrated.World.Ants[0].EatHolderId==-1 && legacyDone.Ants[0].Health==8 && oldBytes.SequenceEqual(File.ReadAllBytes(path)),"wire17 saves without new fields gain outpost service while preserving old meals and original files");
    }
}
