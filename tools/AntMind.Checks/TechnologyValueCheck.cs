using System.Collections.Immutable;
using System.Text.Json;
using AntMind.Contracts;
using AntMind.Core;
using AntMind.Programs;
using AntMind.Runtime;

internal static class TechnologyValueCheck {
    public static async Task Run(Action<bool,string> check) {
        var config=ContentLoader.LoadDefault() with {InitialResourceNodes=0,InitialStock=0,MinimumReserve=0,Combat=new()};
        var world=Simulation.Create(config,901);
        var tiers=TechnologyProgression.Unlocks(world);
        check(tiers.Single(x=>x.Id=="building.cache").Unlocked && tiers.Single(x=>x.Id=="building.workshop") is {RequiredNetworkLevel:2,Unlocked:false} &&
            tiers.Single(x=>x.Id=="species.chef") is {RequiredNetworkLevel:2,Unlocked:false},"one network progression locks the kitchen and chef at level two while keeping basic logistics available");
        check(config.Production.Recipes.OrderBy(x=>x.Code).Select(x=>x.RequiredNetworkLevel).SequenceEqual(new[]{2,3,4,5}),"all five technology stages have concrete progression and four processing unlocks");
        var nest=((QueenObject)world.Objects.Find("queen",1)!).Position;
        var ant=world.Ants[0] with {Species=AntSpeciesIds.Chef,Position=nest,Intent=AntAction.Process,Production=new(1,RecipeCode:1)};
        var kitchen=new BuildingView(1,nest,"workshop") {Phase="ready",Store=new FoodStore().Put(FoodTypes.Fruit,2,[new(2,1)])};
        world=world with {Objects=world.Objects.Put(kitchen),Ants=[ant],NextBuildingId=2,Tick=1};
        var action=new ProductionActionRule();var work=new WorkRule();
        var locked=action.Apply(world);
        check(locked.Ants[0].ActionResult=="technology_locked" && ((BuildingView)locked.Objects.Find("building",1)!).Store.Get(FoodTypes.Fruit)==2,"a legacy ready kitchen cannot bypass the level-two processing requirement");
        var construction=work.Apply(world with {Objects=world.Objects.Put(kitchen with {Phase="blueprint",Store=new FoodStore().Add(FoodTypes.Fiber,12)}),Ants=[ant with {Intent=AntAction.Work}]});
        check(construction.Ants[0].ActionResult=="technology_locked" && ((BuildingView)construction.Objects.Find("building",1)!).ConstructionWork==0,"construction labor cannot bypass technology even when all materials are on site");
        world=action.Apply(world with {NetworkLevel=2});
        var inputs=world;
        var down=work.Apply(world with {NetworkLevel=1,Tick=2,Ants=[world.Ants[0] with {Intent=AntAction.Work,Production=new(1)}]});
        check(down.Ants[0].ActionResult=="technology_locked" && ((BuildingView)down.Objects.Find("building",1)!).WorkInProgress.Total==2,"loaded low-tech worlds preserve WIP without granting free recipe progress");
        for(var tick=0;tick<50;tick++)world=work.Apply(world with {Tick=world.Tick+1,Ants=[world.Ants[0] with {Intent=AntAction.Work,Production=new(1)}]});
        check(world.ProcessingValueAdded==1 && ((BuildingView)world.Objects.Find("building",1)!).Output.FoodValue(config.Foods)==3,"two value-one fruits become one value-three concentrate and count exactly one processing gain");
        var repeated=work.Apply(world with {Tick=world.Tick+1,Ants=[world.Ants[0] with {Intent=AntAction.Work,Production=new(1)}]});
        check(repeated.ProcessingValueAdded==1 && ((BuildingView)repeated.Objects.Find("building",1)!).Output.Total==1,"repeated work on a completed batch cannot repeat output or value rewards");
        var protein=config.Foods.Single(x=>x.Id==FoodTypes.Meal);
        check(protein.Value==4 && protein.HealAmount==12 && config.Foods.Single(x=>x.Id==FoodTypes.Royal).Value==28,"processing produces registered higher development value and stronger healing");
        var mixed=kitchen with {Store=new FoodStore().Put(FoodTypes.Concentrate,1,[new(1,2)]).Put(FoodTypes.Meal,1,[new(1,2)]),Output=new()};
        var mix=action.Apply(world with {NetworkLevel=4,Objects=world.Objects.Put(mixed),Ants=[ant with {Production=new(1,RecipeCode:3),LastInteractionTick=-1000000}]});
        var reserved=(BuildingView)mix.Objects.Find("building",1)!;
        check(reserved.Store.Total==0 && reserved.WorkInProgress.Get(FoodTypes.Concentrate)==1 && reserved.WorkInProgress.Get(FoodTypes.Meal)==1,"mixed recipes reserve every ingredient atomically into one WIP holder");
        var missing=action.Apply(world with {NetworkLevel=4,Objects=world.Objects.Put(mixed with {Store=new FoodStore().Add(FoodTypes.Concentrate,1)}),Ants=[ant with {Production=new(1,RecipeCode:3)}]});
        check(missing.Ants[0].ActionResult=="missing_inputs" && ((BuildingView)missing.Objects.Find("building",1)!).Store.Get(FoodTypes.Concentrate)==1,"one missing ingredient never consumes the ingredients that are available");
        for(var tick=0;tick<70;tick++)mix=work.Apply(mix with {Tick=mix.Tick+1,Ants=[mix.Ants[0] with {Intent=AntAction.Work,Production=new(1)}]});
        var product=(BuildingView)mix.Objects.Find("building",1)!;
        check(product.Output.Get(FoodTypes.Balanced)==1 && ItemCredit.Points(product.Output.Credits[FoodTypes.Balanced])==4 && mix.ProcessingValueAdded-world.ProcessingValueAdded==3,
            "mixed processing conserves all raw admission provenance and grants only its real three-point value gain");
        var top=kitchen with {Store=new FoodStore().Put(FoodTypes.Balanced,2,[new(2,4)]),Output=new()};
        var feast=action.Apply(world with {NetworkLevel=5,Objects=world.Objects.Put(top),Ants=[ant with {Production=new(1,RecipeCode:4)}]});
        for(var tick=0;tick<100;tick++)feast=work.Apply(feast with {Tick=feast.Tick+1,Ants=[feast.Ants[0] with {Intent=AntAction.Work,Production=new(1)}]});
        var feastHolder=(BuildingView)feast.Objects.Find("building",1)!;
        check(feastHolder.Output.Get(FoodTypes.Royal)==1 && ItemCredit.Points(feastHolder.Output.Credits[FoodTypes.Royal])==8 && feast.ProcessingValueAdded-world.ProcessingValueAdded==8,"level-five feast has a genuine final recipe and preserves eight raw points");
        // Currency tests use whole physical portions; all change must remain available exactly once.
        var payment=new FoodStore().Add(FoodTypes.Concentrate,7).PayValue(20,0,config.Foods);
        check(payment.Store.Total==0 && payment.Change==1,"seven concentrate portions pay twenty points and retain one point of change");
        var next=payment.Store.Add(FoodTypes.Fruit,1).PayValue(1,payment.Change,config.Foods);
        check(next.Store.Get(FoodTypes.Fruit)==1 && next.Change==0,"future payments spend existing change before consuming more physical food");
        var wallet=world with {Stock=new FoodStore().Add(FoodTypes.Royal,2),DevelopmentChange=0};
        var paid=TechnologyProgression.Pay(wallet,20);
        check(paid.Inventory==1 && paid.DevelopmentChange==8 && paid.DevelopmentValue==wallet.DevelopmentValue-20 && paid.ConsumedItems.GetValueOrDefault(FoodTypes.Royal)==1,"development value decreases by exact cost while the item ledger records actual consumed portions");
        var reserve=action.Apply(wallet with {Config=config with {MinimumReserve=40},Ants=[ant with {Intent=AntAction.Withdraw,Production=new(0,FoodTypes.Royal,2)}]});
        check(reserve.Ants[0].Carry==0 && reserve.Stock.Get(FoodTypes.Royal)==2,"withdrawal cannot cross a value-based reserve by taking an expensive portion");
        var eater=new EatingRule().Apply(wallet with {Config=config with {MinimumReserve=40},Ants=[ant with {Intent=AntAction.Eat,Health=1,EatFood=FoodTypes.Royal}]});
        check(eater.Ants[0].ActionResult=="insufficient_food" && eater.Stock.Get(FoodTypes.Royal)==2,"nest healing charges the full portion value against the same reserve");
        var perception=new Perception(1,10,nest,nest,0,null,128,128,[]) {TechnologyLevel=2,Recipes=config.Production.Recipes,FoodDefinitions=config.Foods};
        var dsl=AntDslCompiler.Compile("if(technology_level==2 && recipe_unlocked(1) && !recipe_unlocked(2)){memory[0]=food_value(food_concentrate);wait();}").Execute(perception,new(64));
        check(dsl.Error is null && dsl.Memory[0]==3,"ant DSL reads cached technology and public item values with bounded query costs");
        var chefProgram=AntDslCompiler.Compile(DefaultProgramSources.Chef);var peak=0;
        foreach(var level in Enumerable.Range(1,5))foreach(var recipe in config.Production.Recipes) {
            var programResult=chefProgram.Execute(perception with {QueenConnected=true,TechnologyLevel=level,Buildings=[kitchen with {Store=recipe.Ingredients().Aggregate(new FoodStore(),(store,pair)=>store.Add(pair.Key,pair.Value))}],BuildingTypes=config.Production.Buildings,Order=new(1,"kitchen","",nest,nest,nest) {Parameters=ImmutableDictionary<string,int>.Empty.Add("station",1).Add("recipe",recipe.Code)}},new(64));
            peak=Math.Max(peak,programResult.Spent);
            check(programResult.Error is null && programResult.Action==(level>=recipe.RequiredNetworkLevel?AntAction.Process:AntAction.Wait),"chef respects recipe unlocks within the level-one instruction budget");
        }
        check(peak<=64,"all chef technology branches fit the original computation budget (peak "+peak+")");
        var communicating=world with {NetworkLevel=3,Ants=[ant with {Intent=AntAction.Wait,KnownTechnologyLevel=1,RequestQueenInfo=false}]};
        communicating=new QueenCommunicationRule().Apply(communicating);
        check(communicating.Ants[0].KnownTechnologyLevel==1,"network upgrades do not telepathically update the ant's cached technology knowledge");
        communicating=new QueenCommunicationRule().Apply(communicating with {Ants=[communicating.Ants[0] with {RequestQueenInfo=true}]});
        check(communicating.Ants[0].KnownTechnologyLevel==3,"explicit queen request downloads current technology alongside task data");
        foreach(var bad in new[] {
            config with {Production=config.Production with {Recipes=config.Production.Recipes.SetItem(0,config.Production.Recipes[0] with {RequiredNetworkLevel=1})}},
            config with {Production=config.Production with {Recipes=config.Production.Recipes.SetItem(0,config.Production.Recipes[0] with {Input=FoodTypes.Royal})}},
            config with {Production=config.Production with {Recipes=config.Production.Recipes.SetItem(2,config.Production.Recipes[2] with {RequiredNetworkLevel=2})}}
        }) {var rejected=false;try{bad.Validate();}catch(ArgumentException){rejected=true;}check(rejected,"configuration rejects unreachable technology dependencies and circular food processing");}

        var directory=Path.Combine(Directory.GetCurrentDirectory(),"artifacts","technology-checks",Guid.NewGuid().ToString("N"));
        await using var session=new GameSession(config with {InitialStock=100,MinimumReserve=40},44,savesPath:directory);
        async Task<CommandResult> Cmd(CommandKind kind,int value=0,string key="",string species="scout") {
            var s=await session.ObserveAsync();return await session.ExecuteAsync(new(Guid.NewGuid(),s.Revision,kind,value,"technology test",species) {Key=key,ExpectedWorldId=s.WorldId,Location=new(nest.X+3000,nest.Y)});
        }
        var snap=await session.ObserveAsync();
        check(snap.TechnologyLevel==snap.NetworkLevel && snap.DevelopmentValue==100 && !(await Cmd(CommandKind.PlanBuild,key:"workshop")).Accepted,"live actor exposes one technology level and rejects locked kitchen plans");
        await Cmd(CommandKind.ProduceEgg);
        var forbidden=await Cmd(CommandKind.HatchEgg,species:AntSpeciesIds.Chef);
        check(forbidden.Code=="technology_locked" && (await session.ObserveAsync()).Eggs==1,"locked chef hatching retains the existing egg and stock");
        await Cmd(CommandKind.SetMinimumReserve,0);await Cmd(CommandKind.UpgradeNetwork);
        check((await Cmd(CommandKind.HatchEgg,species:AntSpeciesIds.Chef)).Accepted && (await Cmd(CommandKind.PlanBuild,key:"workshop")).Accepted,"network upgrade unlocks the real chef and kitchen commands");
        // A version-one file has no value wallet. Migration changes definitions, never quantities or technology.
        var legacyConfig=config with {Foods=config.Foods.Where(x=>x.Id is not (FoodTypes.Balanced or FoodTypes.Royal)).Select(x=>x with {Value=1}).ToImmutableArray(),
            Production=config.Production with {Buildings=config.Production.Buildings.Select(x=>x with {RequiredNetworkLevel=1}).ToImmutableArray(),Recipes=config.Production.Recipes.Take(2).Select(x=>x with {RequiredNetworkLevel=1}).ToImmutableArray()}};
        var legacy=Simulation.Create(legacyConfig,45) with {Stock=new FoodStore().Add(FoodTypes.Concentrate,2),Objects=world.Objects.Put(kitchen),NextBuildingId=2};
        var catalog=new SpeciesProgramCatalog(config.AntTypes.Select(x=>x.Id));
        SavedGames.Write(directory,"legacy",new(1,12,legacy,catalog.Export()));
        var file=Path.Combine(directory,"legacy.json");var original=await File.ReadAllBytesAsync(file);var migrated=SavedGames.Read(directory,"legacy");var afterMigration=await File.ReadAllBytesAsync(file);
        check(migrated.Format==SavedGames.Format && migrated.Protocol==Protocol.Version && migrated.World.NetworkLevel==1 && migrated.World.Stock.Get(FoodTypes.Concentrate)==2 && migrated.World.StoredFoodValue==6 &&
            migrated.World.Config.Production.Buildings.Single(x=>x.Id=="workshop").RequiredNetworkLevel==2 && original.SequenceEqual(afterMigration),"legacy saves migrate values and gates without erasing items, promoting technology or rewriting the original file");
        var current=migrated with {World=migrated.World with {DevelopmentChange=2,ProcessingValueAdded=7}};
        SavedGames.Write(directory,"current",current);
        var loaded=SavedGames.Read(directory,"current");
        check(loaded.World.DevelopmentChange==2 && loaded.World.ProcessingValueAdded==7 && loaded.World.Config.Production.Recipes.Length==4,"new checkpoints preserve exact change, value statistics and multi-ingredient recipes");
        var before=await session.ObserveAsync();var eval=session.EvaluateSave(new("current",20));
        check(JsonSerializer.Serialize(eval.Baseline)==JsonSerializer.Serialize(eval.Candidate) && (await session.ObserveAsync()).Revision==before.Revision,"migrated technology and value state can be evaluated without changing the live world");
    }
}
