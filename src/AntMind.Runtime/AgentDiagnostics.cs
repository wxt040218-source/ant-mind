using System.Collections.Immutable;
using AntMind.Contracts;
using AntMind.Core;
using AntMind.Programs;
namespace AntMind.Runtime;

public sealed partial class GameSession {
    private readonly AgentEventJournal eventJournal=new();
    private AgentFrame AgentFrame()=>new(world.Tick,revision,(QueenObject)world.Objects.Find("queen",1)!,world.Ants,world.Objects.All<AntView>().Where(a=>a.Health<=0).ToImmutableArray(),
        world.Objects.All<EnemyView>().Where(e=>e.IsDiscovered).Select(e=>e with {Position=e.LastKnownPosition,Health=e.LastKnownHealth}).ToImmutableArray(),world.ObservedBuildings.Values.OrderBy(b=>b.Id).ToImmutableArray(),world.Stock);
    private CommandResult ApplyAndNotify(GameCommand command) {
        var before=worldId;var result=Apply(command);
        if(result.Accepted) {
            if(before!=worldId)eventJournal.Reset(worldId,AgentFrame());
            else if(command.Kind!=CommandKind.Step) {
                var frame=AgentFrame();eventJournal.Observe(frame);
                if(command.Kind==CommandKind.Pause)eventJournal.FlushProgramErrors(frame);
            }
        }
        return result;
    }
    private async Task<T> QueryAsync<T>(Func<T> query,CancellationToken ct) where T:class {
        var completion=new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        await queue.Writer.WriteAsync(new(null,completion,false,Query:()=>query()),ct);
        return (T)await completion.Task.WaitAsync(ct);
    }
    public Task<WatchResult> ConfigureWatchAsync(WatchRequest request,CancellationToken ct=default)=>QueryAsync(()=>eventJournal.Configure(request,AgentFrame(),world.Config.Width,world.Config.Height,world.Config.AntTypes.Select(a=>a.Id),world.Config.Foods.Select(f=>f.Id)),ct);
    public Task<AgentEventPage> ReadEventsAsync(EventReadRequest request,CancellationToken ct=default)=>QueryAsync(()=>eventJournal.Read(request,AgentFrame(),paused),ct);
    public Task<AgentSummary> ObserveSummaryAsync(CancellationToken ct=default)=>QueryAsync(Summary,ct);
    public Task<ProgramPreview> PreviewProgramAsync(PreviewRequest request,CancellationToken ct=default)=>QueryAsync(()=>Preview(request),ct);
    public Task<ProgramBatchPreview> PreviewProgramBatchAsync(PreviewRequest request,CancellationToken ct=default)=>QueryAsync(()=>PreviewBatch(request),ct);
    private AgentSummary Summary() {
        var view=Project(false);var groups=view.Groups.Select(g=>{
            var members=view.Ants.Where(a=>a.GroupId==g.Id).ToArray();
            var receipts=members.Select(a=>new TaskReceipt(a.Id,g.Id,g.Version,a.ReceivedOrder?.Id==g.Id?a.ReceivedOrder.Version:0,a.QueenConnected,
                a.ReceivedOrder?.Id==g.Id && a.ReceivedOrder.Version==g.Version?"current":a.QueenConnected?"awaiting_request":"out_of_range")).ToImmutableArray();
            return new GroupDiagnostics(g.Id,g.Name,g.Version,members.Length,receipts.Count(r=>r.Status=="current"),receipts.Count(r=>r.Status!="current"),receipts,
                members.GroupBy(a=>a.Intent.ToString()).ToImmutableDictionary(x=>x.Key,x=>x.Count()),members.Where(a=>a.ActionStatus==ActionStatus.Failed).GroupBy(a=>a.ActionResult).ToImmutableDictionary(x=>x.Key,x=>x.Count()));
        }).ToImmutableArray();
        var problems=view.Ants.Where(a=>a.LastError is not null || a.ActionStatus==ActionStatus.Failed || a.Health*3<=a.MaxHealth).Select(a=>new AntDiagnostic(a.Id,a.Species,a.GroupId,a.Health,a.MaxHealth,a.Intent.ToString(),a.ActionResult,a.LastError,eventJournal.FailureCount(a.Id))).ToImmutableArray();
        var buildings=view.Buildings.Select(b=>{var d=view.BuildingTypes.Single(x=>x.Id==b.Type);var material=b.ConstructionItem.Length>0?b.ConstructionItem:d.MaterialType;var delivered=b.Store.Get(material);
            return new BuildingDiagnostic(b.Id,b.Type,b.Phase,material,delivered,d.MaterialCost,b.Phase=="blueprint"?Math.Max(0,d.MaterialCost-delivered):0,b.ConstructionWork,d.BuildTicks,b.Recipe,b.RecipeWork,b.Store.Amounts,b.Output.Amounts,b.LastObservedTick);}).ToImmutableArray();
        return new(worldId,revision,world.Tick,paused,speed,view.Queen!.Health,view.Ants.Length,view.AntsLost,view.TechnologyLevel,view.Inventory,view.DevelopmentValue,view.FirstAdmissionPoints,view.ProcessingValueAdded,view.Economy!,
            view.Ants.GroupBy(a=>a.Species).ToImmutableDictionary(g=>g.Key,g=>g.Count()),groups,problems,buildings,view.Enemies,eventJournal.Cursor);
    }
    private ProgramPreview Preview(PreviewRequest request) {
        var ant=world.Ants.FirstOrDefault(a=>a.Species==request.Species && (request.AntId==0 || a.Id==request.AntId))??throw new ArgumentException("No matching living ant.");
        return PreviewAnt(ant,AntDslCompiler.Compile(request.Source));
    }
    private ProgramBatchPreview PreviewBatch(PreviewRequest request) {
        if(request.AntId!=0)throw new ArgumentException("Batch previews cover every living ant of the species; do not supply antId.");
        var ants=world.Ants.Where(a=>a.Species==request.Species).OrderBy(a=>a.Id).ToArray();
        if(ants.Length==0)throw new ArgumentException("No matching living ant.");
        var program=AntDslCompiler.Compile(request.Source);
        var results=ants.Select(a=>new AntProgramPreflight(a.Id,a.GroupId,PreviewAnt(a,program),PreviewAnt(GameRules.PrepareProgramActivation(a,a.ProgramVersion),program))).ToImmutableArray();
        var failed=results.Where(r=>r.Current.Error is not null || r.Activation.Error is not null).Select(r=>r.AntId).ToImmutableArray();
        return new(worldId,revision,request.Species,world.Config.BrainBudgets[world.BrainLevel-1],ants.Length,ants.Length*2,
            results.Max(r=>Math.Max(r.Current.Spent,r.Activation.Spent)),failed.IsEmpty,failed,results,
            ["Coverage: one decision per living ant in its current state and fresh program-activation state at this revision; no future states or physical results are simulated.",
             "Passed means these sampled decisions have no execution error, not proof of future safety, action success or maximum possible cost."]);
    }
    private ProgramPreview PreviewAnt(AntView ant,IAntProgram program) {
        var perception=ProgramRule.Perceive(world,ant);var limit=world.Config.BrainBudgets[world.BrainLevel-1];
        var decision=program.Execute(perception,new(limit));
        var warnings=ImmutableArray.CreateBuilder<string>();
        if(ant.GroupId>0 && ant.ReceivedOrder is null)warnings.Add("No task cached: explicitly request queen information in communication range.");
        if(ant.GroupId>0 && world.Groups.FirstOrDefault(g=>g.Id==ant.GroupId) is {} assigned && ant.ReceivedOrder?.Version!=assigned.Version)warnings.Add("Task cache is not at assigned group version "+assigned.Version+".");
        if(ant.ActionStatus==ActionStatus.Failed)warnings.Add("Last physical action failed: "+ant.ActionResult);
        return new(worldId,revision,ant.Id,ant.Species,limit,decision.Spent,decision.Action.ToString(),decision.Target,decision.Error,decision.Production,decision.RequestQueenInfo,decision.ReportDiscovery,decision.Memory,warnings.ToImmutable()) {EatHolderId=decision.EatHolderId,EatFood=decision.EatFood};
    }
}
