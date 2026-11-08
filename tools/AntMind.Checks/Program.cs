using System.Collections.Immutable;
using System.Text.Json;
using AntMind.Contracts;
using AntMind.Core;
using AntMind.Programs;
using AntMind.Runtime;
using AntMind.Transport;
int checks = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks++; Console.WriteLine("PASS " + label); }
await using var session = new GameSession(new WorldConfig(), 42);
var start = await session.ObserveAsync(true);
Check(start.Species.Length == 3 && start.Species.Single(x => x.Id == AntSpeciesIds.Scout).Count == 5 &&
    start.Species.Single(x => x.Id == AntSpeciesIds.Hauler).Count == 5,
    "initial colony contains the configured scout and hauler populations");
Check(start.Species.Single(x => x.Id == AntSpeciesIds.Scout).CarryCapacity == 1 &&
    start.Species.Single(x => x.Id == AntSpeciesIds.Hauler).CarryCapacity == 1,
    "species snapshot exposes different configured body limits");
await using (var extendedSession = new GameSession(new WorldConfig(InitialResourceNodes: 0), 43,
    [new ExampleRule()])) {
    var beforeExtension = await extendedSession.ObserveAsync();
    await extendedSession.ExecuteAsync(new(Guid.NewGuid(), beforeExtension.Revision, CommandKind.Step));
    var afterExtension = await extendedSession.ObserveAsync();
    Check(afterExtension.Tick == 1 && afterExtension.Ants[0].Carry == 1,
        "runtime adds custom rules after the built-in simulation rules");
}
await using (var controlsSession = new GameSession(new WorldConfig(), 91)) {
    foreach (var setting in new[] { (CommandKind.SetMinimumReserve, 25), (CommandKind.SetPopulationCap, 12) }) {
        var state = await controlsSession.ObserveAsync();
        Check((await controlsSession.ExecuteAsync(new(Guid.NewGuid(), state.Revision, setting.Item1, setting.Item2))).Accepted,
            "Agent management command accepted: " + setting.Item1);
    }
    var stateAfter = await controlsSession.ObserveAsync();
    Check(!(await controlsSession.ExecuteAsync(new(Guid.NewGuid(), stateAfter.Revision, CommandKind.SetQueenRange, 1024))).Accepted &&
        (await controlsSession.ObserveAsync()).QueenRangeCells == 16, "retired range command cannot bypass paid network upgrades");
}
var command = new GameCommand(Guid.NewGuid(), start.Revision, CommandKind.Step);
var first = await session.ExecuteAsync(command);
Check(first.Accepted, "step accepted");
Check(await session.ExecuteAsync(command) == first, "idempotent receipt");
Check((await session.ObserveAsync()).Tick == 1, "duplicate does not advance");
Check(!(await session.ExecuteAsync(command with { Value = 99 })).Accepted, "request ID cannot change payload");
Check(!(await session.ExecuteAsync(new(Guid.NewGuid(), 0, CommandKind.Resume))).Accepted, "stale write rejected");
var filtered = await session.ObserveAsync();
var visionBySpecies = start.Species.ToDictionary(x => x.Id, x => x.VisionCells, StringComparer.Ordinal);
var visibleIds = start.Resources.Where(r => start.Ants.Any(a =>
    Math.Abs((long)a.Position.X-r.Position.X) <= (long)visionBySpecies[a.Species] * Point.UnitsPerCell &&
    Math.Abs((long)a.Position.Y-r.Position.Y) <= (long)visionBySpecies[a.Species] * Point.UnitsPerCell))
    .Select(r=>r.Id).Order().ToArray();
Check(filtered.Resources.Select(r=>r.Id).Order().SequenceEqual(visibleIds), "projection exactly excludes unseen resources");
var queenConfig = new WorldConfig(InitialResourceNodes: 0) { NetworkRanges = [4, 8, 16, 24, 32] };
var boundaryWorld = Simulation.Create(queenConfig, 44);
Check(QueenCommunicationRule.Connected(boundaryWorld, Point.FromCells(36,32)) &&
    !QueenCommunicationRule.Connected(boundaryWorld, new Point(36001,32000)), "queen circle includes exact boundary and excludes points beyond it");
var queenWorld = Simulation.Create(queenConfig, 44) with {
    Tick = 1, Ants = [new AntView(1, Point.FromCells(42,32), 0)],
    Resources = [new ResourceView(1, Point.FromCells(42,32), 10, 1)]
};
var queenRule = new QueenCommunicationRule();
var observer = new LocalObservationRule();
queenWorld = observer.Apply(queenWorld);
Check(queenWorld.QueenResources.IsEmpty && queenWorld.Ants[0].ResourceReports.Length == 1,
    "passive sightings stay private outside queen range");
queenWorld = queenRule.Apply(queenWorld with { Tick = 2, Ants = [queenWorld.Ants[0] with { Position = Point.FromCells(32,32) }] });
Check(queenWorld.QueenResources.IsEmpty && queenWorld.Ants[0].QueenKnowledge.IsEmpty,
    "entering range does not automatically upload or download");
queenWorld = queenRule.Apply(queenWorld with { Tick = 3, Ants = [queenWorld.Ants[0] with { ReportDiscovery = true, RequestQueenInfo = true }] });
Check(queenWorld.QueenResources.Length == 1 && queenWorld.Ants[0].QueenKnowledge.Length == 1,
    "explicit upload and request settle before next decision");
var queenProbe = new ProbeProgram();
new ProgramRule(queenProbe).Apply(queenWorld);
Check(queenProbe.Perception!.QueenResources.Length == 1 && queenProbe.Perception.QueenInfoTick == 3,
    "program reads only its requested cached queen reply");
