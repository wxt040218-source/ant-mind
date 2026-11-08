using System.Text.Json;
using System.Collections.Immutable;
using AntMind.Contracts;
using AntMind.Core;
using AntMind.Programs;
using AntMind.Runtime;
using AntMind.Transport;
if (args.Contains("--compare-ratios")) {
    var ticks = ReadOption(args, "--ticks", 2000);
    var seed = (ulong)ReadOption(args, "--seed", 42);
    if (ticks is < 0 or > 100_000) throw new ArgumentOutOfRangeException(nameof(ticks));
    var baseline = ContentLoader.LoadDefault();
    var cases = new[] { (Name: "scout-heavy", Scouts: 8), (Name: "balanced", Scouts: 5), (Name: "hauler-heavy", Scouts: 2) };
    var results = cases.Select(test => {
        var types = baseline.AntTypes.Select(type => type with {
            InitialCount = type.Id == AntSpeciesIds.Scout ? test.Scouts : type.Id == AntSpeciesIds.Hauler ? 10 - test.Scouts : 0
        }).ToImmutableArray();
        var config = baseline with { InitialAnts = 10, AntTypes = types };
        var world = Evaluate(config, seed, ticks);
        return new {
            test.Name, world.Tick, Scouts = world.Ants.Count(x => x.Species == AntSpeciesIds.Scout),
            Haulers = world.Ants.Count(x => x.Species == AntSpeciesIds.Hauler), world.Delivered,world.FirstAdmissionPoints,
            ScoutDelivered = world.DeliveredBySpecies.GetValueOrDefault(AntSpeciesIds.Scout),
            HaulerDelivered = world.DeliveredBySpecies.GetValueOrDefault(AntSpeciesIds.Hauler),
            DeliveredPerSimulatedSecond = ticks == 0 ? 0 : world.Delivered * baseline.TicksPerSecond / (double)ticks,
            Budget = config.BrainBudgets[world.BrainLevel - 1], seed
        };
    }).ToArray();
    Console.WriteLine(JsonSerializer.Serialize(new { Seed = seed, Ticks = ticks, Ratios = results }));
    return;
}
await using var session = new GameSession(ContentLoader.LoadDefault(), 42);
if (args.Contains("--serve")) {
    using var shutdown = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; shutdown.Cancel(); };
    Console.Error.WriteLine("AntMind headless host; Ctrl+C stops it.");
    await new PipeServer(Environment.GetEnvironmentVariable("ANTMIND_PIPE") ?? "antmind-v1",
        (r, ct) => Gateway.DispatchAsync(session, r, ct)).RunAsync(shutdown.Token);
} else {
    int ticks = args.Length > 0 ? int.Parse(args[0]) : 100;
    if (ticks is < 0 or > 100_000) throw new ArgumentOutOfRangeException(nameof(ticks));
    for(int i=0;i<ticks;i++) {
        var view = await session.ObserveAsync();
        await session.ExecuteAsync(new(Guid.NewGuid(), view.Revision, CommandKind.Step));
    }
    var snapshot = await session.ObserveAsync(true);
    Console.WriteLine(JsonSerializer.Serialize(new { snapshot.Tick, snapshot.Inventory,
        snapshot.Delivered, snapshot.Generated,
        Ants = snapshot.Ants.Length, snapshot.BrainLevel,
        ResourceNodes = snapshot.Resources.Length, Ground = snapshot.Resources.Sum(r => r.Amount) }));
}

static WorldState Evaluate(WorldConfig config, ulong seed, int ticks) {
    var catalog=new SpeciesProgramCatalog(config.AntTypes.Select(x=>x.Id));
    var simulation=GameRules.Create(catalog);
    var world=Simulation.Create(config,seed);
    for(var i=0;i<ticks;i++)world=simulation.Step(GameRules.Activate(world,catalog));
    return world;
}

static int ReadOption(string[] arguments, string option, int fallback) {
    var index = Array.IndexOf(arguments, option);
    return index < 0 ? fallback : index + 1 < arguments.Length
        ? int.Parse(arguments[index + 1], System.Globalization.CultureInfo.InvariantCulture)
        : throw new ArgumentException($"{option} requires an integer value.");
}
