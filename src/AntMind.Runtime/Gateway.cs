using AntMind.Contracts;
using System.Collections.Immutable;
namespace AntMind.Runtime;
public static class Gateway {
    public static async Task<WireResponse> DispatchAsync(GameSession session, WireRequest request, CancellationToken ct) => request.Method switch {
        "evaluate_save" when request.Evaluation is {} eval=>Evaluate(session,eval,ct),
        "list_saves"=>new(Protocol.Version,Saves:session.ListSaves().Select(s=>new SaveMetadata(s.Slot,s.Bytes,s.ModifiedUtc)).ToImmutableArray()),
        "observe" => new(Protocol.Version, Snapshot: await session.ObserveAsync(ct: ct)),
        "command" when request.Command is not null => new(Protocol.Version, Result: await session.ExecuteAsync(request.Command, ct)),
        "agent_presence" when request.Agent is not null => new(Protocol.Version,
            Error: await session.UpdateAgentPresenceAsync(request.Agent, ct) ? null : "invalid_presence"),
        "inspect_object" => await Inspect(session, request, ct),
        _ => new(Protocol.Version, Error: "unknown_method")
    };
    private static WireResponse Evaluate(GameSession session,EvaluationRequest request,CancellationToken ct) {
        try {return new(Protocol.Version,Evaluation:session.EvaluateSave(request,ct));}
        catch(Exception ex) when(ex is ArgumentException or System.Text.Json.JsonException or IOException or FormatException) {return new(Protocol.Version,Error:"invalid_evaluation: "+ex.Message);}
    }
    private static async Task<WireResponse> Inspect(GameSession session, WireRequest request, CancellationToken ct) {
        var view = await session.ObserveAsync(ct: ct);
        ObjectSnapshot? result = request.ObjectKind switch {
            "ant" when view.Ants.Concat(view.DeadAnts).FirstOrDefault(x => x.Id == request.ObjectId) is { } ant => new("ant", ant.Id, Ant: ant),
            "food" when view.Resources.FirstOrDefault(x => x.Id == request.ObjectId) is { } food => new("food", food.Id, Food: food),
            "enemy" when view.Enemies.FirstOrDefault(x=>x.Id==request.ObjectId) is { } enemy => new("enemy", enemy.Id, Enemy: enemy),
            "source" when view.Sources.FirstOrDefault(x=>x.Id==request.ObjectId) is {} source=>new("source",source.Id,Source:source),
            "building" when view.Buildings.FirstOrDefault(x=>x.Id==request.ObjectId) is {} building=>new("building",building.Id,Building:building),
            "queen" when view.Queen is { } queen && queen.Id == request.ObjectId => new("queen", queen.Id, Queen: queen),
            _ => null
        };
        // Undiscovered and nonexistent IDs are indistinguishable to callers.
        return new(Protocol.Version, Error: result is null ? "object_not_found" : null, Object: result);
    }
}
