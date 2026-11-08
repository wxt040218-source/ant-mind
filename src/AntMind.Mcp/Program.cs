using System.ComponentModel;
using System.Collections.Immutable;
using System.Text.Json;
using AntMind.Contracts;
using AntMind.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddSingleton(new PipeClient(Environment.GetEnvironmentVariable("ANTMIND_PIPE") ?? "antmind-v1"));
builder.Services.AddSingleton<AgentLink>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<AgentLink>());
builder.Services.AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly()
    .WithMessageFilters(filters => filters.AddIncomingFilter(next => async (context, ct) => {
        await next(context, ct);
        if (context.JsonRpcMessage is JsonRpcNotification { Method: "notifications/initialized" } && context.Server.ClientInfo is { } info)
            await context.Services!.GetRequiredService<AgentLink>().InitializeAsync(info.Name, ct);
    }));
await builder.Build().RunAsync();
[McpServerToolType]
public sealed class GameTools(PipeClient client) {
    [McpServerTool(Name = "get_actions"), Description("List every callable game command, including all available simulation buttons.")]
    public string GetActions() => JsonSerializer.Serialize(new {
        production="PlanBuild(key=cache|workshop,pointX/Y,reason) creates a free blueprint. CancelBuild(value=buildingId) retains physical materials. DSL harvest(sourceId,qty), deposit(holderId,itemCode,qty), withdraw, transfer(antId,itemCode,qty), process(station,recipeCode), work(station). Holder 0 is nest. source/building_* read local observations only. Chef uses group station/recipe parameters and objective; all species can work, chef rate 2. get_catalog lists stable definitions, inspect_object also accepts source/building. Inventory excludes nonfood fiber. FirstAdmissionPoints counts new raw food once; Economy reports simulated-minute rates.",
        saves="SaveGame(key=slot,reason), LoadGame(key=slot,reason). Load is paused, restores world/RNG/memory/programs and changes WorldId; ALWAYS pass expectedWorldId from observe to command. list_saves returns metadata only.",
        protocolVersion = Protocol.Version, commands = Enum.GetNames<CommandKind>().Where(x => x != nameof(CommandKind.ClearGroupProgram) && x != nameof(CommandKind.Visibility) && x != nameof(CommandKind.Recruit) && x != nameof(CommandKind.SetQueenRange)),
        network = "UpgradeNetwork(reason): spend food to raise the shared technology level; range and technology unlocks are derived from this level. SetQueenRange is retired. Observe NetworkLevel, NextNetworkRangeCells, NextNetworkUpgradeCost and Technologies.", minimumReserveValues = "0..1000000000", populationCapValues = "current population..100",
        speedValues = new[] {1, 2, 5}, visibilityValues = new[] {"Discovered only; hidden food cannot be revealed"},
        species = new[] {"scout", "hauler", "soldier", "chef"},
        workflow = "observe; command with observed revision and new UUID requestId; identical retries reuse UUID",
        carryUpgrade = "UpgradeCarry(species=hauler,reason); applies to all existing and future haulers; observe species for current capacity and next cost",
        eggs = "ProduceEgg(reason): one generic egg; HatchEgg(species,reason): one egg becomes one ant without another resource charge; population cap applies at hatching",
        program = "ApplyProgram(species,programSource,reason); RollbackProgram(species,value=version,reason). All groups share the species code. Nonzero groupId is rejected for program operations.",
        groups = "CreateGroup and UpdateGroup accept name,task, arbitrary activity label, optional foodFilter, objectiveX/Y and radiusCells. AssignAnt(groupId,antId) or atomic AssignAnts(groupId,antIds); 0 removes membership. SetGroupParameter(groupId,key,value), RemoveGroupParameter(groupId,key), SetGroupPoint(groupId,key,pointX,pointY), RemoveGroupPoint(groupId,key), DeleteGroup(groupId). All require reason. 64 arbitrary integer parameters and 16 named points per group. inspect_group reads group and members. Shared species DSL reads param(quoted_key), has_param, group_x, group_y, has_point from explicitly requested queen cache; no group-specific code. Coordinates use 1000 units/cell.",
        food = "Stock is per-type inventory, FoodTypes defines IDs and heal amounts. Natural food is fruit; carcasses are meat; cargo CarryFood is preserved. eat(food_fruit|food_meat) consumes one portion after 40 uninterrupted ticks; nest stock respects reserve. Follow(id,distance) and claim_food(id,quantity,ttl) are explicit DSL actions; claims are advisory and local.",
        combat = "Soldiers use explicit attack(enemy_id); nearest_enemy_* only senses local enemies. Defeated enemies become movable carcass food. Queen death stops the colony.",
        queen = "request_queen_info() and report_discovery() are explicit DSL actions; queen_* data is a cached reply, updated only after a successful in-range request"
    });
    [McpServerTool(Name="evaluate_save"),Description("Compare two isolated continuations of one save with identical world/RNG. Optional candidate species/source replaces that shared species code. No live world changes; 1..1200 ticks; prefer 1200.")]
    public async Task<string> EvaluateSave(string slot,int ticks,CancellationToken cancellationToken,string species="",string source="")=>JsonSerializer.Serialize(await client.SendAsync(new(Protocol.Version,"evaluate_save",Evaluation:new(slot,ticks,species,source)),cancellationToken));
    [McpServerTool(Name="get_catalog"), Description("Read public item, recipe, building and species definitions, without hidden world locations.")]
    public async Task<string> Catalog(CancellationToken cancellationToken) {var r=await client.SendAsync(new(Protocol.Version,"observe"),cancellationToken);return JsonSerializer.Serialize(new {r.Error,Items=r.Snapshot?.FoodTypes,Recipes=r.Snapshot?.Recipes,Buildings=r.Snapshot?.BuildingTypes,Species=r.Snapshot?.Species});}
    [McpServerTool(Name="list_saves"),Description("List save-slot metadata only; saved hidden objects are never returned.")]
    public async Task<string> Saves(CancellationToken cancellationToken)=>JsonSerializer.Serialize(await client.SendAsync(new(Protocol.Version,"list_saves"),cancellationToken));
    [McpServerTool(Name = "validate_program"), Description("Validate bounded ant DSL without changing the world.")]
    public string ValidateProgram(string source) {
        try { AntMind.Programs.AntDslCompiler.Compile(source); return "{\"valid\":true}"; }
        catch (FormatException error) { return JsonSerializer.Serialize(new { valid = false, error = error.Message }); }
    }
    [McpServerTool(Name = "inspect_object"), Description("Read one independent object record by kind=ant, food, queen, enemy, source or building and ID. Undiscovered food returns object_not_found, exactly like a nonexistent ID.")]
    public async Task<string> InspectObject(string kind, int id, CancellationToken cancellationToken) =>
        JsonSerializer.Serialize(await client.SendAsync(new(Protocol.Version, "inspect_object", ObjectKind: kind, ObjectId: id), cancellationToken));
    [McpServerTool(Name = "inspect_group"), Description("Read a group's task data, arbitrary parameters, named points and living members. Groups have no independent code; all members use species programs.")]
    public async Task<string> InspectGroup(int groupId,CancellationToken cancellationToken) {
        var response=await client.SendAsync(new(Protocol.Version,"observe"),cancellationToken);
        var group=response.Snapshot?.Groups.FirstOrDefault(g=>g.Id==groupId);
        return JsonSerializer.Serialize(new { protocolVersion=Protocol.Version,group,
            members=response.Snapshot?.Ants.Where(a=>a.GroupId==groupId).ToArray(),error=group is null?response.Error??"group_not_found":null });
    }
    [McpServerTool(Name = "observe"), Description("Read the live colony with discovered food only, including ant species definitions, counts, delivery statistics and milestone progress/status/deadlines. Never returns the player's unrestricted map.")]
    public async Task<string> Observe(CancellationToken cancellationToken) =>
        JsonSerializer.Serialize(await client.SendAsync(new(Protocol.Version, "observe"), cancellationToken));
    [McpServerTool(Name = "command"), Description("Apply group management CreateGroup, UpdateGroup, DeleteGroup, AssignAnt, or Pause, Resume, Step, Speed, ProduceEgg, HatchEgg, UpgradeCarry, UpgradeBrain, UpgradeNetwork, ApplyProgram or RollbackProgram. ProduceEgg spends resources to create one generic egg; HatchEgg consumes one egg and selects species=scout, species=hauler or species=soldier. Recruit is retired. ApplyProgram supplies bounded DSL text in programSource; activation waits for the next simulation Tick. RollbackProgram uses value as the target version and also activates at a Tick boundary. Spending and program changes require a reason. Supply the observed revision and a stable UUID requestId; retry with identical fields. Visibility switching is retired: both player and Agent only see discovered food.")]
    public async Task<string> Command(string requestId, long expectedRevision, string kind, int value,
        string reason, CancellationToken cancellationToken, string species = AntSpeciesIds.Scout,
        string programSource = "", int groupId=0, int antId=0, string name="", string task="",
        int? rallyX=null,int? rallyY=null,int? objectiveX=null,int? objectiveY=null,int? retreatX=null,int? retreatY=null, string? activity=null,string? foodFilter=null,int? radiusCells=null, int[]? antIds=null,string key="",int? pointX=null,int? pointY=null,string? expectedWorldId=null) {
        Point? Pair(int? x,int? y) => x.HasValue==y.HasValue ? x.HasValue?new Point(x.Value,y!.Value):null : throw new ArgumentException("Supply both coordinates.");
        if (!Enum.TryParse<CommandKind>(kind, false, out var parsed)) throw new ArgumentException("Unknown command kind.");
        return JsonSerializer.Serialize(await client.SendAsync(
            new(Protocol.Version, "command", new(Guid.Parse(requestId), expectedRevision, parsed, value,
                reason, species, programSource) { ExpectedWorldId=expectedWorldId is null?null:Guid.Parse(expectedWorldId),GroupId=groupId,AntId=antId,AntIds=antIds?.ToImmutableArray()??[],Key=key,Location=Pair(pointX,pointY),Name=name,Task=task,Activity=activity,FoodFilter=foodFilter,RadiusCells=radiusCells,Rally=Pair(rallyX,rallyY),Objective=Pair(objectiveX,objectiveY),Retreat=Pair(retreatX,retreatY) }), cancellationToken));
    }
}