var disconnectedWorld = queenRule.Apply(queenWorld with { Tick = 20,
    QueenResources = [new ResourceView(2, Point.FromCells(30,30), 99, 20)],
    Ants = [queenWorld.Ants[0] with { Position = Point.FromCells(42,32), RequestQueenInfo = true }] });
Check(disconnectedWorld.Ants[0].QueenKnowledge[0].Id == 1 && disconnectedWorld.Ants[0].QueenRequestStatus == ActionStatus.Failed,
    "out-of-range request fails and retains old cache without leaking new data");
var cooldownWorld = queenRule.Apply(queenWorld with { Tick = 4,
    Ants = [queenWorld.Ants[0] with { RequestQueenInfo = true }] });
Check(cooldownWorld.Ants[0].QueenInfoTick == 3 && cooldownWorld.Ants[0].QueenRequestStatus == ActionStatus.Failed,
    "queen request cooldown cannot be bypassed");
var queenDsl = AntDslCompiler.Compile("request_queen_info(); report_discovery(); memory[0] = queen_amount_at(0);");
var queenSense = new Perception(1, 2, Point.FromCells(32,32), Point.FromCells(32,32), 0, null, 64,64, [],
    QueenConnected: true, QueenResources: queenWorld.QueenResources);
var queenDecision = queenDsl.Execute(queenSense, new InstructionBudget(64));
Check(queenDecision.RequestQueenInfo && queenDecision.ReportDiscovery && queenDecision.Memory[0] == 10,
    "DSL emits explicit communication requests and can read prior cache");
var rejectedTrail = false;
try { AntDslCompiler.Compile("release(30);"); } catch (FormatException) { rejectedTrail = true; }
Check(rejectedTrail, "removed pheromone actions are rejected by DSL validation");
var a = new Simulation([]); var w1 = Simulation.Create(new(), 123); var w2 = Simulation.Create(new(), 123);
for (int i=0;i<500;i++) { w1=a.Step(w1); w2=a.Step(w2); }
Check(JsonSerializer.Serialize(w1) == JsonSerializer.Serialize(w2), "deterministic seed and tick replay");
var extended = new Simulation([new ExampleRule()]);
Check(extended.Step(w1).Ants[0].Carry == 1, "external rule participates");
var old = await session.ObserveAsync(true);
await session.ExecuteAsync(new(Guid.NewGuid(), old.Revision, CommandKind.Step));
Check(old.Tick == 1, "published snapshot remains unchanged");
var budget = new InstructionBudget(4);
Check(budget.TrySpend(4) && !budget.TrySpend(1) && budget.Remaining == 0, "instruction budget cannot overdraw");
var bounded = AntDslCompiler.Compile("let flag = carry == 0; if (flag) { memory[0] = memory[0] + 1; } else { wait(); }");
var dslPerception = new Perception(1, 1, Point.FromCells(10, 10), Point.FromCells(32, 32), 0,
    null, 64, 64, [], AntSpeciesIds.Scout, 1, 6,
    Memory: Enumerable.Repeat(0, AntDslCompiler.MemorySlots).ToImmutableArray());
var dslDecision = bounded.Execute(dslPerception, new InstructionBudget(64));
Check(dslDecision.Error is null && dslDecision.Memory[0] == 1 && dslDecision.Action == AntAction.Wait,
    "restricted DSL evaluates conditions and persists individual memory");
var heavyDsl = AntDslCompiler.Compile("repeat 16 { memory[0] = memory[0] + 1; } wait();");
var boundedDecision = heavyDsl.Execute(dslPerception, new InstructionBudget(64));
Check(boundedDecision.Error == "instruction_budget_exceeded" && boundedDecision.Spent == 64,
    "DSL loops stop at the exact deterministic instruction budget");
var invalidDslRejected = false;
try { AntDslCompiler.Compile("repeat 100 { wait(); }"); }
catch (FormatException) { invalidDslRejected = true; }
Check(invalidDslRejected, "DSL rejects unbounded or excessive loops during validation");
var deepDslRejected = false;
try { AntDslCompiler.Compile($"if ({new string('!', 1000)}1) {{ wait(); }}"); }
catch (FormatException) { deepDslRejected = true; }
Check(deepDslRejected, "DSL rejects excessive expression nesting before execution");
var sensing = Simulation.Create(new WorldConfig(InitialResourceNodes: 0), 9) with {
    Tick = 1,
    Ants = [
        new AntView(1, Point.FromCells(32, 32), 0, Species: AntSpeciesIds.Scout),
        new AntView(2, Point.FromCells(32, 32), 0, Species: AntSpeciesIds.Hauler)
    ],
    Resources = [new ResourceView(1, Point.FromCells(37, 32), 10, 1)]
};
var scoutProbe = new ProbeProgram(); var haulerProbe = new ProbeProgram();
var probed = new ProgramRule(new Dictionary<string, IAntProgram>(StringComparer.Ordinal) {
    [AntSpeciesIds.Scout] = scoutProbe, [AntSpeciesIds.Hauler] = haulerProbe
}).Apply(sensing);
Check(scoutProbe.Perception?.Nearby.Length == 1 && haulerProbe.Perception?.Nearby.Length == 0,
    "program perception uses each ant species vision radius");
Check(probed.Ants[0].Intent == AntAction.Wait && probed.Ants[1].Intent == AntAction.Wait,
    "program dispatch is resolved by species slot");
