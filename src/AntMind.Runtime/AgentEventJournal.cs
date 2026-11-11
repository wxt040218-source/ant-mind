using System.Collections.Immutable;
using AntMind.Contracts;
namespace AntMind.Runtime;

// Actor-owned session diagnostics. Frames contain own ants and already projected enemy/building knowledge.
internal sealed record AgentFrame(long Tick,long Revision,QueenObject Queen,ImmutableArray<AntView> Ants,ImmutableArray<AntView> DeadAnts,ImmutableArray<EnemyView> Enemies,ImmutableArray<BuildingView> Buildings,FoodStore Stock);
internal sealed class AgentEventJournal {
    private sealed class WatchState(AgentWatch definition) {
        public AgentWatch Definition=definition;
        public HashSet<int> Inside=[];
        public bool LowStock;
    }
    private sealed record Failure(string Reason,long DecisionTick,int Count);
    private sealed class ProgramFault(AntView ant,long tick,long decisionTick) {
        public AntView Ant=ant;
        public readonly string Reason=ant.LastError!;
        public readonly int Version=ant.ProgramVersion;
        public readonly int GroupId=ant.GroupId;
        public readonly string Species=ant.Species;
        public readonly long FirstTick=tick;
        public long LastTick=tick,DecisionTick=decisionTick,LastEmitted=tick,PendingFirstTick=-1;
        public int Total=1,Pending;
    }
    private const int FaultWindowTicks=200;
    private readonly Dictionary<string,WatchState> watches=new(StringComparer.Ordinal);
    private readonly Dictionary<int,Failure> failures=[];
    private readonly Dictionary<int,ProgramFault> programFaults=[];
    private readonly Queue<AgentEvent> events=[];
    private AgentFrame previous=null!;
    private Guid worldId;
    public long Cursor {get;private set;}
    public int FailureCount(int id)=>failures.GetValueOrDefault(id)?.Count??0;
    public void Reset(Guid id,AgentFrame frame) {
        worldId=id;Cursor=0;events.Clear();watches.Clear();failures.Clear();programFaults.Clear();previous=frame;
        watches.Add("default",new(new("default","Colony alerts")));
    }
    private void Emit(WatchState watch,AgentFrame frame,string kind,string severity,string objectKind,int objectId,Point? position,string detail,int count=1,long observedTick=-1,long firstTick=-1,long lastTick=-1,int? programVersion=null) {
        events.Enqueue(new(++Cursor,worldId,frame.Tick,frame.Revision,watch.Definition.Id,kind,severity,objectKind,objectId,position,detail,count,observedTick) {
            FirstTick=firstTick,LastTick=lastTick,ProgramVersion=programVersion
        });
        while(events.Count>2048)events.Dequeue();
    }
    private void Publish(AgentFrame frame,string kind,string severity,string objectKind,int id,Point? position,string detail,int count=1,AntView? ant=null,long observedTick=-1,long firstTick=-1,long lastTick=-1,int? programVersion=null) {
        foreach(var state in watches.Values.OrderBy(w=>w.Definition.Id,StringComparer.Ordinal)) {
            var w=state.Definition;
            if(!w.Kinds.Contains(kind) || ant is not null && (w.GroupId>0 && ant.GroupId!=w.GroupId || w.Species.Length>0 && ant.Species!=w.Species))continue;
            if(kind=="action_failed" && count!=w.FailureThreshold)continue;
            Emit(state,frame,kind,severity,objectKind,id,position,detail,count,observedTick,firstTick,lastTick,programVersion);
        }
    }
    private void FaultEvent(AgentFrame frame,ProgramFault fault,string kind,int count,long firstTick)=>Publish(frame,kind,kind=="program_error"?"warning":"info","ant",fault.Ant.Id,fault.Ant.Position,fault.Reason,count,ant:fault.Ant,firstTick:firstTick,lastTick:fault.LastTick,programVersion:fault.Version);
    private void FlushFault(AgentFrame frame,ProgramFault fault) {
        if(fault.Pending==0)return;
        FaultEvent(frame,fault,"program_error",fault.Pending,fault.PendingFirstTick);
        fault.Pending=0;fault.PendingFirstTick=-1;fault.LastEmitted=frame.Tick;
    }
    // Append summaries; never edit already published events or invalidate a reader's cursor.
    public void FlushProgramErrors(AgentFrame frame) {
        foreach(var fault in programFaults.OrderBy(x=>x.Key).Select(x=>x.Value))FlushFault(frame,fault);
    }
    private void ObserveProgramFault(AgentFrame frame,AntView ant) {
        if(ant.LastError is null)return;
        var decision=ant.RecentDecisions.IsDefaultOrEmpty?-1:ant.RecentDecisions[^1].Tick;
        if(!programFaults.TryGetValue(ant.Id,out var fault) || fault.Version!=ant.ProgramVersion || fault.Reason!=ant.LastError || fault.GroupId!=ant.GroupId || fault.Species!=ant.Species) {
            if(fault is not null)FlushFault(frame,fault);
            fault=new(ant,frame.Tick,decision);programFaults[ant.Id]=fault;
            FaultEvent(frame,fault,"program_error",1,frame.Tick);
        } else if(fault.DecisionTick!=decision) {
            if(fault.Pending==0)fault.PendingFirstTick=frame.Tick;
            fault.Ant=ant;fault.LastTick=frame.Tick;fault.DecisionTick=decision;fault.Pending++;fault.Total++;
        }
    }
    private void FinishProgramFaults(AgentFrame frame) {
        var alive=frame.Ants.ToDictionary(a=>a.Id);
        foreach(var (id,fault) in programFaults.OrderBy(x=>x.Key).ToArray()) {
            if(!alive.TryGetValue(id,out var ant)) {FlushFault(frame,fault);programFaults.Remove(id);continue;}
            if(fault.GroupId!=ant.GroupId || fault.Species!=ant.Species) {FlushFault(frame,fault);programFaults.Remove(id);continue;}
            fault.Ant=ant;
            if(frame.Tick-fault.LastEmitted>=FaultWindowTicks)FlushFault(frame,fault);
            // A transient successful wait between two failing decisions is not a stable recovery.
            var decision=ant.RecentDecisions.IsDefaultOrEmpty?null:ant.RecentDecisions[^1];
            if(ant.LastError is null && decision is {Error:null} && decision.Tick>fault.DecisionTick && frame.Tick-fault.LastTick>=FaultWindowTicks) {
                FlushFault(frame,fault);FaultEvent(frame,fault,"program_recovered",fault.Total,fault.FirstTick);programFaults.Remove(id);
            }
        }
    }
    private void Zones(WatchState state,AgentFrame frame,bool initial=false) {
        var w=state.Definition;var center=w.Center??frame.Queen.Position;var radius=(long)w.RadiusCells*Point.UnitsPerCell;
        if(w.Kinds.Contains("enemy_entered_zone"))foreach(var enemy in frame.Enemies.OrderBy(e=>e.Id)) {
            // Loss of sight is not an exit. Only a newly observed position may change membership.
            if(enemy.LastSeenTick!=frame.Tick)continue;
            if(enemy.Health<=0) {state.Inside.Remove(enemy.Id);continue;}
            var x=(long)enemy.Position.X-center.X;var y=(long)enemy.Position.Y-center.Y;
            var inside=x*x+y*y<=radius*radius;var entered=inside && state.Inside.Add(enemy.Id);
            if(!inside)state.Inside.Remove(enemy.Id);
            if(entered && (!initial || w.FireInitial))Emit(state,frame,"enemy_entered_zone","warning","enemy",enemy.Id,enemy.Position,initial?"Already observed inside the watched zone":"Observed entering the watched zone",observedTick:enemy.LastSeenTick);
        }
        if(w.Kinds.Contains("stock_below")) {
            var low=frame.Stock.Get(w.StockItem)<=w.StockThreshold;
            if(low && !state.LowStock && (!initial || w.FireInitial))Emit(state,frame,"stock_below","warning","stock",0,null,$"{w.StockItem} <= {w.StockThreshold}");
            state.LowStock=low;
        }
    }
    public WatchResult Configure(WatchRequest request,AgentFrame frame,int width,int height,IEnumerable<string> species,IEnumerable<string> items) {
        WatchResult Result(string? error=null)=>new(worldId,Cursor,watches.Values.OrderBy(x=>x.Definition.Id,StringComparer.Ordinal).Select(x=>x.Definition).ToImmutableArray(),error);
        if(request.WorldId!=worldId)return new(worldId,Cursor,[],"world_conflict");
        if(request.Action=="list")return Result();
        if(request.Action=="remove")return Result(!string.IsNullOrWhiteSpace(request.Id) && watches.Remove(request.Id)?null:"watch_not_found");
        if(request.Action!="upsert" || request.Watch is not {} w)return Result("invalid_watch_action");
        bool Key(string value)=>!string.IsNullOrEmpty(value) && value.Length is >0 and <=40 && value.All(c=>char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.');
        if(!Key(w.Id) || string.IsNullOrWhiteSpace(w.Name) || w.Name.Length>80 || w.Kinds.IsDefaultOrEmpty || w.Kinds.Length>AgentEventKinds.All.Length || w.Kinds.Any(x=>!AgentEventKinds.All.Contains(x)) ||
            w.RadiusCells is <1 or >1024 || w.GroupId<0 || w.FailureThreshold is <1 or >100 || w.StockThreshold<0 || string.IsNullOrWhiteSpace(w.StockItem) || w.StockItem.Length>40 || w.Species is null || w.Species.Length>0 && !species.Contains(w.Species) || w.Kinds.Contains("stock_below") && !items.Contains(w.StockItem) ||
            w.Center is {} p && (p.X<0 || p.Y<0 || p.X>(width-1)*1000 || p.Y>(height-1)*1000))return Result("invalid_watch");
        if(!watches.ContainsKey(w.Id) && watches.Count>=32)return Result("watch_capacity");
        w=w with {Kinds=w.Kinds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray()};
        if(watches.TryGetValue(w.Id,out var old) && old.Definition with {Kinds=[]}==w with {Kinds=[]} && old.Definition.Kinds.SequenceEqual(w.Kinds))return Result();
        var state=new WatchState(w);watches[w.Id]=state;Zones(state,frame,true);return Result();
    }
    public AgentEventPage Read(EventReadRequest request,AgentFrame frame,bool paused) {
        var oldest=events.Count>0?events.Peek().Sequence:Cursor+1;
        AgentEventPage Empty(string error)=>new(worldId,frame.Tick,frame.Revision,paused,Cursor,oldest,false,false,[],error);
        if(request.WorldId!=worldId)return Empty("world_conflict");
        if(request.WatchId is null || request.After<0 || request.After>Cursor || request.Limit is <1 or >100)return Empty("invalid_event_cursor_or_limit");
        if(request.WatchId.Length>0 && !watches.ContainsKey(request.WatchId))return Empty("watch_not_found");
        var selected=events.Where(e=>e.Sequence>request.After && (request.WatchId.Length==0 || e.WatchId==request.WatchId)).Take(request.Limit).ToImmutableArray();
        var cursor=selected.Length==request.Limit?selected[^1].Sequence:Cursor;
        return new(worldId,frame.Tick,frame.Revision,paused,cursor,oldest,request.After<oldest-1,cursor<Cursor,selected);
    }
    public void Observe(AgentFrame frame) {
        if(frame.Queen.Health<previous.Queen.Health)Publish(frame,"queen_attacked","critical","queen",frame.Queen.Id,frame.Queen.Position,$"Health {previous.Queen.Health} -> {frame.Queen.Health}");
        var oldAnts=previous.Ants.ToDictionary(a=>a.Id);var oldDead=previous.DeadAnts.Select(a=>a.Id).ToHashSet();
        foreach(var ant in frame.DeadAnts.Where(a=>!oldDead.Contains(a.Id)).OrderBy(a=>a.Id))Publish(frame,"ant_died","critical","ant",ant.Id,ant.Position,ant.Species+" died",ant:ant);
        foreach(var ant in frame.Ants.OrderBy(a=>a.Id)) {
            oldAnts.TryGetValue(ant.Id,out var old);
            if(old is not null && ant.Health<old.Health)Publish(frame,"ant_attacked","warning","ant",ant.Id,ant.Position,$"Health {old.Health} -> {ant.Health}",ant:ant);
            ObserveProgramFault(frame,ant);
            if(ant.ReceivedOrder is {} received && (old?.ReceivedOrder?.Id!=received.Id || old?.ReceivedOrder?.Version!=received.Version))Publish(frame,"task_received","info","ant",ant.Id,ant.Position,$"Group {received.Id} version {received.Version}",received.Version,ant);
            if(ant.ActionStatus==ActionStatus.Failed) {
                var tick=ant.RecentDecisions.IsDefaultOrEmpty?-1:ant.RecentDecisions[^1].Tick;
                var last=failures.GetValueOrDefault(ant.Id);
                if(last is null || last.DecisionTick!=tick || last.Reason!=ant.ActionResult) {
                    var count=last?.Reason==ant.ActionResult?last.Count+1:1;failures[ant.Id]=new(ant.ActionResult,tick,count);
                    Publish(frame,"action_failed","warning","ant",ant.Id,ant.Position,ant.ActionResult,count,ant);
                }
            } else if(ant.ActionStatus==ActionStatus.Succeeded)failures.Remove(ant.Id);
        }
        foreach(var id in failures.Keys.Where(id=>!frame.Ants.Any(a=>a.Id==id)).ToArray())failures.Remove(id);
        FinishProgramFaults(frame);
        var oldEnemies=previous.Enemies.ToDictionary(e=>e.Id);
        foreach(var enemy in frame.Enemies.OrderBy(e=>e.Id)) {
            oldEnemies.TryGetValue(enemy.Id,out var old);
            if(old is null && enemy.Health>0)Publish(frame,"enemy_seen","warning","enemy",enemy.Id,enemy.Position,"Enemy discovered",observedTick:enemy.LastSeenTick);
            if(old is {Health:>0} && enemy.Health<=0)Publish(frame,"enemy_defeated","info","enemy",enemy.Id,enemy.Position,"Enemy observed defeated",observedTick:enemy.LastSeenTick);
        }
        var oldBuildings=previous.Buildings.ToDictionary(b=>b.Id);
        foreach(var b in frame.Buildings.OrderBy(b=>b.Id)) {
            oldBuildings.TryGetValue(b.Id,out var old);
            if(b.Phase=="ready" && old?.Phase!="ready")Publish(frame,"building_ready","info","building",b.Id,b.Position,b.Type+" ready",observedTick:b.LastObservedTick);
            if(old is {Recipe.Length:>0} && b.Recipe.Length==0 && b.Output.Total>old.Output.Total)Publish(frame,"production_completed","info","building",b.Id,b.Position,old.Recipe+" completed",observedTick:b.LastObservedTick);
        }
        foreach(var state in watches.Values.OrderBy(w=>w.Definition.Id,StringComparer.Ordinal))Zones(state,frame);
        previous=frame;
    }
}
