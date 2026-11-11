using System.Collections.Immutable;
using System.Text.Json;
using AntMind.Contracts;
using AntMind.Core;
using AntMind.Programs;
using AntMind.Runtime;

internal static class FreeMovementCheck {
    private static long Squared(Point a,Point b) {var x=(long)a.X-b.X;var y=(long)a.Y-b.Y;return x*x+y*y;}
    public static async Task Run(Action<bool,string> check) {
        var start=Point.FromCells(10,10);
        var target=new Point(start.X+4000,start.Y+3000);
        check(PointMath.Towards(start,target,200)==new Point(start.X+160,start.Y+120),"arbitrary-angle navigation advances both coordinates in the target ratio");
        var rays=new[]{new Point(8000,8000),new Point(8000,3000),new Point(3000,8000),new Point(-8000,3000),new Point(8000,-3000),new Point(-3000,-8000),new Point(0,8000),new Point(8000,0)};
        foreach(var ray in rays) {
            var headingTarget=new Point(start.X+ray.X,start.Y+ray.Y);var next=PointMath.Towards(start,headingTarget,200);
            check(Squared(start,next)<=200*200 && Squared(start,next)>197*197 &&
                (ray.X==0 || Math.Sign(next.X-start.X)==Math.Sign(ray.X)) && (ray.Y==0 || Math.Sign(next.Y-start.Y)==Math.Sign(ray.Y)),"heading changes preserve the same physical speed cap: "+ray);
        }
        var east=PointMath.Towards(new(0,0),new(7000,3000),200);var north=PointMath.Towards(new(0,0),new(3000,7000),200);var west=PointMath.Towards(new(0,0),new(-7000,-3000),200);
        check(east.X==north.Y && east.Y==north.X && east.X==-west.X && east.Y==-west.Y,"integer navigation has no X-first bias under rotation and reflection");
        var position=start;var endpoint=new Point(start.X+14007,start.Y+9003);var ticks=0;var straight=true;
        while(position!=endpoint && ticks++<1000) {
            var next=PointMath.Towards(position,endpoint,200);
            straight &= next.X>=position.X && next.Y>=position.Y && next.X<=endpoint.X && next.Y<=endpoint.Y && next!=position && Squared(position,next)<=40000;
            straight &= Math.Abs((long)(next.X-start.X)*9003-(long)(next.Y-start.Y)*14007)<=14007*50;
            position=next;
        }
        check(straight && position==endpoint && ticks<100,"long diagonal paths reach the exact point without overshoot, stalling or an axis-aligned corner");
        check(PointMath.Towards(new(0,0),new(1,1),1)==new Point(1,0) && PointMath.Towards(new(1,0),new(1,1),1)==new Point(1,1) && PointMath.Towards(start,target,0)==start,"sub-unit direction rounding still makes progress and zero speed remains stopped");
        check(PointMath.Distance(new(0,0),new(3,4))==5 && PointMath.Distance(new(0,0),new(1,1))==2 &&
            PointMath.Distance(new(int.MinValue,int.MinValue),new(int.MaxValue,int.MaxValue))>int.MaxValue,"distance calculations use deterministic integer ceiling and do not overflow at coordinate extremes");
        var config=new WorldConfig(InitialResourceNodes:0);var world=Simulation.Create(config,1701);
        var ant=world.Ants[0] with {Position=start,Target=target,Intent=AntAction.Move};var movement=new MovementRule();
        var moved=movement.Apply(world with {Ants=[ant]});var loaded=movement.Apply(world with {Ants=[ant with {Carry=1}]});
        check(moved.Ants[0].Position==new Point(start.X+160,start.Y+120) && Squared(start,loaded.Ants[0].Position)<Squared(start,moved.Ants[0].Position),"actual ant navigation keeps load slowdown on arbitrary headings");
        var final=movement.Apply(world with {Ants=[ant with {Target=new(start.X+3,start.Y+4)}]});
        check(final.Ants[0].Position==final.Ants[0].Target && final.Ants[0].Intent==AntAction.Wait && final.Ants[0].ActionResult=="arrived","reaching a target snaps exactly and stops without automatic interactions");
        var clamped=movement.Apply(world with {Ants=[ant with {Position=new(0,0),Target=new(-9000,-9000)}]});
        check(clamped.Ants[0].Position==new Point(0,0) && clamped.Ants[0].Target==new Point(0,0) && clamped.Ants[0].ActionResult=="arrived","world-bound clamping keeps the stored destination consistent with arrival");
        check(CombatMath.Move(start,target,200)==moved.Ants[0].Position,"enemy navigation shares the same free-direction geometry as ants");
        var follower=ant with {Intent=AntAction.Follow,FollowAntId=2,FollowDistance=0,Target=null};
        var following=movement.Apply(new FollowRule().Apply(world with {Ants=[follower,ant with {Id=2,Position=new(start.X+2000,start.Y+1000),Intent=AntAction.Wait}]}));
        check(following.Ants[0].Position.X>start.X && following.Ants[0].Position.Y>start.Y,"following another ant also moves diagonally towards its local target");
        var p=new Perception(1,1,start,Point.FromCells(32,32),0,null,64,64,[]);
        var goalProgram=AntDslCompiler.Compile("set_target(18000,16000);");var goal=goalProgram.Execute(p,new(64));
        var legacy=AntDslCompiler.Compile("move_to(18000,16000);").Execute(p,new(64));
        check(goal.Action==AntAction.Move && goal.Target==new Point(18000,16000) && goal.Error is null && goal.Spent==legacy.Spent && goal.Target==legacy.Target,"set_target assigns one persistent destination and move_to remains budget-compatible");
        var setOnce=AntDslCompiler.Compile("if (!has_target) { set_target(18000,16000); }");
        var programRule=new ProgramRule(setOnce);var travelling=world with {Ants=[ant with {Target=null,Intent=AntAction.Wait,NextDecisionTick=0}]};
        for(var tick=1;tick<=100;tick++)travelling=movement.Apply(programRule.Apply(travelling with {Tick=tick}));
        check(travelling.Ants[0].Position==goal.Target && travelling.Ants[0].Intent==AntAction.Wait && travelling.Ants[0].LastError is null,"a program can set its destination once and the engine travels across later decision cycles until arrival");
        var query=AntDslCompiler.Compile("memory[0]=has_target; memory[1]=target_x; memory[2]=target_y; memory[3]=target_distance; memory[4]=target_reached;");
        var queryResult=query.Execute(p with {CurrentTarget=new(start.X+3000,start.Y+4000)},new(64));
        var arrived=query.Execute(p with {CurrentTarget=start},new(64));
        check(queryResult.Memory.Take(5).SequenceEqual(new[]{1,start.X+3000,start.Y+4000,5000,0}) && arrived.Memory[4]==1,"programs can inspect the destination, true radial distance and exact arrival");
        var absent=query.Execute(p,new(64));
        check(absent.Memory[0]==0 && absent.Memory[3]==int.MaxValue && absent.Memory[4]==0,"missing targets have an explicit flag and never report false arrival");
        var stopped=AntDslCompiler.Compile("wait();").Execute(p with {CurrentTarget=target,CurrentAction=AntAction.Move},new(64));
        var rerouted=AntDslCompiler.Compile("set_target(0,0);").Execute(p with {CurrentTarget=target,CurrentAction=AntAction.Move},new(64));
        var reroutedWorld=movement.Apply(world with {Ants=[ant with {Target=rerouted.Target,Intent=rerouted.Action}]});
        check(stopped.Target is null && stopped.Action==AntAction.Wait && reroutedWorld.Ants[0].Position.X<start.X && reroutedWorld.Ants[0].Position.Y<start.Y,"explicit wait cancels travel and a new destination immediately changes direction");
        var bounds=AntDslCompiler.Compile("set_target(-100,2147483647);").Execute(p,new(64));
        check(bounds.Target==new Point(0,63000),"DSL destinations use fixed-point map bounds without snapping to whole cells");
        var badArguments=false;try{AntDslCompiler.Compile("set_target(1);");}catch(FormatException){badArguments=true;}
        check(badArguments,"new destination instructions validate their argument count before activation");

        var directory=Path.Combine(Directory.GetCurrentDirectory(),"artifacts","free-movement-saves",Guid.NewGuid().ToString("N"));
        await using var session=new GameSession(config,1702,savesPath:directory);
        async Task Cmd(CommandKind kind,string key="",string source="") {
            var s=await session.ObserveAsync();var r=await session.ExecuteAsync(new(Guid.NewGuid(),s.Revision,kind,Reason:"free-direction regression",ProgramSource:source) {ExpectedWorldId=s.WorldId,Key=key});
            if(!r.Accepted)throw new Exception(r.Code);
        }
        await Cmd(CommandKind.ApplyProgram,source:"if (!has_target) {set_target(12000,17000);}");
        for(var i=0;i<17;i++)await Cmd(CommandKind.Step);await Cmd(CommandKind.SaveGame,"midflight");
        var before=await session.ObserveAsync();for(var i=0;i<25;i++)await Cmd(CommandKind.Step);var branchA=await session.ObserveAsync();
        await Cmd(CommandKind.LoadGame,"midflight");var restored=await session.ObserveAsync();for(var i=0;i<25;i++)await Cmd(CommandKind.Step);var branchB=await session.ObserveAsync();
        check(restored.Paused && restored.WorldId!=before.WorldId && restored.Ants[0].Target==before.Ants[0].Target && restored.Ants[0].Position==before.Ants[0].Position,"midflight saves restore the precise point and active goal without resetting movement");
        check(JsonSerializer.Serialize(branchA.Ants)==JsonSerializer.Serialize(branchB.Ants) && branchA.Tick==branchB.Tick,"resuming the same fixed-point navigation checkpoint reproduces positions, decisions and memory exactly");
    }
}