var moving = sensing with { Resources = [], Ants = [
    new AntView(1, Point.FromCells(10, 10), 0, Point.FromCells(20, 10), AntAction.Move, Species: AntSpeciesIds.Scout),
    new AntView(2, Point.FromCells(10, 10), 0, Point.FromCells(20, 10), AntAction.Move, Species: AntSpeciesIds.Hauler)
] };
var movedEmpty = new MovementRule().Apply(moving);
var scoutPosition = movedEmpty.Ants.Single(x => x.Species == AntSpeciesIds.Scout).Position;
var haulerPosition = movedEmpty.Ants.Single(x => x.Species == AntSpeciesIds.Hauler).Position;
Check(scoutPosition.X - Point.FromCells(10, 10).X == 200 &&
    haulerPosition.X - Point.FromCells(10, 10).X == 120,
    "empty movement speed is species-specific");
var movingLoaded = moving with { HaulerCarryLevel = 2, Ants = [
    moving.Ants[0] with { Carry = 1 }, moving.Ants[1] with { Carry = 2 }
] };
var movedLoaded = new MovementRule().Apply(movingLoaded);
Check(movedLoaded.Ants.Single(x => x.Species == AntSpeciesIds.Scout).Position.X - Point.FromCells(10, 10).X == 20 &&
    movedLoaded.Ants.Single(x => x.Species == AntSpeciesIds.Hauler).Position.X - Point.FromCells(10, 10).X == 108,
    "linear load penalty reduces each species movement speed");
// Every legal payload gets slower; unlocking capacity cannot accelerate the same payload.
var payloadSpeeds = Enumerable.Range(0, 11).Select(load => {
    var world = moving with { HaulerCarryLevel = 10, Ants = [moving.Ants[1] with { Carry = load }] };
    return new MovementRule().Apply(world).Ants[0].Position.X - moving.Ants[1].Position.X;
}).ToArray();
Check(payloadSpeeds.SequenceEqual(new[] {120,114,108,102,96,90,84,78,72,66,60}),
    "all ten payloads slow haulers monotonically and remain faster than loaded scouts");
var oneFood = moving with { Ants = [moving.Ants[1] with { Carry = 1 }] };
Check(new MovementRule().Apply(oneFood).Ants[0].Position ==
    new MovementRule().Apply(oneFood with { HaulerCarryLevel = 10 }).Ants[0].Position,
    "capacity upgrades do not change speed for the same actual payload");
var pipeName = "antmind-check-" + Guid.NewGuid().ToString("N");
using var stop = new CancellationTokenSource();
var server = new PipeServer(pipeName, (r, ct) => Gateway.DispatchAsync(session, r, ct)).RunAsync(stop.Token);
var remote = await new PipeClient(pipeName).SendAsync(new(Protocol.Version, "observe"));
Check(remote.Snapshot?.Tick == 2, "live IPC observes shared runtime");
var invalid = await new PipeClient(pipeName).SendAsync(new(99, "observe"));
Check(invalid.Error == "protocol_version", "wire version rejected");
var forbidden = await new PipeClient(pipeName).SendAsync(new(Protocol.Version, "command",
    new(Guid.NewGuid(), remote.Snapshot!.Revision, CommandKind.Visibility, 1)));
Check(forbidden.Result?.Accepted == false && forbidden.Result.Code == "discovery_only", "Agent cannot bypass discovery through the old visibility API");
stop.Cancel(); await server;
using var oversized = new MemoryStream(new byte[] { 255, 255, 255, 127 });
try { await Frames.ReadAsync<WireRequest>(oversized, default); Check(false, "frame cap"); }
catch (InvalidDataException) { Check(true, "oversized frame rejected before allocation"); }
var current = await session.ObserveAsync();
var repeated = new GameCommand(Guid.NewGuid(), current.Revision, CommandKind.Step);
var replies = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => session.ExecuteAsync(repeated)));
Check(replies.All(r => r == replies[0]) && (await session.ObserveAsync()).Tick == current.Tick+1, "concurrent duplicate writes commit once");
var beforeInvalid = await session.ObserveAsync();
Check(!(await session.ExecuteAsync(new(Guid.NewGuid(), beforeInvalid.Revision, CommandKind.Speed, 3))).Accepted && (await session.ObserveAsync()).Revision == beforeInvalid.Revision, "invalid speed does not mutate revision");
var harvesting = new Simulation([new ProgramRule(new DefaultForager()), new MovementRule(),
    new InteractionRule(), new LocalObservationRule(), new QueenCommunicationRule(), new ResourceRefreshRule()]);
var harvestWorld = Simulation.Create(new WorldConfig(), 42);
for (int i = 0; i < 5000; i++) harvestWorld = harvesting.Step(harvestWorld);
Check(harvestWorld.Delivered > 0 && harvestWorld.Inventory == 100 + harvestWorld.Delivered,
    "default colony finds, picks up and delivers food");
Check(harvestWorld.Generated == harvestWorld.Delivered + harvestWorld.Ants.Sum(a => a.Carry) +
    harvestWorld.Resources.Sum(r => r.Amount), "generated food is conserved");
Check(harvestWorld.DeliveredBySpecies.Values.Sum() == harvestWorld.Delivered &&
    harvestWorld.DeliveredBySpecies.GetValueOrDefault(AntSpeciesIds.Scout) == 0 &&
    harvestWorld.DeliveredBySpecies.GetValueOrDefault(AntSpeciesIds.Hauler) > 0,
    "default scouts explore while haulers account for delivered food");
Check(harvestWorld.Resources.Length <= 20 && harvestWorld.Resources.Sum(r => r.Amount) <= 1200 &&
    harvestWorld.Ants.All(a => a.Carry >= 0 && a.Carry <= harvestWorld.Config.AntType(a.Species).CarryCapacity),
    "resource and per-species carrying caps hold");
