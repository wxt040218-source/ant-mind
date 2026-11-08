using System.Collections.Immutable;
using AntMind.Contracts;
using AntMind.Core;
using AntMind.Programs;
using AntMind.Runtime;

internal static class CombatCheck {
    public static void Run(Action<bool,string> check) {
        var combat=new CombatRule();
        var config=new WorldConfig(InitialResourceNodes:0);
        var soldier=new AntView(1,Point.FromCells(10,10),0,Intent:AntAction.Attack,Species:AntSpeciesIds.Soldier) {
            Health=40,MaxHealth=40,AttackTargetId=1
        };
        var world=Simulation.Create(config,42) with { Tick=1,Ants=[soldier] };
        world=world with { Objects=world.Objects.Put(new EnemyView(1,Point.FromCells(11,10),6,18,5)) };
        var killed=combat.Apply(world);
        var dead=(EnemyView)killed.Objects.Find("enemy",1)!;
        check(dead.Health==0 && dead.CorpseFoodId==1 && killed.EnemiesDefeated==1 && killed.Resources.Single() is { Origin:ResourceOrigin.Carcass,FoodType:FoodTypes.Meat,Amount:5,MaxCapacity:5 },
            "explicit soldier attack defeats an enemy and creates an independent food carcass");
        check(killed.Inventory==100 && killed.Delivered==0 && killed.Generated==5,
            "enemy death does not credit inventory before the carcass is transported");
        var repeated=combat.Apply(killed with { Tick=11,Ants=[killed.Ants[0] with { Intent=AntAction.Attack }] });
        check(repeated.Generated==5 && repeated.EnemiesDefeated==1 && repeated.Resources.Length==1,
            "dead enemies cannot generate multiple corpses or repeated food");
        var collector=new AntView(2,Point.FromCells(11,10),0,Intent:AntAction.Pickup,Species:AntSpeciesIds.Hauler);
        var pickup=new InteractionRule().Apply(killed with { HaulerCarryLevel=5,Ants=killed.Ants.Add(collector) });
        check(pickup.Ants.Single(x=>x.Id==2).Carry==5 && pickup.Resources.IsEmpty,"haulers can explicitly pick up defeated-enemy food");
        var delivered=new InteractionRule().Apply(pickup with { Tick=12,Ants=pickup.Ants.Select(x=>x.Id==2 ? x with {
            Position=Point.FromCells(32,32),Intent=AntAction.Deliver } : x).ToImmutableArray() });
        check(delivered.Inventory==105 && delivered.Delivered==5 && delivered.Generated==delivered.Delivered+delivered.Resources.Sum(x=>x.Amount)+delivered.Ants.Sum(x=>x.Carry),
            "carcass pickup and delivery enter the existing inventory and conservation rules");
        var worker=new AntView(1,Point.FromCells(10,10),3) { Health=2 };
        var attacked=Simulation.Create(config,42) with { Tick=1,Ants=[worker],Generated=3 };
        attacked=attacked with { Objects=attacked.Objects.Put(new EnemyView(1,Point.FromCells(11,10),18,18,2)) };
        var lost=combat.Apply(attacked);
        check(lost.Ants.IsEmpty && lost.AntsLost==1 && ((AntView)lost.Objects.Find("ant",1)!).Health==0,
            "enemies damage and kill ants while preserving their independent records");
        check(lost.Resources.Sum(x=>x.Amount)==3 && lost.Generated==3 && ((AntView)lost.Objects.Find("ant",1)!).Carry==0,
            "killed ants drop existing cargo without inventing or destroying food");
        var durable=world with { Objects=world.Objects.Put(new EnemyView(1,Point.FromCells(11,10),18,18,2)) };
        var first=combat.Apply(durable);
        var cooldown=combat.Apply(first with { Tick=2,Ants=[first.Ants[0] with { Intent=AntAction.Attack }] });
        check(((EnemyView)cooldown.Objects.Find("enemy",1)!).Health==12 && cooldown.Ants[0].ActionResult=="attack_cooldown",
            "repeated attack calls cannot bypass physical attack cooldown");
        var distant=combat.Apply(world with { Objects=world.Objects.Put(new EnemyView(1,Point.FromCells(20,20),18,18,2)) });
        check(((EnemyView)distant.Objects.Find("enemy",1)!).Health==18 && distant.Ants[0].ActionResult=="enemy_out_of_reach",
            "explicit attacks validate real distance rather than remotely damaging a target");
        var passive=combat.Apply(durable with { Ants=[soldier with { Intent=AntAction.Wait }] });
        check(((EnemyView)passive.Objects.Find("enemy",1)!).Health==18,"soldiers do not attack without a program-issued action");
        var guard=AntDslCompiler.Compile(DefaultProgramSources.Soldier);
        var sense=new Perception(1,1,soldier.Position,Point.FromCells(32,32),0,null,64,64,[],AntSpeciesIds.Soldier,1,8) {
            Health=40,MaxHealth=40,NearbyEnemies=[new EnemyView(1,Point.FromCells(11,10),18,18,2)]
        };
        var action=guard.Execute(sense,new InstructionBudget(64));
        check(action.Action==AntAction.Attack && action.AttackTargetId==1 && action.Error is null,
            "soldier DSL senses local enemies and emits a budgeted explicit attack");
        var follow=guard.Execute(sense with { NearbyEnemies=[new EnemyView(2,Point.FromCells(15,10),18,18,2)] },new InstructionBudget(64));
        check(follow.Action==AntAction.Move && follow.Target==Point.FromCells(15,10),"soldier program approaches distant visible enemies");
        var spawnConfig=config with { Combat=new() { Enabled=true,SpawnIntervalTicks=1,EnemyCap=1 } };
        var spawnStart=Simulation.Create(spawnConfig,42) with { Tick=1 };
        var spawn=new EnemySpawnRule().Apply(spawnStart);
        var same=new EnemySpawnRule().Apply(spawnStart);
        check(spawn.Enemies.SequenceEqual(same.Enemies) && spawn.RandomState==same.RandomState && spawn.Enemies[0].FoodAmount is >=1 and <=5,
            "enemy spawning and corpse yield use the deterministic world random source");
        check(new EnemySpawnRule().Apply(spawn with { Tick=2 }).Enemies.Length==1,"enemy spawning respects the active population cap");
        var queen=(QueenObject)world.Objects.Find("queen",1)!;
        var threatened=world with { Ants=[],Objects=world.Objects.Put(queen with { Health=2 }).Put(new EnemyView(2,queen.Position,18,18,2)) };
        var defeat=combat.Apply(threatened);
        check(defeat.IsDefeated && ((QueenObject)defeat.Objects.Find("queen",1)!).Health==0,
            "enemies in range attack the queen and can defeat the colony");
        check(new Simulation([combat]).Step(defeat).Tick==defeat.Tick,"a defeated world cannot continue simulating combat or harvesting");
        var denseEnemies=Enumerable.Range(1,3).Select(id=>new EnemyView(id,Point.FromCells(11+id,10),18,18,3)).ToImmutableArray();
        var dense=guard.Execute(sense with { NearbyEnemies=denseEnemies },new InstructionBudget(64));
        check(dense.Error is null && dense.Spent<=64,"guard program stays within level-one budget at the configured enemy cap");
        var integrated=Simulation.Create(config with { ResourceRefreshTicks=int.MaxValue },42);
        integrated=integrated with { Ants=integrated.Ants.Add(soldier with { Id=11,Position=Point.FromCells(34,32),Intent=AntAction.Wait }),
            Objects=integrated.Objects.Put(new EnemyView(1,Point.FromCells(40,32),18,18,3)) };
        // Add the guard after replacing the object database, preserving its canonical record.
        integrated=integrated with { Ants=integrated.Ants.Add(soldier with { Id=11,Position=Point.FromCells(34,32),Intent=AntAction.Wait }) };
        var completePipeline=new Simulation([new LocalObservationRule(),new ProgramRule(new DefaultForager()),new QueenCommunicationRule(),
            new MovementRule(),new EnemyMovementRule(),new CombatRule(),new InteractionRule(),new ResourceDiscoveryRule()]);
        for(var tick=0;tick<2000;tick++) {
            integrated=completePipeline.Step(integrated);
            if(integrated.Ants.Any(x=>x.LastError is not null)) throw new Exception("Integrated combat program error: "+integrated.Tick+" "+string.Join("; ",integrated.Ants.Where(a=>a.LastError is not null).Select(a=>$"{a.Id}/{a.Species}/{a.LastError}/{a.LastInstructionSpent}/{a.LastDecisionBranch}")));
        }
        check(integrated.EnemiesDefeated==1 && integrated.Generated==3 && integrated.Delivered>0 && !integrated.IsDefeated &&
            integrated.Generated==integrated.Delivered+integrated.Resources.Sum(x=>x.Amount)+integrated.Ants.Sum(x=>x.Carry),
            "default soldier chases and defeats an enemy, then default haulers deliver its carcass through the full pipeline");
        CheckRuntime(check).GetAwaiter().GetResult();
    }
    private static async Task CheckRuntime(Action<bool,string> check) {
        await using(var session=new GameSession(new WorldConfig(InitialResourceNodes:0),42,[new CombatFixture(false)])) {
            var initial=await session.ObserveAsync();
            await session.ExecuteAsync(new(Guid.NewGuid(),initial.Revision,CommandKind.Step));
            var hidden=await session.ObserveAsync(true);
            check(hidden.Enemies.IsEmpty && (await Gateway.DispatchAsync(session,new(Protocol.Version,"inspect_object",ObjectKind:"enemy",ObjectId:1),default)).Error=="object_not_found",
                "undiscovered enemies are hidden from player and Agent object queries");
            var egg=await session.ExecuteAsync(new(Guid.NewGuid(),hidden.Revision,CommandKind.ProduceEgg,Reason:"test soldier"));
            await session.ExecuteAsync(new(Guid.NewGuid(),egg.Revision,CommandKind.HatchEgg,Reason:"hatch soldier",Species:AntSpeciesIds.Soldier));
            var spawned=await session.ObserveAsync();
            check(spawned.Ants.Any(x=>x.Id==11 && x.Species==AntSpeciesIds.Soldier && x.Health==40) && spawned.DeadAnts.Any(x=>x.Id==10),
                "generic eggs hatch healthy soldiers and dead ant IDs are never reused");
        }
        await using(var doomed=new GameSession(new WorldConfig(InitialResourceNodes:0) { Combat=new() { QueenHealth=2 } },42,[new CombatFixture(true)])) {
            var initial=await doomed.ObserveAsync();
            await doomed.ExecuteAsync(new(Guid.NewGuid(),initial.Revision,CommandKind.Step));
            var end=await doomed.ObserveAsync();
            check(end.Defeated && end.Paused && end.Queen?.Health==0 &&
                !(await doomed.ExecuteAsync(new(Guid.NewGuid(),end.Revision,CommandKind.Resume))).Accepted,
                "queen defeat pauses the runtime and rejects attempts to resume the ended colony");
        }
    }
    private sealed class CombatFixture(bool queenThreat) : IWorldRule {
        public string Id=>"test.combat-fixture"; public int Order=>1;
        public WorldState Apply(WorldState world) {
            if(world.Tick!=1) return world;
            var database=world.Objects.Put(new EnemyView(1,queenThreat ? Point.FromCells(32,32) : Point.FromCells(63,63),18,18,2));
            if(!queenThreat && database.Find("ant",10) is AntView ant) database=database.Put(ant with { Health=0 });
            return world with { Objects=database };
        }
    }
}
