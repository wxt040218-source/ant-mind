using System.Collections.Immutable;
using System.Text.Json;
using AntMind.Contracts;
using AntMind.Core;
using AntMind.Runtime;

internal static class PriorityFixCheck {
    public static async Task Run(Action<bool,string> check) {
        var config=ContentLoader.LoadDefault() with {Combat=new(),InitialResourceNodes=0};
        var directory=Path.Combine(Directory.GetCurrentDirectory(),"artifacts","priority-fix-checks",Guid.NewGuid().ToString("N"));
        var nest=Point.FromCells(config.Width/2,config.Height/2);
        var action=new ProductionActionRule();
        var world=Simulation.Create(config with {MinimumReserve=0},18001);
        world=world with {FirstAdmissionPoints=48,Ants=[world.Ants.First(a=>a.Species=="hauler") with {Position=nest,Carry=1,CarryFood=FoodTypes.Concentrate,CargoTracked=true,CargoCredits=[new(1,2)],Intent=AntAction.Deposit,Production=new(0,FoodTypes.Concentrate,1),LastInteractionTick=-1000000}]};
        world=new MilestoneRule().Apply(action.Apply(world));
        check(world.Delivered==0 && world.FirstAdmissionPoints==50 && MilestoneRule.Snapshot(world).Single(m=>m.Id=="deliver-50").Status=="completed","nest deposit advances standard supply goals using raw admission points, including processed provenance");
        world=action.Apply(world with {Tick=20,Ants=[world.Ants[0] with {Intent=AntAction.Withdraw,Production=new(0,FoodTypes.Concentrate,1)}]});
        world=new MilestoneRule().Apply(action.Apply(world with {Tick=40,Ants=[world.Ants[0] with {Intent=AntAction.Deposit}]}));
        check(world.FirstAdmissionPoints==50 && world.Stock.Get(FoodTypes.Concentrate)==1 && MilestoneRule.Current(world,"first_admission_points")==50,"withdrawal and redeposit do not farm supply-goal progress");
        var oldGoals=MilestoneRule.Defaults.Select(d=>d.Id=="deliver-50"?d with {Metric="delivered",Description="累计送回 50 份食物"}:d.Id=="supply-sprint"?d with {Metric="delivered",Description="解锁后 10 分钟内累计总交付达到 200"}:d).ToImmutableArray();
        var legacy=Simulation.Create(config with {Milestones=oldGoals},18002) with {Tick=1000,FirstAdmissionPoints=144,Delivered=8};
        var catalog=new SpeciesProgramCatalog(config.AntTypes.Select(a=>a.Id));
        SavedGames.Write(directory,"legacy-goals",new(4,16,new MilestoneRule().Apply(legacy),catalog.Export()));
        var path=Path.Combine(directory,"legacy-goals.json");var bytes=File.ReadAllBytes(path);var migrated=SavedGames.Read(directory,"legacy-goals");
        check(migrated.World.FirstAdmissionPoints==144 && migrated.World.Delivered==8 && migrated.World.Tick==1000 && migrated.World.RandomState==legacy.RandomState && MilestoneRule.Snapshot(migrated.World).Single(m=>m.Id=="deliver-50").Status=="completed" && bytes.SequenceEqual(File.ReadAllBytes(path)),"wire16 old goals migrate and unlock from existing earned points without ticks, RNG or original-file changes");
        var custom=oldGoals.Select(d=>d.Id=="deliver-50"?d with {Description="Custom action counter"}:d).ToImmutableArray();
        SavedGames.Write(directory,"custom-goals",new(4,16,legacy with {Config=config with {Milestones=custom},MilestoneProgress=ImmutableDictionary<string,MilestoneProgress>.Empty.Add("supply-sprint",new(0,Failed:true))},catalog.Export()));
        var preserved=SavedGames.Read(directory,"custom-goals");
        check(preserved.World.Config.Milestones.Single(d=>d.Id=="deliver-50").Metric=="delivered" && preserved.World.MilestoneProgress["supply-sprint"].Failed,"migration preserves custom action-based goals and latched failures");

        await using var session=new GameSession(config,18003,[new DiverseAnts()],savesPath:directory);
        var before=await session.ObserveAsync();await session.ExecuteAsync(new(Guid.NewGuid(),before.Revision,CommandKind.Step));before=await session.ObserveAsync();
        var summaryBefore=await session.ObserveSummaryAsync();
        const string candidate="if(ant_id==1){wait();}else{repeat 16{memory[0]=memory[0]+1;}}";
        var single=await session.PreviewProgramAsync(new("scout",candidate,1));var batch=await session.PreviewProgramBatchAsync(new("scout",candidate));
        var ids=before.Ants.Where(a=>a.Species=="scout").Select(a=>a.Id).Order().ToArray();
        check(single.Error is null && !batch.Passed && batch.FailedAntIds.SequenceEqual(ids.Where(id=>id!=1)) && batch.Results.Select(r=>r.AntId).SequenceEqual(ids) && batch.EvaluatedDecisions==ids.Length*2,"whole-species preflight checks all living members in stable order and catches faults missed by the first ant");
        var reset=await session.PreviewProgramBatchAsync(new("scout","if(memory[0]==7){set_target(12345,23456);}else{wait();}"));
        check(reset.Results.First().Current.Target==new Point(12345,23456) && reset.Results.First().Activation.Target is null && reset.Results.First().Activation.Memory[0]==0,"preflight distinguishes existing memory from the real program-activation reset");
        var local=await session.PreviewProgramBatchAsync(new("scout","set_target(source_x(2),source_y(2));"));
        check(local.Results.Single(r=>r.AntId==1).Current.Target==new Point(0,0) && local.Results.Single(r=>r.AntId==2).Current.Target!=new Point(0,0),"batch preflight constructs each ant's own perception instead of using the Agent's broader projection");
        check(JsonSerializer.Serialize(before)==JsonSerializer.Serialize(await session.ObserveAsync()) && JsonSerializer.Serialize(summaryBefore)==JsonSerializer.Serialize(await session.ObserveSummaryAsync()),"batch preflight changes no projected state, program, memory, revision or event cursor");
        var bad=await Gateway.DispatchAsync(session,new(Protocol.Version,"preview_program_batch",Preview:new("missing","wait();")),default);
        check(bad.Error?.StartsWith("invalid_preview",StringComparison.Ordinal)==true,"empty or invalid batch species returns an explicit error, not a misleading pass");

        await using var faults=new GameSession(config,18004,[new AlternatingFaults()],savesPath:directory);
        var first=await faults.ObserveAsync();
        async Task Step() {var s=await faults.ObserveAsync();await faults.ExecuteAsync(new(Guid.NewGuid(),s.Revision,CommandKind.Step));}
        await Step();var initial=await faults.ReadEventsAsync(new(first.WorldId));var original=initial.Items.Single(e=>e.Kind=="program_error");
        for(var i=1;i<620;i++)await Step();
        var events=await faults.ReadEventsAsync(new(first.WorldId,Limit:100));var errors=events.Items.Where(e=>e.Kind=="program_error").ToArray();
        check(errors.Length==3 && errors.Sum(e=>e.Count)==200 && errors[0].FirstTick==1 && errors[^1].LastTick==399 && errors.All(e=>e.ProgramVersion==0),"200 recurring faults aggregate into three immutable notifications with exact occurrence counts and time ranges");
        check(events.Items.Single(e=>e.Kind=="program_recovered").Tick==599 && events.Items.Single(e=>e.Kind=="program_recovered").Count==200,"brief error-free waits do not fake recovery; 200 quiet ticks with a successful decision report stable recovery once");
        check(events.Items.Any(e=>e.Kind=="queen_attacked" && e.Tick==2 && e.Severity=="critical") && events.Items.First(e=>e.Sequence==original.Sequence)==original,"critical damage remains immediate and earlier event pages remain unchanged after aggregation");
        await Step();await Step();await Step();await Step();
        var changed=await faults.ReadEventsAsync(new(first.WorldId,events.Cursor,Limit:100));
        check(changed.Items.Count(e=>e.Kind=="program_error")==3 && changed.Items.Any(e=>e.Kind=="program_error" && e.ProgramVersion==1) && changed.Items.Any(e=>e.Kind=="program_error" && e.Detail=="other_reason") && changed.Items.Any(e=>e.Kind=="ant_died"),"new program versions, new reasons and death still notify immediately instead of being swallowed by aggregation");
        await using var filtered=new GameSession(config,18005,[new GroupSwitchFaults()],savesPath:directory);
        var f=await filtered.ObserveAsync();
        foreach(var group in new[]{1,2})await filtered.ConfigureWatchAsync(new(f.WorldId,"upsert",new("group"+group,"Only this group") {Kinds=["program_error"],GroupId=group}));
        for(var i=0;i<10;i++){f=await filtered.ObserveAsync();await filtered.ExecuteAsync(new(Guid.NewGuid(),f.Revision,CommandKind.Step));}
        f=await filtered.ObserveAsync();await filtered.ExecuteAsync(new(Guid.NewGuid(),f.Revision,CommandKind.Pause));
        var groupOne=await filtered.ReadEventsAsync(new(f.WorldId,WatchId:"group1"));var groupTwo=await filtered.ReadEventsAsync(new(f.WorldId,WatchId:"group2"));
        check(groupOne.Items.Sum(e=>e.Count)==5 && groupTwo.Items.Sum(e=>e.Count)==5 && groupOne.Items.All(e=>e.LastTick<=5) && groupTwo.Items.All(e=>e.FirstTick>=6),"regrouping flushes old-scope fault counts before new-scope alerts; pause flushes remaining counts without cross-group contamination");
    }
    private sealed class DiverseAnts : IWorldRule {
        public string Id=>"test.diverse-preflight";public int Order=>100;
        public WorldState Apply(WorldState w) {
            if(w.Tick!=1)return w;
            var nest=((QueenObject)w.Objects.Find("queen",1)!).Position;var source=(SourceView)w.Objects.Find("source",2)!;
            return w with {Ants=w.Ants.Select(a=>a.Id==1?a with {Position=nest,Memory=a.Memory.SetItem(0,7)}:a.Id==2?a with {Position=source.Position}:a.Id==5?a with {Health=0}:a).ToImmutableArray()};
        }
    }
    private sealed class AlternatingFaults : IWorldRule {
        public string Id=>"test.alternating-program-fault";public int Order=>100;
        public WorldState Apply(WorldState w) {
            if(w.Tick>624)return w;
            var a=w.Ants.Single(x=>x.Id==1);var error=w.Tick<=399 && w.Tick%2==1?"repeating_fault":w.Tick is 621 or 622?"new_version_fault":w.Tick==623?"other_reason":null;
            a=a with {LastError=error,ProgramVersion=w.Tick>=622?2:w.Tick==621?1:0,NextDecisionTick=long.MaxValue,RecentDecisions=a.RecentDecisions.Add(new(w.Tick,0,"test",1,error)),Health=w.Tick==624?0:a.Health};
            w=w with {Objects=w.Objects.Put(a)};
            if(w.Tick==2){var queen=(QueenObject)w.Objects.Find("queen",1)!;w=w with {Objects=w.Objects.Put(queen with {Health=queen.Health-1})};}
            return w;
        }
    }
    private sealed class GroupSwitchFaults : IWorldRule {
        public string Id=>"test.group-switch-fault";public int Order=>100;
        public WorldState Apply(WorldState w) {
            var a=w.Ants.Single(x=>x.Id==1);
            return w with {Objects=w.Objects.Put(a with {GroupId=w.Tick<=5?1:2,LastError="same_fault",NextDecisionTick=long.MaxValue,RecentDecisions=a.RecentDecisions.Add(new(w.Tick,0,"test",1,"same_fault"))})};
        }
    }
}