var configured = ContentLoader.LoadDefault();
Check(configured.BrainBudgets.SequenceEqual(new[] { 64, 128, 256, 512, 1024 }) &&
    configured.ResourceRefreshTicks == 300 && configured.AntTypes.Length == 4 && configured.Production.Enabled && configured.AntTypes.Single(x=>x.Id==AntSpeciesIds.Chef).WorkRate==2,
    "versioned content loads shared and per-species prototype values");
await using (var programmable = new GameSession(new WorldConfig(InitialResourceNodes: 0), 17)) {
    var initial = await programmable.ObserveAsync();
    var source = "memory[0] = memory[0] + 1; repeat 2 { memory[1] = memory[1] + 1; } wait();";
    var submitted = await programmable.ExecuteAsync(new(Guid.NewGuid(), initial.Revision,
        CommandKind.ApplyProgram, Reason: "verify species program slot", Species: AntSpeciesIds.Scout,
        ProgramSource: source));
    var pending = await programmable.ObserveAsync();
    Check(submitted.Accepted && pending.Programs.Single(x => x.Species == AntSpeciesIds.Scout).ActiveVersion == 0 &&
        pending.Programs.Single(x => x.Species == AntSpeciesIds.Scout).PendingVersion == 1,
        "valid program is staged while the old version remains active");
    await programmable.ExecuteAsync(new(Guid.NewGuid(), pending.Revision, CommandKind.Step));
    var applied = await programmable.ObserveAsync();
    Check(applied.Programs.Single(x => x.Species == AntSpeciesIds.Scout).ActiveVersion == 1 &&
        applied.Ants.Where(x => x.Species == AntSpeciesIds.Scout).All(x => x.ProgramVersion == 1 &&
            x.Memory[0] == 1 && x.Memory[1] == 2) &&
        applied.Ants.Where(x => x.Species == AntSpeciesIds.Hauler).All(x => x.ProgramVersion == 0),
        "program update activates at a Tick boundary and isolates species memory and versions");
    var invalidProgram = await programmable.ExecuteAsync(new(Guid.NewGuid(), applied.Revision,
        CommandKind.ApplyProgram, Reason: "reject invalid syntax", Species: AntSpeciesIds.Scout,
        ProgramSource: "run_csharp(1);"));
    Check(!invalidProgram.Accepted && invalidProgram.Code == "invalid_program" &&
        (await programmable.ObserveAsync()).Programs.Single(x => x.Species == AntSpeciesIds.Scout).ActiveVersion == 1,
        "invalid source is rejected without replacing the active program");
    var rollback = await programmable.ObserveAsync();
    var rollbackQueued = await programmable.ExecuteAsync(new(Guid.NewGuid(), rollback.Revision,
        CommandKind.RollbackProgram, Value: 0, Reason: "restore built-in scout", Species: AntSpeciesIds.Scout));
    var rollbackPending = await programmable.ObserveAsync();
    Check(rollbackQueued.Accepted && rollbackPending.Programs.Single(x => x.Species == AntSpeciesIds.Scout).ActiveVersion == 1,
        "rollback waits for a simulation boundary");
    await programmable.ExecuteAsync(new(Guid.NewGuid(), rollbackPending.Revision, CommandKind.Step));
    var rolledBack = await programmable.ObserveAsync();
    var scoutPrograms = rolledBack.Programs.Single(x => x.Species == AntSpeciesIds.Scout);
    Check(scoutPrograms.ActiveVersion == 2 && scoutPrograms.History.Length == 3 &&
        scoutPrograms.ActiveSource == DefaultProgramSources.Scout &&
        scoutPrograms.History[2].Reason.Contains("rollback to v0", StringComparison.Ordinal),
        "rollback creates an auditable new version using the selected prior source");
    for (var i = 0; i < 240; i++) {
        var view = await programmable.ObserveAsync();
        await programmable.ExecuteAsync(new(Guid.NewGuid(), view.Revision, CommandKind.Step));
    }
    var logged = await programmable.ObserveAsync();
    Check(logged.Ants.Where(x => x.Species == AntSpeciesIds.Scout).All(x => x.RecentDecisions.Length == 20),
        "each ant retains a bounded window of its last 20 decisions");
}
var replay = Simulation.Create(new WorldConfig(), 42);
for (int i = 0; i < 5000; i++) replay = harvesting.Step(replay);
Check(replay.RandomState == harvestWorld.RandomState && replay.Delivered == harvestWorld.Delivered &&
    JsonSerializer.Serialize(replay.Ants) == JsonSerializer.Serialize(harvestWorld.Ants) &&
    JsonSerializer.Serialize(replay.Resources) == JsonSerializer.Serialize(harvestWorld.Resources) &&
    JsonSerializer.Serialize(replay.QueenResources) == JsonSerializer.Serialize(harvestWorld.QueenResources), "harvesting replay is deterministic");
