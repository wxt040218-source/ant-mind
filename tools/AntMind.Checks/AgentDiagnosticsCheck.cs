using System.Collections.Immutable;
using System.Text.Json;
using AntMind.Contracts;
using AntMind.Core;
using AntMind.Programs;
using AntMind.Runtime;

internal static class AgentDiagnosticsCheck {
    public static async Task Run(Action<bool,string> check) {
        var config=ContentLoader.LoadDefault() with {Combat=new(),InitialResourceNodes=0};
        var directory=Path.Combine(Directory.GetCurrentDirectory(),"artifacts","agent-diagnostics-checks",Guid.NewGuid().ToString("N"));
        await using var session=new GameSession(config,17001,[new AlertScenario()],savesPath:directory);
        var start=await session.ObserveAsync();var rngBefore=start.Revision;
        var allWatch=new AgentWatch("test","All observed events") {Kinds=AgentEventKinds.All,RadiusCells=4};
        var registered=await session.ConfigureWatchAsync(new(start.WorldId,"upsert",allWatch));
        check(registered.Error is null && (await session.ObserveAsync()).Revision==rngBefore,"registering watches changes no simulation revision");
        var identical=await session.ConfigureWatchAsync(new(start.WorldId,"upsert",allWatch));
        check(identical.Cursor==registered.Cursor,"identical watch updates do not duplicate initial alerts");
        var invalid=await session.ConfigureWatchAsync(new(start.WorldId,"upsert",allWatch with {Kinds=["unknown_kind"]}));
        check(invalid.Error=="invalid_watch" && (await session.ConfigureWatchAsync(new(start.WorldId))).Watches.Length==2,"invalid watch kinds are rejected without altering active subscriptions");
        check((await session.ConfigureWatchAsync(new(Guid.NewGuid(),"list"))).Error=="world_conflict","watch requests require the actual world instance");
        async Task<CommandResult> Cmd(CommandKind kind,string key="",GroupConfiguration? setup=null,int group=0,Guid? requestId=null) {
            var s=await session.ObserveAsync();return await session.ExecuteAsync(new(requestId??Guid.NewGuid(),s.Revision,kind,Reason:"agent diagnostics check") {ExpectedWorldId=s.WorldId,Key=key,GroupId=group,GroupConfiguration=setup});
        }
        await Cmd(CommandKind.Step);var page=await session.ReadEventsAsync(new(start.WorldId,WatchId:"test"));
        check(page.Items.IsEmpty,"hidden enemies never produce discovery or proximity events");
        await Cmd(CommandKind.Step);page=await session.ReadEventsAsync(new(start.WorldId,WatchId:"test"));
        check(page.Items.Count(e=>e.Kind=="enemy_seen")==1 && page.Items.Count(e=>e.Kind=="enemy_entered_zone")==1 && page.Items.All(e=>e.ObjectId!=901),"only newly observed enemy information raises discovered and zone-entry events");
        var cursor=page.Cursor;await Cmd(CommandKind.Step);var stale=await session.ReadEventsAsync(new(start.WorldId,cursor,"test"));
        check(stale.Items.IsEmpty,"a hidden true position update outside vision is not observable through event watches");
        await Cmd(CommandKind.Step);await Cmd(CommandKind.Step);var reentry=await session.ReadEventsAsync(new(start.WorldId,cursor,"test"));
        check(reentry.Items.Count(e=>e.Kind=="enemy_entered_zone")==1 && reentry.Items[0].ObservedTick==5,"a re-observed exit and re-entry triggers exactly one fresh zone alert");
        await Cmd(CommandKind.Step);var injury=await session.ReadEventsAsync(new(start.WorldId,reentry.Cursor,"test"));
        check(injury.Items.Any(e=>e.Kind=="queen_attacked" && e.Severity=="critical") && injury.Items.Any(e=>e.Kind=="ant_died" && e.ObjectId==2) && injury.Items.Any(e=>e.Kind=="ant_attacked" && e.ObjectId==1),"queen damage, own-ant damage and casualties produce separate actionable events");
        check(injury.Items.Any(e=>e.Kind=="building_ready" && e.ObjectId==1),"observed completed construction raises a building-ready event");
        cursor=injury.Cursor;for(var i=0;i<5;i++)await Cmd(CommandKind.Step);
        var failed=await session.ReadEventsAsync(new(start.WorldId,cursor,"test"));
        check(failed.Items.Count(e=>e.Kind=="action_failed")==1 && failed.Items.Single(e=>e.Kind=="action_failed").Count==3,"repeated physical failures are counted by attempts, not each stale-status tick");
        for(var i=0;i<4;i++)await Cmd(CommandKind.Step);var completed=await session.ReadEventsAsync(new(start.WorldId,failed.Cursor,"test"));
        check(completed.Items.Count(e=>e.Kind=="production_completed")==1 && completed.Items.Count(e=>e.Kind=="program_error")==1,"production completion and a persistent program fault each notify once");
        var duplicate=await session.ReadEventsAsync(new(start.WorldId,failed.Cursor,"test"));
        check(JsonSerializer.Serialize(duplicate.Items)==JsonSerializer.Serialize(completed.Items),"event reads replay reliably and do not acknowledge or consume messages");
        check((await session.ReadEventsAsync(new(Guid.NewGuid()))).Error=="world_conflict" && (await session.ReadEventsAsync(new(start.WorldId,long.MaxValue))).Error=="invalid_event_cursor_or_limit","world switches and invalid future cursors are explicit errors");
        var summary=await session.ObserveSummaryAsync();var full=await session.ObserveAsync();
        check(summary.QueenHealth==full.Queen!.Health && summary.Problems.Any(a=>a.Result=="test_no_items" || a.ProgramError=="test_program_fault") && !JsonSerializer.Serialize(summary).Contains("ActiveSource"),"compact summaries expose real physical failures and omit code/history payloads");
        check(summary.KnownEnemies.All(e=>e.Id!=901) && summary.Buildings.Single().RemainingMaterial==0,"summary discovery permissions and completed-material needs match public observations");
        var beforePreview=await session.ObserveAsync();var preview=await session.PreviewProgramAsync(new("scout","memory[0]=123;set_target(12345,23456);",1));var afterPreview=await session.ObserveAsync();
        check(preview.Target==new Point(12345,23456) && preview.Memory[0]==123 && preview.Spent<=preview.Budget && beforePreview.Revision==afterPreview.Revision && beforePreview.Ants[0].Memory.SequenceEqual(afterPreview.Ants[0].Memory),"program preview uses real permitted perception without mutating memory, target or revision");
        var oldError=await Gateway.DispatchAsync(session,new(Protocol.Version,"preview_program",Preview:new("scout","while(true){}",1)),default);
        check(oldError.Error?.StartsWith("invalid_preview",StringComparison.Ordinal)==true,"invalid preview sources return a useful error without changing the installed program");

        // Atomically replace data and membership, while keeping explicit queen communication.
        await using var groups=new GameSession(config,17002,[new MoveAntOutside()],savesPath:directory);
        async Task<CommandResult> GroupCmd(GroupConfiguration setup,int group=0,Guid? id=null,long? revision=null) {
            var s=await groups.ObserveAsync();return await groups.ExecuteAsync(new(id??Guid.NewGuid(),revision??s.Revision,CommandKind.ConfigureGroup,Reason:"atomic group regression") {ExpectedWorldId=s.WorldId,GroupId=group,GroupConfiguration=setup});
        }
        var setup=new GroupConfiguration("Fruit route","Agent-defined logistics") {Members=[1,6],Parameters=ImmutableDictionary<string,int>.Empty.Add("source",1).Add("to",0),Points=ImmutableDictionary<string,Point>.Empty.Add("take",new(69000,64000)).Add("drop",new(64000,64000))};
        var baseline=await groups.ObserveAsync();var identity=Guid.NewGuid();var added=await GroupCmd(setup,id:identity,revision:baseline.Revision);var configured=await groups.ObserveAsync();
        check(added.Accepted && added.Detail=="1" && configured.Revision==baseline.Revision+1 && configured.Groups.Single().Points.Count==2 && configured.Ants.Count(a=>a.GroupId==1)==2,"one group command creates complete parameters, points and membership in one revision");
        var reordered=setup with {Parameters=setup.Parameters.Reverse().ToImmutableDictionary(),Points=setup.Points.Reverse().ToImmutableDictionary()};
        var retry=await GroupCmd(reordered,id:identity,revision:baseline.Revision);
        check(retry==added && (await groups.ObserveAsync()).Revision==configured.Revision,"idempotent retries compare nested configuration values instead of collection references");
        var reused=await GroupCmd(setup with {Task="different"},id:identity,revision:baseline.Revision);
        check(reused.Code=="request_id_reused","reusing a group request ID with different nested content is rejected");
        var bad=await GroupCmd(setup with {Members=[1,999]},1);var badPoint=await GroupCmd(setup with {Points=setup.Points.Add("bad",new(-1,0))},1);
        check(!bad.Accepted && !badPoint.Accepted && (await groups.ObserveAsync()).Revision==configured.Revision && (await groups.ObserveAsync()).Groups.Single().Version==1,"bad group fields or members reject the whole replacement without a partial version or assignment");
        var pending=await groups.ObserveSummaryAsync();
        check(pending.Groups.Single().Pending==2 && pending.Groups.Single().Receipts.All(r=>r.ReceivedVersion==0 && r.Status=="awaiting_request"),"Agent task receipts report requested versus actually cached versions");
        await groups.ExecuteAsync(new(Guid.NewGuid(),configured.Revision,CommandKind.Step));
        var ack=await groups.ObserveSummaryAsync();
        check(ack.Groups.Single().Acknowledged==2 && ack.Groups.Single().Pending==0,"only an explicit successful queen request acknowledges the new group task");
        var changed=await GroupCmd(setup with {Task="Version two",Members=[1] },1);var versionTwo=await groups.ObserveAsync();
        check(changed.Accepted && versionTwo.Ants.Single(a=>a.Id==1).ReceivedOrder?.Version==1 && versionTwo.Ants.Single(a=>a.Id==6).GroupId==0 && versionTwo.Ants.Single(a=>a.Id==6).ReceivedOrder is null,"retained members keep old cached orders until refresh while removed members are reset");
        check((await groups.ObserveSummaryAsync()).Groups.Single().Receipts.Single().Status=="out_of_range","task diagnostics identify an out-of-range member awaiting an updated order without telepathic delivery");
        var current=await groups.ObserveAsync();await groups.ExecuteAsync(new(Guid.NewGuid(),current.Revision,CommandKind.SaveGame,Reason:"save event regression") {Key="event-checkpoint",ExpectedWorldId=current.WorldId});
        current=await groups.ObserveAsync();await groups.ExecuteAsync(new(Guid.NewGuid(),current.Revision,CommandKind.LoadGame,Reason:"load event regression") {Key="event-checkpoint",ExpectedWorldId=current.WorldId});
        var loaded=await groups.ObserveAsync();
        check(loaded.WorldId!=current.WorldId && (await groups.ReadEventsAsync(new(current.WorldId))).Error=="world_conflict" && (await groups.ConfigureWatchAsync(new(loaded.WorldId))).Watches.Single().Id=="default","load rotates event identity and resets session watches so old cursors cannot silently target a different world");

        // Ring retention, independent readers, capacity and metadata-only updates.
        var ringStart=await groups.ObserveAsync();
        for(var i=0;i<2100;i++)await groups.ConfigureWatchAsync(new(ringStart.WorldId,"upsert",new("overflow","Alert "+i) {Kinds=["stock_below"],StockItem="meat",StockThreshold=0}));
        var gap=await groups.ReadEventsAsync(new(ringStart.WorldId,Limit:100));
        check(gap.Gap && gap.HasMore && gap.Items.Length==100 && gap.OldestCursor>1,"bounded event retention reports history loss and paginates instead of growing forever");
        long seen=gap.Cursor;var sequences=gap.Items.Select(e=>e.Sequence).ToHashSet();
        while(true) {var pageNext=await groups.ReadEventsAsync(new(ringStart.WorldId,seen,Limit:100));foreach(var e in pageNext.Items)if(!sequences.Add(e.Sequence))throw new Exception("Duplicate page event");seen=pageNext.Cursor;if(!pageNext.HasMore)break;}
        check(sequences.Count==2048 && (await groups.ObserveAsync()).Tick==ringStart.Tick && (await groups.ObserveAsync()).Revision==ringStart.Revision,"event pagination returns every retained event once without advancing the simulation");
        for(var i=0;i<30;i++)await groups.ConfigureWatchAsync(new(ringStart.WorldId,"upsert",new("watch"+i,"Bounded watch") {FireInitial=false}));
        check((await groups.ConfigureWatchAsync(new(ringStart.WorldId,"upsert",new("extra","Too many")))).Error=="watch_capacity","watch configuration has a fixed session capacity");
        check((await groups.ConfigureWatchAsync(new(ringStart.WorldId,"remove",Id:"watch0"))).Error is null,"removing a watch frees capacity without deleting historical events");

        var local=new Perception(1,1,new(1000,1000),new(0,0),0,new(5000,5000),128,128,[],CurrentAction:AntAction.Move);
        var strict=AntDslCompiler.Compile("set_target(require_group_x(\"missing\"),1000);").Execute(local,new(64));
        check(strict.Error=="missing_group_point:missing" && strict.Target is null && strict.Action==AntAction.Wait,"strict group field reads stop with an explicit missing-key error instead of moving to zero");
        var zero=AntDslCompiler.Compile("set_target(require_group_x(\"home\"),require_group_y(\"home\"));").Execute(local with {Order=new(1,"origin","",new(),new(),new()) {Points=ImmutableDictionary<string,Point>.Empty.Add("home",new(0,0))}},new(64));
        check(zero.Error is null && zero.Target==new Point(0,0),"strict reads preserve a deliberately configured origin coordinate");
        var guard=AntDslCompiler.Compile(DefaultProgramSources.Soldier);var dangerous=local with {Species="soldier",Position=new(0,0),Health=8,MaxHealth=40,NearbyEnemies=[new(1,new(500,500),4,18,1)],NestStock=ImmutableDictionary<string,long>.Empty.Add("fruit",10)};
        check(guard.Execute(dangerous,new(64)).Action==AntAction.Attack,"injured default guards attack an enemy in reach instead of endlessly trying interrupted meals");
        check(guard.Execute(dangerous with {NearbyEnemies=[]},new(64)).Action==AntAction.Eat,"the same guard explicitly heals only when the local area is safe");
        var readyConfig=ContentLoader.LoadDefault();var kitchen=new BuildingView(1,new(0,0),"workshop") {Phase="ready"};
        var chefSense=local with {Position=new(0,0),TechnologyLevel=2,BuildingTypes=readyConfig.Production.Buildings,Recipes=readyConfig.Production.Recipes,Buildings=[kitchen],Order=new(1,"chef","",new(),new(),new()) {Parameters=ImmutableDictionary<string,int>.Empty.Add("station",1).Add("recipe",1)}};
        var chef=AntDslCompiler.Compile(DefaultProgramSources.Chef);
        check(chef.Execute(chefSense,new(64)).Action==AntAction.Wait && chef.Execute(chefSense with {Buildings=[kitchen with {Store=new FoodStore().Add("fruit",2)}]},new(64)).Action==AntAction.Process,"default chefs wait for complete ingredients and process explicitly once ready");
    }
    private sealed class MoveAntOutside : IWorldRule {
        public string Id=>"test.move-member-outside";public int Order=>70;
        public WorldState Apply(WorldState w)=>w.Tick==1?w with {Ants=w.Ants.Select(a=>a.Id==1?a with {Position=new(1000,1000)}:a).ToImmutableArray()}:w;
    }
    private sealed class AlertScenario : IWorldRule {
        public string Id=>"test.alert-scenario";public int Order=>60;
        public WorldState Apply(WorldState w) {
            var nest=((QueenObject)w.Objects.Find("queen",1)!).Position;
            if(w.Tick==1) {
                var building=new BuildingView(1,new(nest.X+3000,nest.Y),"workshop") {IsDiscovered=true,LastObservedTick=1};
                w=w with {Objects=w.Objects.Put(new EnemyView(900,new(nest.X+1000,nest.Y),18,18,1)).Put(new EnemyView(901,new(1000,1000),18,18,1)).Put(building),ObservedBuildings=w.ObservedBuildings.Add(1,building),NextEnemyId=902,NextBuildingId=2};
            }
            if(w.Tick is >=2 and <=5) {
                var enemy=(EnemyView)w.Objects.Find("enemy",900)!;var pos=new Point(nest.X+(w.Tick is 3 or 4?12000:1000),nest.Y);
                enemy=w.Tick==3?enemy with {Position=pos}:enemy with {Position=pos,IsDiscovered=true,LastKnownPosition=pos,LastKnownHealth=18,LastSeenTick=w.Tick};w=w with {Objects=w.Objects.Put(enemy)};
            }
            if(w.Tick==6) {
                var queen=(QueenObject)w.Objects.Find("queen",1)!;var one=w.Ants.Single(a=>a.Id==1);var two=w.Ants.Single(a=>a.Id==2);
                var building=(BuildingView)w.Objects.Find("building",1)!;building=building with {Phase="ready",ConstructionWork=200,LastObservedTick=6};
                w=w with {Objects=w.Objects.Put(queen with {Health=queen.Health-3}).Put(one with {Health=one.Health-2}).Put(two with {Health=0}).Put(building),ObservedBuildings=w.ObservedBuildings.SetItem(1,building)};
            }
            if(w.Tick is >=7 and <=11) {
                var ant=w.Ants.Single(a=>a.Id==3);var log=w.Tick is 7 or 9 or 11?ant.RecentDecisions.Add(new(w.Tick,0,"test",1,null)):ant.RecentDecisions;
                w=w with {Objects=w.Objects.Put(ant with {ActionStatus=ActionStatus.Failed,ActionResult="test_no_items",RecentDecisions=log})};
            }
            if(w.Tick==12 || w.Tick==13) {
                var building=(BuildingView)w.Objects.Find("building",1)!;building=building with {Recipe=w.Tick==12?"concentrate":"",Output=w.Tick==13?new FoodStore().Add("fruit_concentrate",1):new(),LastObservedTick=w.Tick};
                w=w with {Objects=w.Objects.Put(building),ObservedBuildings=w.ObservedBuildings.SetItem(1,building)};
            }
            if(w.Tick is 14 or 15) {var ant=w.Ants.Single(a=>a.Id==4);w=w with {Objects=w.Objects.Put(ant with {LastError="test_program_fault"})};}
            return w;
        }
    }
}
