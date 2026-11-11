using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AntMind.Contracts;
namespace AntMind.Transport;

// Shared by the native console window and CLI. No world handle, reflection or shell execution.
public sealed class CommandConsole(Func<WireRequest,CancellationToken,Task<WireResponse>> send) {
    private static readonly JsonSerializerOptions Pretty=new() {WriteIndented=true};
    private static readonly JsonSerializerOptions Input=new() {PropertyNameCaseInsensitive=true,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow};
    public const string Help="""
        help                         Commands and examples
        summary | watches | events [cursor]   Compact diagnostics / monitors / event history
        watch {JSON AgentWatch}      Register a watch; coordinates use fixed-point units
        preflight {JSON PreviewRequest}   Preview every living ant of Species in current/activation states
        status                       Tick, revision, economy and level counts
        ants [species]               All living ants, health, XP, intent and last error
        ant <id> | inspect <kind> <id>   One discovered object; never reveals unknown objects
        groups | catalog             Task data / definitions and progression
        pause | resume | step [1..1000] | speed <1|2|5>
        save <slot> | load <slot>     Full checkpoint (load pauses and rotates WorldId)
        apply <species> <file.ant>    Validate/queue shared species code from a local file
        rollback <species> <version>
        eval <slot> <ticks>           Two isolated continuations, no live mutation
        execute <CommandKind> {JSON options}
          execute SetMinimumReserve {"value":20}
          execute PlanBuild {"key":"workshop","location":{"x":65000,"y":64000}}
        Coordinates: 1000 units/cell. No arbitrary C#, shell, XP injection or map reveal.
        """;
    public async Task<string> ExecuteAsync(string line,CancellationToken ct=default) {
        try {
            if(line.Length>16000)return "error: command_too_long";
            var split=line.Trim().Split(' ',2,StringSplitOptions.RemoveEmptyEntries);
            if(split.Length==0)return "";
            var op=split[0].ToLowerInvariant();var tail=split.Length>1?split[1].Trim():"";
            if(op=="help")return Help;
            if(op=="summary")return Print(await send(new(Protocol.Version,"summary"),ct));
            if(op=="preflight")return Print(await send(new(Protocol.Version,"preview_program_batch",Preview:JsonSerializer.Deserialize<PreviewRequest>(tail,Input)??throw new ArgumentException("PreviewRequest JSON required.")),ct));
            if(op is "events" or "watches" or "watch") {
                var observed=await send(new(Protocol.Version,"summary"),ct);
                if(observed.Summary is not {} overview)return Print(observed);
                if(op=="events")return Print(await send(new(Protocol.Version,"events",Events:new(overview.WorldId,tail.Length==0?0:long.Parse(tail,System.Globalization.CultureInfo.InvariantCulture))),ct));
                var watch=op=="watch"?JsonSerializer.Deserialize<AgentWatch>(tail,Input):null;
                return Print(await send(new(Protocol.Version,"watch",Watch:new(overview.WorldId,op=="watch"?"upsert":"list",watch)),ct));
            }
            if(op=="inspect") {
                var args=Words(tail);if(args.Count!=2)return "usage: inspect kind id";
                return Print(await send(new(Protocol.Version,"inspect_object",ObjectKind:args[0],ObjectId:Number(args[1])),ct));
            }
            if(op=="ant")return await ExecuteAsync("inspect ant "+tail,ct);
            if(op=="eval") {
                var args=Words(tail);if(args.Count!=2)return "usage: eval slot ticks";
                return Print(await send(new(Protocol.Version,"evaluate_save",Evaluation:new(args[0],Number(args[1]))),ct));
            }
            var response=await send(new(Protocol.Version,"observe"),ct);
            if(response.Error is not null || response.Snapshot is not {} view)return Print(response);
            if(op=="status")return Print(new {view.SchemaVersion,view.WorldId,view.Tick,view.Revision,view.Paused,view.Speed,view.Inventory,view.DevelopmentValue,view.TechnologyLevel,view.FirstAdmissionPoints,view.ProcessingValueAdded,
                Levels=view.Ants.GroupBy(a=>a.Level).OrderBy(g=>g.Key).ToDictionary(g=>g.Key,g=>g.Count()),Errors=view.Ants.Where(a=>a.LastError is not null).Select(a=>new {a.Id,a.LastError})});
            if(op=="ants")return Print(view.Ants.Where(a=>tail.Length==0 || a.Species==tail).Select(a=>new {a.Id,a.Species,a.Level,a.Experience,a.CompletedWork,a.Health,a.MaxHealth,a.Position,a.Carry,a.Intent,a.ActionStatus,a.ActionResult,a.LastError}));
            if(op=="groups")return Print(view.Groups);
            if(op=="catalog")return Print(new {view.Species,view.AntLevels,view.FoodTypes,view.TechnologyEras,view.Technologies,view.BuildingTypes,view.Recipes});
            if(op=="step") {
                var count=tail.Length==0?1:Number(tail);if(count is <1 or >1000)return "error: step_count_must_be_1_to_1000";
                if(!view.Paused)return "error: pause_before_step";
                for(var i=0;i<count;i++) {
                    ct.ThrowIfCancellationRequested();
                    var result=await Send(new(Guid.NewGuid(),view.Revision,CommandKind.Step) {ExpectedWorldId=view.WorldId},ct);
                    if(!result.Accepted)return Print(result);
                    view=(await send(new(Protocol.Version,"observe"),ct)).Snapshot??throw new IOException("Missing game snapshot.");
                    if(view.Defeated)break;
                }
                return $"ok: tick={view.Tick}, revision={view.Revision}, levels="+string.Join(",",view.Ants.GroupBy(a=>a.Level).OrderBy(g=>g.Key).Select(g=>$"L{g.Key}:{g.Count()}"));
            }
            var command=new GameCommand(Guid.NewGuid(),view.Revision,CommandKind.Pause,Reason:"local debug console") {ExpectedWorldId=view.WorldId};
            switch(op) {
                case "pause":case "resume":command=command with {Kind=op=="pause"?CommandKind.Pause:CommandKind.Resume};break;
                case "speed":command=command with {Kind=CommandKind.Speed,Value=Number(tail)};break;
                case "save":case "load":command=command with {Kind=op=="save"?CommandKind.SaveGame:CommandKind.LoadGame,Key=tail};break;
                case "apply": {
                    var args=Words(tail);if(args.Count!=2)return "usage: apply species file.ant (quote paths with spaces)";
                    var info=new FileInfo(args[1]);if(!info.Exists || info.Length>32768)return "error: program_file_missing_or_too_large";
                    command=command with {Kind=CommandKind.ApplyProgram,Species=args[0],ProgramSource=await File.ReadAllTextAsync(info.FullName,ct)};break;
                }
                case "rollback": {
                    var args=Words(tail);if(args.Count!=2)return "usage: rollback species version";
                    command=command with {Kind=CommandKind.RollbackProgram,Species=args[0],Value=Number(args[1])};break;
                }
                case "execute": {
                    var args=tail.Split(' ',2,StringSplitOptions.RemoveEmptyEntries);
                    if(args.Length==0 || !Enum.TryParse<CommandKind>(args[0],true,out var kind) || !Enum.IsDefined(kind))return "error: invalid_command_kind";
                    var options=args.Length==2?JsonSerializer.Deserialize<GameCommand>(args[1],Input):null;
                    command=(options??command) with {RequestId=command.RequestId,ExpectedRevision=view.Revision,ExpectedWorldId=view.WorldId,Kind=kind,
                        Reason=string.IsNullOrWhiteSpace(options?.Reason)?"local debug console":options.Reason};break;
                }
                default:return "error: unknown_console_command; type help";
            }
            return Print(await Send(command,ct));
        } catch(OperationCanceledException) when(!ct.IsCancellationRequested) {return "error: game_connection_timeout; start the matching host and check ANTMIND_PIPE";}
        catch(Exception ex) when(ex is ArgumentException or JsonException or IOException or InvalidOperationException or FormatException or OverflowException) {return "error: "+ex.Message;}
    }
    private async Task<CommandResult> Send(GameCommand c,CancellationToken ct) {
        var response=await send(new(Protocol.Version,"command",c),ct);
        return response.Result??throw new IOException(response.Error??"Missing command result.");
    }
    private static int Number(string value)=>int.Parse(value,System.Globalization.CultureInfo.InvariantCulture);
    private static string Print<T>(T value)=>JsonSerializer.Serialize(value,Pretty);
    private static List<string> Words(string text) {
        var result=new List<string>();var token=new StringBuilder();var quoted=false;
        foreach(var c in text) {if(c=='"'){quoted=!quoted;continue;}if(char.IsWhiteSpace(c) && !quoted){if(token.Length>0){result.Add(token.ToString());token.Clear();}}else token.Append(c);}
        if(quoted)throw new FormatException("Unclosed quote.");if(token.Length>0)result.Add(token.ToString());return result;
    }
}