await using (var eggs = new GameSession(new WorldConfig(InitialStock: 500, PopulationCap: 11, MinimumReserve: 400), 42)) {
    async Task<CommandResult> EggCommand(CommandKind kind, string species = AntSpeciesIds.Scout) {
        var state = await eggs.ObserveAsync();
        return await eggs.ExecuteAsync(new(Guid.NewGuid(), state.Revision, kind, Reason: "egg lifecycle check", Species: species));
    }
    var initial = await eggs.ObserveAsync();
    Check(!(await EggCommand(CommandKind.HatchEgg)).Accepted && (await eggs.ObserveAsync()).Revision == initial.Revision,
        "hatching without eggs is rejected without mutation");
    var produce = new GameCommand(Guid.NewGuid(), initial.Revision, CommandKind.ProduceEgg, Reason: "produce generic egg");
    var produced = await eggs.ExecuteAsync(produce);
    Check(await eggs.ExecuteAsync(produce) == produced, "egg production retry does not spend twice");
    var eggView = await eggs.ObserveAsync();
    Check(eggView.Eggs == 1 && eggView.EggsProduced == 1 && eggView.Inventory == 480 && eggView.Ants.Length == 10 && eggView.RecruitmentSpent == 20,
        "queen produces generic egg and charges resources without spawning an ant");
    Check(!(await EggCommand(CommandKind.HatchEgg, "unknown")).Accepted && (await eggs.ObserveAsync()).Eggs == 1,
        "invalid species does not consume an egg");
    Check((await EggCommand(CommandKind.HatchEgg, AntSpeciesIds.Hauler)).Accepted, "generic egg can hatch into selected hauler species");
    var hatched = await eggs.ObserveAsync();
    Check(hatched.Eggs == 0 && hatched.EggsHatched == 1 && hatched.Ants.Length == 11 && hatched.Inventory == 480 && hatched.Species.Single(x => x.Id == AntSpeciesIds.Hauler).Count == 6,
        "hatching consumes exactly one egg with no second resource charge");
    await EggCommand(CommandKind.ProduceEgg);
    var beforeCap = await eggs.ObserveAsync();
    Check(!(await EggCommand(CommandKind.HatchEgg)).Accepted && (await eggs.ObserveAsync()).Eggs == beforeCap.Eggs,
        "population cap preserves unhatched eggs");
    Check(!(await EggCommand(CommandKind.UpgradeBrain)).Accepted, "minimum reserve still blocks upgrade");
    Check(!(await EggCommand(CommandKind.Recruit)).Accepted, "retired recruit command cannot bypass eggs");
}
await using (var reserveEggs = new GameSession(new WorldConfig(InitialStock: 50, MinimumReserve: 40), 42)) {
    var initial = await reserveEggs.ObserveAsync();
    var rejected = await reserveEggs.ExecuteAsync(new(Guid.NewGuid(), initial.Revision, CommandKind.ProduceEgg, Reason: "reserve check"));
    var after = await reserveEggs.ObserveAsync();
    Check(!rejected.Accepted && after.Inventory == 50 && after.Eggs == 0 && after.Revision == initial.Revision,
        "egg production respects minimum reserve atomically");
}
await using (var scoutEggs = new GameSession(new WorldConfig(), 42)) {
    var initial = await scoutEggs.ObserveAsync();
    await scoutEggs.ExecuteAsync(new(Guid.NewGuid(), initial.Revision, CommandKind.ProduceEgg, Reason: "scout egg"));
    var produced = await scoutEggs.ObserveAsync();
    var hatch = new GameCommand(Guid.NewGuid(), produced.Revision, CommandKind.HatchEgg, Reason: "scout hatch", Species: AntSpeciesIds.Scout);
    var results = await Task.WhenAll(scoutEggs.ExecuteAsync(hatch), scoutEggs.ExecuteAsync(hatch));
    var after = await scoutEggs.ObserveAsync();
    Check(results[0] == results[1] && after.Eggs == 0 && after.EggsProduced == after.Eggs + after.EggsHatched && after.Species.Single(x => x.Id == AntSpeciesIds.Scout).Count == 6,
        "one generic egg hatches into scout once under duplicate concurrent requests");
}
await using (var economic = new GameSession(new WorldConfig(InitialStock: 500), 42)) {
    var initial = await economic.ObserveAsync();
    var upgraded = await economic.ExecuteAsync(new(Guid.NewGuid(), initial.Revision,
        CommandKind.UpgradeBrain, Reason: "increase decision budget"));
    var upgradedView = await economic.ObserveAsync();
    Check(upgraded.Accepted && upgradedView.BrainLevel == 2 && upgradedView.Inventory == 400 &&
        upgradedView.UpgradeSpent == 100, "brain upgrade spends stock atomically");
}
// Drop/relay cycles preserve food; only explicit final delivery increments score.
var relay = Simulation.Create(new WorldConfig(InitialResourceNodes: 0), 101) with {
    Generated = 4, Tick = 1, HaulerCarryLevel = 4,
    Ants = [new AntView(1, Point.FromCells(10,10), 4, Intent: AntAction.Drop, Species: AntSpeciesIds.Hauler)]
};
var interaction = new InteractionRule();
relay = interaction.Apply(relay);
Check(relay.Resources.Sum(x => x.Amount) == 4 && relay.Delivered == 0 && relay.Generated == 4 && relay.Ants[0].Carry == 0,
    "explicit drop preserves food and never scores");
relay = interaction.Apply(relay with { Tick = 11, Ants = [relay.Ants[0] with { Intent = AntAction.Pickup }] });
Check(relay.Ants[0].Carry == 4 && relay.Resources.IsEmpty && relay.Delivered == 0,
    "pickup of dropped food does not generate new score");
var settled = interaction.Apply(relay with { Tick = 21 });
Check(settled.Ants[0].Carry == 4 && settled.Ants[0].Intent == AntAction.Wait,
    "one interaction request does not repeat itself");
var remoteDelivery = interaction.Apply(relay with { Tick = 21, Ants = [relay.Ants[0] with { Intent = AntAction.Deliver }] });
Check(remoteDelivery.Delivered == 0 && remoteDelivery.Ants[0].ActionResult == "not_at_nest",
    "delivery does not automatically move to the nest");
relay = interaction.Apply(relay with { Tick = 21, Ants = [relay.Ants[0] with { Position = Point.FromCells(32,32), Intent = AntAction.Deliver }] });
Check(relay.Delivered == 4 && relay.Generated == relay.Delivered + relay.Ants.Sum(x => x.Carry) + relay.Resources.Sum(x => x.Amount),
    "final explicit delivery scores food once and preserves conservation");
