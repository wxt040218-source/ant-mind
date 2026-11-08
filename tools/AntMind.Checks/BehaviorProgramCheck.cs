using System.Collections.Immutable;
using AntMind.Contracts;
using AntMind.Core;
using AntMind.Programs;
using AntMind.Runtime;

internal static class BehaviorProgramCheck {
    public static void Run(Action<bool, string> check) {
        var scout = AntDslCompiler.Compile(File.ReadAllText("content/programs/scout-explorer.ant"));
        var hauler = AntDslCompiler.Compile(File.ReadAllText("content/programs/hauler-reliable.ant"));
        var nest = Point.FromCells(32, 32);
        var blank = new Perception(6, 1000, Point.FromCells(49,42), nest, 0, Point.FromCells(49,42), 64,64, [],
            AntSpeciesIds.Hauler, 10, 3, AntAction.Wait, Enumerable.Repeat(0,16).ToImmutableArray());
        Perception State(int mode, bool connected = false) => blank with {
            Memory = blank.Memory.SetItem(0, mode).SetItem(1, 1200).SetItem(2, 1000), QueenConnected = connected
        };
        var emptyArrival = hauler.Execute(State(1) with { ActionStatus = ActionStatus.Succeeded,
            QueenResources = [new ResourceView(30, blank.Position, 1, 500)] }, new InstructionBudget(64));
        check(emptyArrival.Error is null && emptyArrival.Target == nest && emptyArrival.Memory[0] == 2 && emptyArrival.Memory[1] == 1201,
            "hauler rejects an arrived empty cached target and commits to returning for fresh information");
        var haulerReturn = hauler.Execute(State(2, true) with { Position = Point.FromCells(47,32) }, new InstructionBudget(64));
        var scoutReturn = scout.Execute(State(1, true) with { Position = Point.FromCells(47,32) }, new InstructionBudget(64));
        check(haulerReturn.Target == nest && haulerReturn.Memory[0] == 2 && scoutReturn.Target == nest && scoutReturn.Memory[0] == 1,
            "both programs continue returning through the network boundary until reaching the nest");
        var staleReply = hauler.Execute(State(3, true) with { QueenInfoTick = 999, QueenRequestStatus = ActionStatus.Succeeded }, new InstructionBudget(64));
        var freshReply = hauler.Execute(State(3, true) with { QueenInfoTick = 1000 }, new InstructionBudget(64));
        check(staleReply.Memory[0] == 3 && staleReply.RequestQueenInfo && freshReply.Memory[0] == 0,
            "hauler waits for a timestamp-confirmed new reply instead of accepting a stale success flag");
        var reported = scout.Execute(State(2, true) with { QueenInfoTick = 1000, QueenReportStatus = ActionStatus.Succeeded }, new InstructionBudget(64));
        check(reported.Memory[0] == 0 && reported.Memory[1] == 1480,
            "scout starts a new exploration window only after a confirmed upload and fresh reply");
        var food = new ResourceView(1, blank.Position, 5, 1000);
        var pickup = hauler.Execute(State(1) with { Nearby = [food], ActionStatus = ActionStatus.Succeeded }, new InstructionBudget(64));
        check(pickup.Action == AntAction.Pickup && pickup.Error is null, "hauler explicitly picks up actual food after arrival");
        var retry = hauler.Execute(State(1) with { Nearby=[food], ActionStatus=ActionStatus.Failed }, new InstructionBudget(64));
        check(retry.Action == AntAction.Pickup && retry.Error is null,
            "hauler retries a cooldown-limited pickup instead of becoming stuck after a failed action");
        var peak = 0;
        foreach (var program in new[] {scout, hauler})
        foreach (var mode in Enumerable.Range(0,5))
        foreach (var connected in new[] {false,true})
        foreach (var cargo in new[] {0,1})
        foreach (var status in Enum.GetValues<ActionStatus>())
        foreach (var count in new[] {0,20}) {
            var nearby = Enumerable.Range(1,count).Select(id=>food with { Id=id }).ToImmutableArray();
            var result = program.Execute(State(mode, connected) with { Nearby=nearby, QueenResources=nearby, Carry=cargo,
                ActionStatus=status, QueenInfoTick=1000, QueenReportStatus=ActionStatus.Succeeded }, new InstructionBudget(64));
            if (result.Error is not null) throw new Exception($"Behavior matrix: {mode}/{connected}/{cargo}/{status}/{count}: {result.Error}");
            peak = Math.Max(peak, result.Spent);
        }
        check(peak <= 64, $"both behavior programs fit the level-one instruction budget across state branches (peak {peak})");
        var simulation = new Simulation([new LocalObservationRule(), new ProgramRule(new Dictionary<string,IAntProgram> {
            [AntSpeciesIds.Scout]=scout, [AntSpeciesIds.Hauler]=hauler
        }), new QueenCommunicationRule(), new MovementRule(), new InteractionRule(), new ResourceRefreshRule(), new ResourceDiscoveryRule()]);
        var world = Simulation.Create(new WorldConfig(), 42);
        var scoutsPickedUp = false;
        for (var tick=0;tick<5000;tick++) {
            world = simulation.Step(world);
            scoutsPickedUp |= world.Ants.Any(x=>x.Species==AntSpeciesIds.Scout && x.Carry>0);
            if (world.Ants.Any(x=>x.LastError is not null)) throw new Exception("Behavior simulation produced a program error.");
        }
        check(!scoutsPickedUp && world.DeliveredBySpecies.GetValueOrDefault(AntSpeciesIds.Scout)==0 && world.DeliveredBySpecies.GetValueOrDefault(AntSpeciesIds.Hauler)>0,
            "5000 ticks keep scouts on reconnaissance while haulers deliver food without program errors");
    }
}
