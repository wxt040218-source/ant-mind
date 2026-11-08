using System.Collections.Immutable;
using AntMind.Contracts;
using AntMind.Core;
using AntMind.Programs;
using AntMind.Runtime;

internal static class ColonyCheck {
    public static async Task Run(Action<bool,string> check) {
        var config=new WorldConfig(InitialResourceNodes:0,MinimumReserve:0);
        var nest=Point.FromCells(32,32);
        var type=AntSpeciesIds.Hauler;
        var ant=new AntView(1,nest,0,Species:type) { Health=5,MaxHealth=12 };
        var food=new ResourceView(1,nest,3,0,3) { FoodType=FoodTypes.Meat };
        var world=Simulation.Create(config,42) with { Ants=[ant],Resources=[food],HaulerCarryLevel=10,NextResourceId=2 };
        var interaction=new InteractionRule();
        world=interaction.Apply(world with { Tick=1,Ants=[ant with { Intent=AntAction.Pickup }] });
        check(world.Ants[0].Carry==3 && world.Ants[0].CarryFood==FoodTypes.Meat,"typed meat pickup preserves cargo type");
        world=interaction.Apply(world with { Tick=11,Ants=[world.Ants[0] with { Intent=AntAction.Drop }] });
        check(world.Resources.All(f=>f.FoodType==FoodTypes.Meat) && world.Resources.Sum(f=>f.Amount)==3,"dropping meat never creates fruit");
        world=interaction.Apply(world with { Tick=21,Ants=[world.Ants[0] with { Intent=AntAction.Pickup }] });
        world=interaction.Apply(world with { Tick=31,Ants=[world.Ants[0] with { Intent=AntAction.Deliver }] });
        check(world.Stock.Get(FoodTypes.Meat)==3 && world.Stock.Get(FoodTypes.Fruit)==100 && world.Inventory==103 && world.Delivered==3,
            "typed delivery credits meat and derived inventory without rewriting fruit");
        var mixed=interaction.Apply(world with { Tick=50,Ants=[ant with { Carry=1,CarryFood=FoodTypes.Meat,Intent=AntAction.Pickup }],
            Resources=[food with { FoodType=FoodTypes.Fruit }] });
        check(mixed.Ants[0].Carry==1 && mixed.Resources[0].Amount==3,"one cargo load cannot silently mix food types");
        var spent=world.Stock.Spend(101);
        check(spent.Get(FoodTypes.Fruit)==0 && spent.Get(FoodTypes.Meat)==2 && spent.Total==2,"development spending uses fruit before meat");
        var eat=new EatingRule();
        var start=eat.Apply(world with { Tick=40,Ants=[ant with { Intent=AntAction.Eat,EatFood=FoodTypes.Meat }] });
        check(start.Ants[0].HealUntilTick==80 && start.Ants[0].Health==5 && start.Stock.Get(FoodTypes.Meat)==3,"eating channels without charging or healing early");
        var healed=eat.Apply(start with { Tick=80 });
        check(healed.Ants[0].Health==11 && healed.Stock.Get(FoodTypes.Meat)==2 && healed.FoodEaten==1 && healed.HealthRestored==6,
            "meat heals six and consumes exactly one portion at completion");
        var full=eat.Apply(healed with { Tick=90,Ants=[ant with { Health=12,Intent=AntAction.Eat }] });
        check(full.FoodEaten==1 && full.Ants[0].ActionResult=="already_healthy","full health does not consume food");
        var carried=eat.Apply(world with { Tick=1,Ants=[ant with { Position=Point.FromCells(2,2),Carry=1,CarryFood=FoodTypes.Meat,Intent=AntAction.Eat,EatFood=FoodTypes.Meat }] });
        carried=eat.Apply(carried with { Tick=41 });
        check(carried.Ants[0].Carry==0 && carried.Ants[0].Health==11 && carried.Inventory==103,"field healing consumes carried meat instead of remote nest stock");
        var reserve=eat.Apply(world with { Config=config with { MinimumReserve=103 },Tick=1,Ants=[ant with { Intent=AntAction.Eat,EatFood=FoodTypes.Meat }] });
        check(reserve.Ants[0].ActionResult=="insufficient_food" && reserve.Inventory==103,"nest healing respects minimum reserve");
        var interrupted=start with { Objects=start.Objects.Put(new EnemyView(1,Point.FromCells(35,32),18,18,1)),
            Ants=[start.Ants[0] with { Position=Point.FromCells(36,32) }] };
        interrupted=new CombatRule().Apply(interrupted with { Tick=41 });
        check(interrupted.Ants[0].HealUntilTick==0 && interrupted.Ants[0].ActionResult=="eating_interrupted" && interrupted.FoodEaten==0,
            "enemy damage interrupts eating without consuming food");
        var simultaneous=Simulation.Create(config,1) with { Stock=FoodStore.Fruit(1),Tick=1,
            Ants=[ant with { Intent=AntAction.Eat },ant with { Id=2,Intent=AntAction.Eat }] };
        simultaneous=eat.Apply(simultaneous);simultaneous=eat.Apply(simultaneous with { Tick=41 });
        check(simultaneous.FoodEaten==1 && simultaneous.Inventory==0 && simultaneous.Ants.Count(a=>a.Health==8)==1,
            "concurrent eaters cannot consume the same last portion twice");
        var killed=Simulation.Create(config,1) with { Tick=1,Ants=[ant with { Position=Point.FromCells(10,10),Health=1,Carry=2,CarryFood=FoodTypes.Meat }] };
        killed=killed with { Objects=killed.Objects.Put(new EnemyView(1,Point.FromCells(11,10),18,18,1)) };
        killed=new CombatRule().Apply(killed);
        check(killed.Resources.Sum(f=>f.Amount)==2 && killed.Resources.All(f=>f.FoodType==FoodTypes.Meat) && killed.Generated==0,
            "death preserves typed cargo without generating replacement food");
        var group=new GroupOrder(1,"狩猎","收肉",nest,Point.FromCells(40,32),nest);
        var communication=new QueenCommunicationRule();
        var ordered=communication.Apply(Simulation.Create(config,1) with { Tick=1,Groups=[group],
            Ants=[ant with { GroupId=1,RequestQueenInfo=true }] });
        check(ordered.Ants[0].ReceivedOrder==group,"explicit in-range queen request downloads group order");
        ordered=communication.Apply(ordered with { Tick=20,Groups=[group with { Objective=Point.FromCells(50,50),Version=2 }],
            Ants=[ordered.Ants[0] with { Position=Point.FromCells(0,0),RequestQueenInfo=true }] });
        check(ordered.Ants[0].ReceivedOrder?.Version==1 && ordered.Ants[0].QueenRequestStatus==ActionStatus.Failed,
            "out-of-range group retains old order and cannot see new objective");
        var claims=new ClaimRule();
        var claimed=claims.Apply(Simulation.Create(config,1) with { Tick=1,Resources=[food],
            Ants=[ant with { ClaimFoodId=1,ClaimQuantity=2,ClaimDuration=10 },ant with { Id=2,ClaimFoodId=1,ClaimQuantity=2,ClaimDuration=10 }] });
        check(claimed.Claims.Length==2 && claimed.Claims.Sum(c=>c.Quantity)==3 && claimed.Claims[0].Quantity==2,
            "stable resource claims allocate remaining quantity without overbooking");
        check(claims.Apply(claimed with { Tick=11 }).Claims.IsEmpty,"resource claims expire at exact deadline");
        var hidden=claims.Apply(claimed with { Ants=[ant with { Position=Point.FromCells(0,0),ClaimFoodId=1,ClaimQuantity=1,ClaimDuration=10 }] });
        check(hidden.Ants[0].ActionResult=="food_not_visible","claims do not reveal remote food");
        var follow=new FollowRule().Apply(Simulation.Create(config,1) with { Ants=[ant with { Intent=AntAction.Follow,FollowAntId=2,FollowDistance=1000 },ant with { Id=2,Position=Point.FromCells(34,32) }] });
        check(follow.Ants[0].Target==Point.FromCells(33,32),"follow keeps requested distance using only visible ally position");
        var gone=new FollowRule().Apply(follow with { Ants=[follow.Ants[0] with { Intent=AntAction.Follow },follow.Ants[1] with { Position=Point.FromCells(0,0) }] });
        check(gone.Ants[0].ActionResult=="ally_not_visible" && gone.Ants[0].Target is null,"following fails instead of tracking unseen ally");
        var cleaned=new ClaimCleanupRule().Apply(claimed with { Ants=[ant],Resources=[food with { Amount=1 }] });
        check(cleaned.Claims.Length==1 && cleaned.Claims[0].Quantity==1,"post-interaction cleanup removes dead owners and clips claims to remaining food");
        var perception=new Perception(1,1,nest,nest,1,null,64,64,[food]) { CarryFood=FoodTypes.Meat,Health=5,Order=group,GroupId=1,
            NearbyAllies=[new(2,type,1,Point.FromCells(33,32),12,12)] };
        var decision=AntDslCompiler.Compile("claim_food(nearest_id, 1, 100); eat(food_meat);").Execute(perception,new InstructionBudget(64));
        check(decision.Error is null && decision.ClaimFoodId==1 && decision.Action==AntAction.Eat && decision.EatFood==FoodTypes.Meat,"DSL produces typed eating and explicit claim intents");
        decision=AntDslCompiler.Compile("follow(nearest_group_member_id, 1000);").Execute(perception,new InstructionBudget(64));
        check(decision.Error is null && decision.FollowAntId==2 && decision.Action==AntAction.Follow,"DSL resolves local mixed-species group member");
        var commonHauler=AntDslCompiler.Compile(DefaultProgramSources.Hauler);
        var fruitFood=food with { Id=1,FoodType=FoodTypes.Fruit };
        var meatFood=food with { Id=2 };
        var meatOrder=group with { FoodFilter=FoodTypes.Meat,RadiusCells=3,Objective=nest };
        var groupSense=perception with { Carry=0,Health=12,Nearby=[fruitFood,meatFood],Order=meatOrder };
        var meatDecision=commonHauler.Execute(groupSense,new InstructionBudget(64));
        var fruitDecision=commonHauler.Execute(groupSense with { GroupId=2,Order=meatOrder with { Id=2,FoodFilter=FoodTypes.Fruit } },new InstructionBudget(64));
        check(meatDecision.Error is null && fruitDecision.Error is null && meatDecision.PickupFoodId==2 && fruitDecision.PickupFoodId==1,
            "one shared hauler program collects meat or fruit based only on cached group parameters");
        var exactPickup=interaction.Apply(Simulation.Create(config,1) with { Tick=1,Resources=[fruitFood,meatFood],
            Ants=[ant with { Intent=AntAction.Pickup,PickupFoodId=2 }] });
        check(exactPickup.Ants[0].CarryFood==FoodTypes.Meat && exactPickup.Resources.Single(f=>f.Id==1).Amount==3,
            "explicit targeted pickup cannot take overlapping fruit instead of requested meat");
        var distantMeat=meatFood with { Id=3,Position=Point.FromCells(34,32) };
        var areaDecision=commonHauler.Execute(groupSense with { Nearby=[meatFood,distantMeat],Order=meatOrder with { Objective=distantMeat.Position,RadiusCells=1 } },new InstructionBudget(64));
        check(areaDecision.Error is null && areaDecision.Target==distantMeat.Position,
            "shared program selects matching food within its group work area");
        var custom=meatOrder with { Parameters=ImmutableDictionary<string,int>.Empty.Add("priority",17),
            Points=ImmutableDictionary<string,Point>.Empty.Add("supply",Point.FromCells(39,30)) };
        var customCode=AntDslCompiler.Compile("memory[0]=param(\"priority\"); memory[1]=group_x(\"supply\"); memory[2]=has_point(\"supply\");");
        var customResult=customCode.Execute(groupSense with { Order=custom },new InstructionBudget(64));
        var noReply=customCode.Execute(groupSense with { Order=null },new InstructionBudget(64));
        check(customResult.Error is null && customResult.Memory[0]==17 && customResult.Memory[1]==39000 && customResult.Memory[2]==1 &&
            noReply.Memory.Take(3).All(v=>v==0),"Agent-defined parameter and named-point queries only read the received cache");
        foreach(var sample in Directory.GetFiles("content/programs/groups","*.ant")) {
            var result=AntDslCompiler.Compile(File.ReadAllText(sample)).Execute(perception,new InstructionBudget(64));
            check(result.Error is null,"group example executes within level-one budget: "+Path.GetFileName(sample));
        }
        // Complete real rule pipeline: explicit kill -> typed hauling -> nest eating -> conservation.
        var programs=new Dictionary<string,IAntProgram> {
            [AntSpeciesIds.Soldier]=AntDslCompiler.Compile("if (nearest_enemy_id > 0) { attack(nearest_enemy_id); } else { wait(); }"),
            [AntSpeciesIds.Hauler]=AntDslCompiler.Compile("if (health < max_health && nest_meat > 0) { eat(food_meat); } else if (carry > 0) { if (at_nest) { deliver(); } else { move_to(nest_x,nest_y); } } else if (nearest_amount > 0) { if (nearest_distance <= 1000) { pickup(); } else { move_to(nearest_x,nearest_y); } } else { wait(); }")
        };
        var pipeline=new Simulation([new ProgramRule(programs),new MovementRule(),new CombatRule(),new InteractionRule(),new EatingRule(),new ResourceDiscoveryRule()]);
        var chain=Simulation.Create(config with { InitialStock=0 },1) with { Ants=[ant,new AntView(2,Point.FromCells(34,32),0,Species:AntSpeciesIds.Soldier) { Health=40,MaxHealth=40 }] };
        chain=chain with { Objects=chain.Objects.Put(new EnemyView(1,Point.FromCells(35,32),6,18,3)) };
        for(var i=0;i<1000;i++) chain=pipeline.Step(chain);
        check(chain.EnemiesDefeated==1 && chain.Delivered==3 && chain.FoodEaten==2 && chain.HealthRestored==7 && chain.Ants[0].Health==12 &&
            chain.Inventory==1 && chain.Stock.Get(FoodTypes.Meat)==1 && chain.Ants.All(a=>a.LastError is null),
            "full pipeline kills enemy, transports meat and explicitly heals wounded hauler to full");
        check(chain.Config.InitialStock+chain.Generated==chain.Inventory+chain.Resources.Sum(f=>f.Amount)+chain.Ants.Sum(a=>a.Carry)+chain.FoodEaten,
            "food conservation includes healing consumption across complete combat and hauling pipeline");
        await RuntimeChecks(config,check);
    }
    private static async Task RuntimeChecks(WorldConfig config,Action<bool,string> check) {
        await using var session=new GameSession(config,1);
        async Task<CommandResult> Send(CommandKind kind,int group=0,int ant=0,string source="",int value=0,string species=AntSpeciesIds.Scout)=>
            await session.ExecuteAsync(new(Guid.NewGuid(),(await session.ObserveAsync()).Revision,kind,value,"colony check",species,source) {
                GroupId=group,AntId=ant,Name="测试组",Task="采集",Objective=Point.FromCells(40,32) });
        check((await Send(CommandKind.CreateGroup)).Accepted,"actor creates mixed-species group");
        await Send(CommandKind.CreateGroup,1);
        check((await session.ObserveAsync()).Groups.Length==2,"creating while another group is selected creates a distinct stable ID");
        await Send(CommandKind.DeleteGroup,2);
        check((await Send(CommandKind.AssignAnt,1,1)).Accepted && (await Send(CommandKind.AssignAnt,1,6)).Accepted,"group accepts scout and hauler members");
        var invalid=await Send(CommandKind.AssignAnt,99,1);
        check(!invalid.Accepted && invalid.Code=="group_not_found","nonexistent group cannot absorb members");
        check(!(await Send(CommandKind.ApplyProgram,1,source:"wait();")).Accepted,"group-specific code is rejected; only species programs exist");
        await Send(CommandKind.ApplyProgram,source:"memory[0]=7; wait();");await Send(CommandKind.Step);
        var view=await session.ObserveAsync();
        check(view.Programs.Length==3 && view.Programs.All(p=>p.GroupId==0) && view.Ants.Where(a=>a.Species==AntSpeciesIds.Scout).All(a=>a.Memory[0]==7 && a.ProgramVersion==1),
            "all scouts share one code version across grouped and ungrouped members");
        var revision=view.Revision;
        var parameter=new GameCommand(Guid.NewGuid(),revision,CommandKind.SetGroupParameter,11,"custom strategy parameter") {GroupId=1,Key="priority"};
        check((await session.ExecuteAsync(parameter)).Accepted,"Agent sets arbitrary named integer parameter");
        await Send(CommandKind.ApplyProgram,source:"memory[1]=param(\"priority\"); request_queen_info(); wait();");
        for(var i=0;i<12;i++)await Send(CommandKind.Step);
        view=await session.ObserveAsync();
        check(view.Ants.Single(a=>a.Id==1).Memory[1]==11 && view.Ants.Single(a=>a.Id==2).Memory[1]==0,
            "same species code interprets different cached group parameters without independent programs");
        var pointCommand=new GameCommand(Guid.NewGuid(),view.Revision,CommandKind.SetGroupPoint,Reason:"custom rendezvous") {GroupId=1,Key="supply",Location=Point.FromCells(38,30)};
        check((await session.ExecuteAsync(pointCommand)).Accepted,"Agent stores arbitrary named work points");
        var batch=new GameCommand(Guid.NewGuid(),(await session.ObserveAsync()).Revision,CommandKind.AssignAnts,Reason:"batch assignment") {GroupId=1,AntIds=[2,7]};
        var batchResult=await session.ExecuteAsync(batch);
        check(batchResult.Accepted && await session.ExecuteAsync(batch with {AntIds=[2,7]})==batchResult,"batch member assignment supports structurally identical request retries");
        view=await session.ObserveAsync();
        var failed=await session.ExecuteAsync(batch with {RequestId=Guid.NewGuid(),ExpectedRevision=view.Revision,AntIds=[3,999]});
        check(!failed.Accepted && (await session.ObserveAsync()).Ants.Single(a=>a.Id==3).GroupId==0,"invalid batch assignment is atomic");
        check(!(await Send(CommandKind.RollbackProgram,1,value:0)).Accepted,"group-specific rollback cannot bypass shared species programs");
        await Send(CommandKind.RollbackProgram,value:0);await Send(CommandKind.Step);
        check((await session.ObserveAsync()).Programs.Single(p=>p.Species==AntSpeciesIds.Scout).ActiveVersion==3,"species rollback still affects every group");
        await Send(CommandKind.DeleteGroup,1);
        view=await session.ObserveAsync();
        check(view.Groups.IsEmpty && view.Ants.All(a=>a.GroupId==0 && a.ReceivedOrder is null),"disbanding releases all members and cached orders");
    }
}