await using (var carrying = new GameSession(new WorldConfig(InitialStock: 5000), 42)) {
    var before = await carrying.ObserveAsync();
    var upgrade = new GameCommand(Guid.NewGuid(), before.Revision, CommandKind.UpgradeCarry, Reason: "haul larger batches", Species: AntSpeciesIds.Hauler);
    var accepted = await carrying.ExecuteAsync(upgrade);
    Check(accepted.Accepted && await carrying.ExecuteAsync(upgrade) == accepted, "carry upgrade is idempotent");
    var upgraded = await carrying.ObserveAsync();
    Check(upgraded.Species.Single(x => x.Id == AntSpeciesIds.Hauler).CarryCapacity == 2 &&
        upgraded.Species.Single(x => x.Id == AntSpeciesIds.Scout).CarryCapacity == 1 && upgraded.Inventory == 4920 && upgraded.CarryUpgradeSpent == 80,
        "carry upgrade increases haulers only and spends configured cost");
    var invalidSpecies = await carrying.ExecuteAsync(new(Guid.NewGuid(), upgraded.Revision, CommandKind.UpgradeCarry, Reason: "wrong body", Species: AntSpeciesIds.Scout));
    Check(!invalidSpecies.Accepted, "scout cannot buy hauler carry upgrade");
    for (var level = 0; level < 8; level++) {
        var currentCarry = await carrying.ObserveAsync();
        await carrying.ExecuteAsync(new(Guid.NewGuid(), currentCarry.Revision, CommandKind.UpgradeCarry, Reason: "next carry level", Species: AntSpeciesIds.Hauler));
    }
    var maxed = await carrying.ObserveAsync();
    Check(maxed.Species.Single(x => x.Id == AntSpeciesIds.Hauler).CarryCapacity == 10 &&
        !(await carrying.ExecuteAsync(new(Guid.NewGuid(), maxed.Revision, CommandKind.UpgradeCarry, Reason: "max level", Species: AntSpeciesIds.Hauler))).Accepted,
        "carry progression reaches configured maximum and rejects further spending");
}
await using (var reserveCarry = new GameSession(new WorldConfig(), 42)) {
    var state = await reserveCarry.ObserveAsync();
    var result = await reserveCarry.ExecuteAsync(new(Guid.NewGuid(), state.Revision, CommandKind.UpgradeCarry, Reason: "reserve boundary", Species: AntSpeciesIds.Hauler));
    var after = await reserveCarry.ObserveAsync();
    Check(!result.Accepted && after.Inventory == state.Inventory && after.Revision == state.Revision && after.Species.Single(x => x.Id == AntSpeciesIds.Hauler).CarryCapacity == 1,
        "carry upgrade cannot spend below reserve or mutate rejected state");
}
var upgradedPickup = interaction.Apply(Simulation.Create(new WorldConfig(InitialResourceNodes: 0), 1) with {
    Tick = 1, HaulerCarryLevel = 2, Generated = 20,
    Ants = [new AntView(1, Point.FromCells(10,10), 0, Intent: AntAction.Pickup, Species: AntSpeciesIds.Hauler)],
    Resources = [new ResourceView(1, Point.FromCells(10,10), 20, 1)]
});
Check(upgradedPickup.Ants[0].Carry == 2 && upgradedPickup.Resources[0].Amount == 18,
    "upgraded capacity affects actual pickup batch size");
await using (var network = new GameSession(new WorldConfig(InitialStock: 3000) {
    Technologies = [new("queen.communication", "蚁后信息收发", 1), new("test.high-tech", "测试专用高科技", 3)]
}, 42)) {
    var before = await network.ObserveAsync();
    Check(before.NetworkLevel == 1 && before.QueenRangeCells == 16 && !before.Technologies.Single(x => x.Id == "test.high-tech").Unlocked,
        "network starts at level one with future technology locked");
    var purchase = new GameCommand(Guid.NewGuid(), before.Revision, CommandKind.UpgradeNetwork, Reason: "expand colony network");
    Check((await network.ExecuteAsync(purchase)).Accepted && (await network.ExecuteAsync(purchase)).Accepted,
        "network upgrade supports identical request retries");
    var after = await network.ObserveAsync();
    Check(after.NetworkLevel == 2 && after.QueenRangeCells == 24 && after.Inventory == 2940 && after.NetworkUpgradeSpent == 60 && after.Revision == before.Revision + 1,
        "network upgrade charges exactly once and exposes the new range immediately");
    Check(!after.Technologies.Single(x => x.Id == "test.high-tech").Unlocked,
        "technology remains locked below its configured network requirement");
    await network.ExecuteAsync(new(Guid.NewGuid(), after.Revision, CommandKind.UpgradeNetwork, Reason: "unlock test technology"));
    var unlocked = await network.ObserveAsync();
    Check(unlocked.NetworkLevel == 3 && unlocked.Technologies.Single(x => x.Id == "test.high-tech").Unlocked,
        "technology unlocks exactly at its required network level");
    while ((await network.ObserveAsync()).NextNetworkUpgradeCost > 0) {
        var networkState = await network.ObserveAsync();
        Check((await network.ExecuteAsync(new(Guid.NewGuid(), networkState.Revision, CommandKind.UpgradeNetwork, Reason: "max network"))).Accepted,
            "configured network level can be purchased");
    }
    var maxed = await network.ObserveAsync();
    Check(maxed.NetworkLevel == 5 && maxed.QueenRangeCells == 64 && maxed.NextNetworkRangeCells == 0 &&
        !(await network.ExecuteAsync(new(Guid.NewGuid(), maxed.Revision, CommandKind.UpgradeNetwork, Reason: "maximum"))).Accepted &&
        (await network.ObserveAsync()).Inventory == maxed.Inventory, "maximum network level rejects further charges");
}
await using (var poorNetwork = new GameSession(new WorldConfig(InitialStock: 99), 42)) {
    var before = await poorNetwork.ObserveAsync();
    var result = await poorNetwork.ExecuteAsync(new(Guid.NewGuid(), before.Revision, CommandKind.UpgradeNetwork, Reason: "reserve constraint"));
    var after = await poorNetwork.ObserveAsync();
    Check(!result.Accepted && after.NetworkLevel == 1 && after.Inventory == 99 && after.Revision == before.Revision,
        "network spending respects minimum reserve without partial mutation");
    Check(!(await poorNetwork.ExecuteAsync(new(Guid.NewGuid(), after.Revision, CommandKind.UpgradeNetwork))).Accepted,
        "network upgrade requires an audit reason");
}
var networkWorld = Simulation.Create(new WorldConfig(InitialResourceNodes: 0), 42) with {
    Tick = 1, Ants = [new AntView(1, Point.FromCells(52,32), 0) { RequestQueenInfo = true }],
    QueenResources = [new ResourceView(1, Point.FromCells(32,32), 10, 1)]
};
var deniedReply = queenRule.Apply(networkWorld);
var expandedReply = queenRule.Apply(networkWorld with { NetworkLevel = 2 });
Check(deniedReply.Ants[0].QueenRequestStatus == ActionStatus.Failed && expandedReply.Ants[0].QueenRequestStatus == ActionStatus.Succeeded &&
    expandedReply.Ants[0].QueenKnowledge.Length == 1, "network range changes actual communication eligibility");
var gatedNetwork = networkWorld with { Config = networkWorld.Config with { Technologies = [new("queen.communication", "蚁后信息收发", 2)] },
    Ants = [networkWorld.Ants[0] with { Position = Point.FromCells(32,32) }] };
Check(queenRule.Apply(gatedNetwork).Ants[0].QueenRequestStatus == ActionStatus.Failed &&
    queenRule.Apply(gatedNetwork with { NetworkLevel = 2 }).Ants[0].QueenRequestStatus == ActionStatus.Succeeded &&
    !gatedNetwork.IsTechnologyUnlocked("unknown.technology"), "core rules enforce technology gates and unknown technology stays locked");
try {
    (new WorldConfig { Technologies = [new("queen.communication", "bad gate", 6)] }).Validate();
    Check(false, "invalid technology level must fail validation");
} catch (ArgumentException) { Check(true, "out-of-range technology requirements rejected at load time"); }
try {
    (new WorldConfig { NetworkRanges = [16, 8, 32, 48, 64] }).Validate();
    Check(false, "decreasing network range must fail validation");
} catch (ArgumentException) { Check(true, "network progression rejects decreasing ranges"); }
// Typed object storage, small independent food capacities, and visibility invariants.
var capacitySamples = Enumerable.Range(1, 100).SelectMany(seed => Simulation.Create(new WorldConfig(), (ulong)seed).Resources).ToArray();
Check(capacitySamples.All(x => x.MaxCapacity is >= 1 and <= 5 && x.Amount == x.MaxCapacity) &&
    capacitySamples.Select(x => x.MaxCapacity).Distinct().Count() == 5,
    "natural food capacity is independently randomized from one to five");
var archivedFood = (ResourceView?)relay.Objects.Find("food", 1);
Check(archivedFood is { Amount: 0, MaxCapacity: 4 } && relay.Objects.Find("ant", 1) is AntView,
    "depleted food retains its original capacity and type-scoped identity in the object database");
var splitCargo = interaction.Apply(Simulation.Create(new WorldConfig(InitialResourceNodes: 0), 1) with {
    Tick = 1, HaulerCarryLevel = 10, Generated = 12,
    Ants = [new AntView(1, Point.FromCells(10,10), 10, Intent: AntAction.Drop, Species: AntSpeciesIds.Hauler)],
    Resources = [new ResourceView(1, Point.FromCells(10,10), 2, 1, 3)], NextResourceId = 2
});
Check(splitCargo.Resources.Sum(x => x.Amount) == 12 && splitCargo.Resources.All(x => x.Amount <= x.MaxCapacity && x.MaxCapacity <= 5) &&
    ((ResourceView)splitCargo.Objects.Find("food", 1)!).MaxCapacity == 3,
    "large dropped loads split into independent capacity-limited piles without enlarging existing food");
var attributeFood = new ResourceView(101, Point.FromCells(1,1), 3, 0, 5) {
    Properties = ImmutableDictionary<string,string>.Empty.Add("nutrition", "high")
};
var objectDatabase = new EntityDatabase().Put(attributeFood).Put(new AntView(101, Point.FromCells(2,2), 0));
var nextDatabase = objectDatabase.Put(attributeFood with { Amount = 1 });
Check(((ResourceView)objectDatabase.Find("food", 101)!).Amount == 3 &&
    ((ResourceView)nextDatabase.Find("food", 101)!).Properties["nutrition"] == "high" &&
    nextDatabase.Find("ant", 101) is AntView,
    "independent typed records preserve extension properties and immutable history");
await using (var fogSession = new GameSession(new WorldConfig(InitialResourceNodes: 0), 42, [new DiscoveryScenarioRule()])) {
    var beforeFog = await fogSession.ObserveAsync(true);
    await fogSession.ExecuteAsync(new(Guid.NewGuid(), beforeFog.Revision, CommandKind.Step));
    var playerFog = await fogSession.ObserveAsync(true);
    var agentFog = await fogSession.ObserveAsync();
    Check(playerFog.Resources.Select(x=>x.Id).SequenceEqual(new[] {901}) && JsonSerializer.Serialize(playerFog.Resources) == JsonSerializer.Serialize(agentFog.Resources) &&
        playerFog.Generated is null && agentFog.Generated is null,
        "both player and Agent hide unknown objects and global hidden-resource totals");
    var secretObject = await Gateway.DispatchAsync(fogSession, new(Protocol.Version, "inspect_object", ObjectKind: "food", ObjectId: 902), default);
    var missingObject = await Gateway.DispatchAsync(fogSession, new(Protocol.Version, "inspect_object", ObjectKind: "food", ObjectId: 999), default);
    Check(secretObject == missingObject && secretObject.Error == "object_not_found",
        "object ID queries cannot reveal whether an undiscovered food exists");
    var knownObject = await Gateway.DispatchAsync(fogSession, new(Protocol.Version, "inspect_object", ObjectKind: "food", ObjectId: 901), default);
    Check(knownObject.Object?.Food is { MaxCapacity: 2, Amount: 2, IsDiscovered: true, DiscoveredTick: 1 },
        "discovered object queries expose capacity, remaining amount and discovery metadata");
    await fogSession.ExecuteAsync(new(Guid.NewGuid(), playerFog.Revision, CommandKind.Step));
    var staleFog = await fogSession.ObserveAsync(true);
    Check(staleFog.Resources.Single(x=>x.Id==901).Amount == 2 && staleFog.Resources.Single(x=>x.Id==901).LastSeenTick == 1,
        "known food outside observation range does not leak its current quantity");
    await fogSession.ExecuteAsync(new(Guid.NewGuid(), staleFog.Revision, CommandKind.Step));
    var moreFog = await fogSession.ObserveAsync();
    Check(moreFog.Resources.Any(x=>x.Id==902 && x.DiscoveredTick==3), "exploration discovers another independent food record");
    await fogSession.ExecuteAsync(new(Guid.NewGuid(), moreFog.Revision, CommandKind.Step));
    var emptiedFog = await fogSession.ObserveAsync();
    Check(emptiedFog.Resources.Single(x=>x.Id==901) is { Amount: 0, MaxCapacity: 2, IsDiscovered: true },
        "re-observed depletion retains known object identity and original capacity");
}
var leaseClock = new TestClock();
await using (var presenceSession = new GameSession(new WorldConfig(InitialResourceNodes: 0), 42, timeProvider: leaseClock)) {
    var baseline = await presenceSession.ObserveAsync();
    var clientA = Guid.NewGuid(); var clientB = Guid.NewGuid();
    Check(!baseline.AgentConnected, "plain game observation does not pretend an MCP client is connected");
    await presenceSession.UpdateAgentPresenceAsync(new(clientA, "client-a"));
    await presenceSession.UpdateAgentPresenceAsync(new(clientB, "client-b"));
    var connected = await presenceSession.ObserveAsync();
    Check(connected.AgentConnected && connected.ConnectedAgents.Length == 2 && connected.Revision == baseline.Revision,
        "multiple connection leases light the indicator without advancing simulation revision");
    await presenceSession.UpdateAgentPresenceAsync(new(clientA, "client-a", false));
    Check((await presenceSession.ObserveAsync()).ConnectedAgents.SequenceEqual(new[] {"client-b"}),
        "disconnecting one client preserves the other client's indicator");
    leaseClock.Advance(TimeSpan.FromSeconds(6));
    Check(!(await presenceSession.ObserveAsync()).AgentConnected,
        "stale MCP heartbeat expires even while simulation is paused");
}
await ProductionCheck.Run(Check);
CombatCheck.Run(Check);
await ColonyCheck.Run(Check);
MapPresentationCheck.Run(Check);
MilestoneCheck.Run(Check);
BehaviorProgramCheck.Run(Check);
await McpCheck.RunAsync();
Console.WriteLine($"All {checks} architectural checks passed.");
sealed class ExampleRule : IWorldRule {
    public string Id => "example.carry"; public int Order => 100;
    public WorldState Apply(WorldState world) => world with { Ants = world.Ants.SetItem(0, world.Ants[0] with { Carry = 1 }) };
}
sealed class ProbeProgram : IAntProgram {
    public Perception? Perception { get; private set; }
    public Decision Execute(Perception perception, InstructionBudget budget) {
        Perception = perception;
        return new("probe", 0, null, AntAction.Wait);
    }
}

sealed class TestClock : TimeProvider {
    private long timestamp;
    public override long TimestampFrequency => 1000;
    public override long GetTimestamp() => timestamp;
    public void Advance(TimeSpan duration) => timestamp += (long)duration.TotalMilliseconds;
}
sealed class DiscoveryScenarioRule : IWorldRule {
    public string Id => "test.discovery-scenario";
    public int Order => 1;
    public WorldState Apply(WorldState world) {
        if (world.Tick == 1) return world with {
            Ants = [new AntView(1, Point.FromCells(32,32), 0, NextDecisionTick: long.MaxValue)],
            Resources = [new ResourceView(901, Point.FromCells(33,32), 2, 1, 2), new ResourceView(902, Point.FromCells(1,1), 4, 1, 4)],
            NextResourceId = 903, Generated = 6
        };
        if (world.Tick == 2) return world with {
            Ants = [world.Ants[0] with { Position = Point.FromCells(0,63) }],
            Resources = world.Resources.Select(x=>x.Id==901 ? x with { Amount=1 } : x).ToImmutableArray()
        };
        if (world.Tick == 3) return world with { Ants = [world.Ants[0] with { Position = Point.FromCells(1,1) }] };
        if (world.Tick == 4) return world with { Ants = [world.Ants[0] with { Position = Point.FromCells(33,32) }],
            Resources = world.Resources.Where(x=>x.Id!=901).ToImmutableArray() };
        return world;
    }
}
