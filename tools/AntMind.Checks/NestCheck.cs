// VERSION=0.1.0
// S2 巢穴规划架构检查：只走 Program.cs 的单一 Check(bool,label) 计数器。
using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using AntMind.Contracts;
using AntMind.Core;
using AntMind.Programs;
using AntMind.Runtime;

// 与其他 Check 一致放在全局命名空间：Program.cs 顶层语句直接按类型名调用。
internal static class NestCheck
{
    private const int CoreIndex = NestLayout.CoreIndex;

    public static async Task Run(Action<bool, string> check)
    {
        var config = ContentLoader.LoadDefault();
        var defaultLayout = NestLayout.Default;
        var defaultMask = NestLayout.ComputeConnectedMask(defaultLayout.Cells);
        check(SavedGames.Format == 6 && Protocol.Version == 19, "nest/version/wire19-content16-save6");
        check(defaultLayout.Cells.Length == NestLayout.CellCount && defaultLayout.ConnectedMask.Length == NestLayout.CellCount,
            "nest/grid/81-cells-stable-order");
        check(defaultLayout.Cells[CoreIndex] is { X: 4, Y: 4, ChamberType: "core", Phase: "active", Discovered: true }
              && defaultLayout.Cells.Count(c => c.ChamberType == "core") == 1
              && defaultLayout.Cells.Select((c, i) => (c, i)).All(x => x.c.X == x.i % NestLayout.GridWidth && x.c.Y == x.i / NestLayout.GridWidth),
            "nest/grid/single-core-at-4-4");
        check(defaultMask.SequenceEqual(defaultLayout.ConnectedMask)
              && defaultMask.Count(v => v) == 1 && defaultMask[CoreIndex],
            "nest/migration/connected-mask-matches-bfs");
        check(defaultLayout.CapacityBonus == 0 && defaultLayout.Recompute(config.NestLayout).CapacityBonus == 0,
            "nest/migration/single-core-default");

        var cells = defaultLayout.Cells;
        cells = cells.SetItem(31, cells[31] with { ChamberType = "granary", Level = 1, Phase = "active", Discovered = true });
        var connected = NestLayout.ComputeConnectedMask(cells);
        check(connected[31] && !connected[22] && !connected[32] && connected[40]
              && NestLayout.ComputeConnectedMask(cells).SequenceEqual(connected),
            "nest/connectivity/bfs-stable");
        cells = cells.SetItem(21, cells[21] with { ChamberType = "granary", Level = 2, Phase = "active", Discovered = true });
        var diagonalConnected = NestLayout.ComputeConnectedMask(cells);
        check(!diagonalConnected[21] && !diagonalConnected[30] && !diagonalConnected[32],
            "nest/connectivity/no-diagonal");

        var isolated = (defaultLayout with { Cells = cells }).Recompute(config.NestLayout);
        check(isolated.CapacityBonus == 40 && isolated.ConnectedMask[31] && !isolated.ConnectedMask[21],
            "nest/connectivity/isolated-room-zero-bonus");
        var effects = NestEffects.Aggregate(isolated.Cells, isolated.ConnectedMask, config.NestLayout);
        check(effects.CapacityBonus == 40 && effects.EggCostBasis == 100 && effects.SoldierAttackBasis == 100
              && effects.SoldierDefenseBasis == 100 && effects.ProcessSlots == 0 && effects.ProcessSpeedBasis == 100,
            "nest/effect/integer-basis-only");
        check(NestEffects.Aggregate(defaultLayout.Cells, defaultMask, config.NestLayout).CapacityBonus == 0,
            "nest/effect/capacity-only-widen");
        check(config.Foods.Single(f => f.Id == FoodTypes.Wood) is { Edible: false, Value: 0 }
              && config.NestLayout.WoodCost["granary"] == 6 && config.NestLayout.WorkTarget["granary"] == 100,
            "nest/cost/wood-not-food");
        check(ChamberCatalog.Cost("granary", 2, config.NestLayout) == (12, 200),
            "nest/cost/construction-work-monotonic");

        var baseWorld = WithWood(Simulation.Create(config, 7001));
        var planned = NestTransitions.Apply(baseWorld, 4, 3, "granary", CommandKind.PlanChamber).World;
        var plannedCell = planned.Nest.Cells[31];
        var dug = NestTransitions.Apply(planned, 4, 3, "", CommandKind.DigChamber).World;
        var dugCell = dug.Nest.Cells[31];
        check(plannedCell.Phase == "planned" && plannedCell.ChamberType == "granary" && plannedCell.Discovered
              && dugCell.Phase == "digging" && dugCell.WoodReserved == 6 && dugCell.WorkTarget == 100
              && plannedCell.WorkTarget == 0 && dugCell.ConstructionWork >= plannedCell.ConstructionWork
              && ItemLedger.Imbalance(dug).Values.All(v => v == 0),
            "nest/cost/construction-work-monotonic");
        var cancelled = NestTransitions.Apply(dug, 4, 3, "", CommandKind.CancelChamber).World;
        check(cancelled.Nest.Cells[31].WoodReserved == 0 && cancelled.Nest.Cells[31].ChamberType == "empty"
              && cancelled.Stock.Get(FoodTypes.Wood) == baseWorld.Stock.Get(FoodTypes.Wood)
              && ItemLedger.Imbalance(cancelled).Values.All(v => v == 0),
            "nest/cost/cancel-refunds-unspent-only");
        check(ItemLedger.Imbalance(dug).Values.All(v => v == 0)
              && ItemLedger.Held(dug).GetValueOrDefault(FoodTypes.Wood) == ItemLedger.Held(cancelled).GetValueOrDefault(FoodTypes.Wood),
            "nest/conservation/reserved-wood-in-ledger");

        var builder = baseWorld.Ants[0] with {
            Species = AntSpeciesIds.Builder, ChamberX = 4, ChamberY = 3, ChamberAction = 2, WorkRateRemainder = 0
        };
        var working = dug with { Ants = [builder], Tick = 1 };
        working = new NestConstructionRule().Apply(working);
        check(working.Nest.Cells[31].ConstructionWork > dugCell.ConstructionWork
              && working.Nest.Cells[31].Phase == "digging"
              && ItemLedger.Imbalance(working).Values.All(v => v == 0),
            "nest/conservation/reserved-wood-in-ledger");
        var completedWorld = working;
        for (var i = 0; i < 80 && completedWorld.Nest.Cells[31].Phase != "active"; i++)
        {
            var ant = completedWorld.Ants[0] with { ChamberX = 4, ChamberY = 3, ChamberAction = 2, Species = AntSpeciesIds.Builder, WorkRateRemainder = 99 };
            completedWorld = new NestConstructionRule().Apply(completedWorld with { Ants = [ant] });
        }
        var activeCell = completedWorld.Nest.Cells[31];
        check(activeCell.Phase == "active" && activeCell.WoodReserved == 0 && completedWorld.Nest.ConnectedMask[31]
              && completedWorld.Nest.CapacityBonus == 40
              && completedWorld.ConsumedItems.GetValueOrDefault(FoodTypes.Wood) == 6
              && ItemLedger.Imbalance(completedWorld).Values.All(v => v == 0),
            "nest/effect/no-connected-zero-gain");
        check(completedWorld.FirstAdmissionPoints == baseWorld.FirstAdmissionPoints
              && completedWorld.CreatedItems.GetValueOrDefault(FoodTypes.Wood) == baseWorld.CreatedItems.GetValueOrDefault(FoodTypes.Wood)
              && completedWorld.DevelopmentValue == baseWorld.DevelopmentValue,
            "nest/no-farming/build-cancel-loop");

        var farm = baseWorld;
        var initialWood = farm.Stock.Get(FoodTypes.Wood);
        for (var i = 0; i < 5; i++)
        {
            farm = NestTransitions.Apply(farm, 4, 3, "granary", CommandKind.PlanChamber).World;
            farm = NestTransitions.Apply(farm, 4, 3, "", CommandKind.DigChamber).World;
            farm = NestTransitions.Apply(farm, 4, 3, "", CommandKind.CancelChamber).World;
        }
        check(farm.Stock.Get(FoodTypes.Wood) == initialWood && farm.FirstAdmissionPoints == baseWorld.FirstAdmissionPoints
              && farm.CreatedItems.GetValueOrDefault(FoodTypes.Wood) == baseWorld.CreatedItems.GetValueOrDefault(FoodTypes.Wood)
              && ItemLedger.Imbalance(farm).Values.All(v => v == 0),
            "nest/no-farming/build-cancel-loop");

        check(ReplayTrace(config, 7011).SequenceEqual(ReplayTrace(config, 7011)),
            "nest/determinism/same-seed-tick-replay");
        await CheckCommands(check, config);
        await CheckFourKinds(check, config);
        var observer = await GameSessionProjectionsAsync(config);
        check(observer.Player == observer.Agent && observer.Player.Contains("\"nest_capacity\"")
              && observer.Player.Contains("\"observed_chambers\"") && !observer.Player.Contains("\"cells\""),
            "nest/visibility/player-equals-agent");
        check(observer.Hidden.ChamberType == "empty" && !observer.Hidden.Discovered
              && observer.Hidden.Level == 1 && !observer.Hidden.Connected,
            "nest/visibility/undiscovered-chamber-is-empty");
        check(observer.Observed.Count == 1 && observer.Observed[0].X == 4 && observer.Observed[0].Y == 4,
            "nest/visibility/observed-chambers-only");
        CheckDsl(check);
        CheckMigration(check, config);

        var rules = Rules(config);
        check(rules.Length == 23 && rules.Single(r => r.Id == "core.nest-construction").Order == 33,
            "nest/order/core-nest-construction-33");
    }

    private static WorldState WithWood(WorldState world)
    {
        // ProductionFactory 把 wood 放在 SourceView，不进全局 Stock；命令层 DigChamber 只从 Stock.Take。
        // 夹具同步补一笔 Created + Stock，保持 ItemLedger.Imbalance 全 0。
        world = Economy.Created(world, FoodTypes.Wood, 60);
        return world with { Stock = world.Stock.Add(FoodTypes.Wood, 60) };
    }

    private static ImmutableArray<IWorldRule> Rules(WorldConfig config)
    {
        var field = typeof(Simulation).GetField("rules", BindingFlags.NonPublic | BindingFlags.Instance);
        var simulation = GameRules.Create(new SpeciesProgramCatalog(config.AntTypes.Select(x => x.Id)));
        return (ImmutableArray<IWorldRule>)field!.GetValue(simulation)!;
    }

    private static IReadOnlyList<long> ReplayTrace(WorldConfig config, ulong seed)
    {
        var trace = new List<long>();
        var world = WithWood(Simulation.Create(config, seed));
        world = NestTransitions.Apply(world, 4, 3, "granary", CommandKind.PlanChamber).World;
        world = NestTransitions.Apply(world, 4, 3, "", CommandKind.DigChamber).World;
        var simulation = new Simulation([new NestConstructionRule(), new LocalObservationRule()]);
        for (var tick = 0; tick < 12; tick++)
        {
            var ant = world.Ants[0] with { ChamberX = 4, ChamberY = 3, ChamberAction = 2, Species = AntSpeciesIds.Builder };
            world = simulation.Step(world with { Ants = [ant] });
            trace.Add(world.Tick);
            trace.Add((long)world.RandomState);
            trace.Add((long)world.NestSeed);
            trace.Add(world.Nest.Cells[31].ConstructionWork);
            trace.Add(world.Nest.ConnectedMask.Count(v => v));
            trace.Add(world.Stock.Get(FoodTypes.Wood));
            trace.Add(world.FirstAdmissionPoints);
            trace.Add(world.ObservedChambers.Sum());
        }
        return trace;
    }

    private static async Task CheckCommands(Action<bool, string> check, WorldConfig config)
    {
        var directory = Path.Combine(Path.GetTempPath(), "antmind-nest-commands-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await using var session = new GameSession(config, 7022, savesPath: directory);
            var v = await session.ObserveAsync();
            var request = new GameCommand(Guid.NewGuid(), v.Revision, CommandKind.PlanChamber) {
                ExpectedWorldId = v.WorldId, ChamberX = 4, ChamberY = 3, ChamberType = "granary"
            };
            var first = await session.ExecuteAsync(request);
            var replay = await session.ExecuteAsync(request);
            var changed = await session.ExecuteAsync(request with { ChamberX = 3, ChamberY = 4, ChamberType = "corridor" });
            var after = await session.ObserveAsync();
            check(first.Accepted && first.Code == "applied" && replay.Accepted && replay == first
                  && !changed.Accepted && changed.Code == "request_id_reused" && after.Revision == v.Revision + 1,
                "nest/command/revision-idempotent");
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }

    private static async Task CheckFourKinds(Action<bool, string> check, WorldConfig config)
    {
        const string label = "nest/command/four-kinds-real-accepted";
        var directory = Path.Combine(Path.GetTempPath(), "antmind-nest-four-kinds-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var catalog = new SpeciesProgramCatalog(config.AntTypes.Select(x => x.Id));
            var fixture = WithWood(Simulation.Create(config, 7032));
            var cells = fixture.Nest.Cells.SetItem(31, fixture.Nest.Cells[31] with {
                ChamberType = "granary", Level = 1, Phase = "active", Discovered = true, WorkTarget = 100
            });
            var rebuilt = (fixture.Nest with { Cells = cells }).Recompute(config.NestLayout);
            fixture = fixture with { Nest = rebuilt, ObservedChambers = NestTransitions.RebuildObserved(cells) };
            SavedGames.Write(directory, "nest4", new SavedGame(SavedGames.Format, Protocol.Version, fixture, catalog.Export()));

            await using var session = new GameSession(config, 7033, savesPath: directory);
            var loadView = await session.ObserveAsync();
            var loaded = await session.ExecuteAsync(new GameCommand(Guid.NewGuid(), loadView.Revision, CommandKind.LoadGame, Reason: "nest4") {
                ExpectedWorldId = loadView.WorldId, Key = "nest4"
            });
            if (!loaded.Accepted || loaded.Code != "loaded_paused")
            {
                check(false, label);
                return;
            }

            async Task<CommandResult> Send(CommandKind kind, int x, int y, string type)
            {
                var view = await session.ObserveAsync();
                return await session.ExecuteAsync(new GameCommand(Guid.NewGuid(), view.Revision, kind, Reason: "nest4") {
                    ExpectedWorldId = view.WorldId, ChamberX = x, ChamberY = y, ChamberType = type
                });
            }

            var plan = await Send(CommandKind.PlanChamber, 5, 4, "granary");
            var dig = await Send(CommandKind.DigChamber, 5, 4, "granary");
            var upgrade = await Send(CommandKind.UpgradeChamber, 4, 3, "granary");
            var cancel = await Send(CommandKind.CancelChamber, 5, 4, "granary");
            check(new[] { plan, dig, upgrade, cancel }.All(r => r.Accepted && r.Code == "applied"), label);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }

    private static async Task<(string Player, string Agent, ChamberView Hidden, IReadOnlyList<ChamberView> Observed)> GameSessionProjectionsAsync(WorldConfig config)
    {
        var directory = Path.Combine(Path.GetTempPath(), "antmind-nest-project-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await using var session = new GameSession(config, 7021, savesPath: directory);
            var player = await session.ObserveAsync(true);
            var agent = await session.ObserveAsync();
            var hidden = ChamberView.Hidden(0, 0);
            return (JsonSerializer.Serialize(player), JsonSerializer.Serialize(agent), hidden, player.ObservedChambers);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }

    private static void CheckDsl(Action<bool, string> check)
    {
        check(Throws(() => AntDslCompiler.Compile("plan_chamber 4 3 \"storage\";"))
              && Throws(() => AntDslCompiler.Compile("plan_chamber 4 3;"))
              && Throws(() => AntDslCompiler.Compile("let x = chamber_unknown(4,3);")),
            "nest/dsl/whitelist-closed");
        var source = "let a = chamber_connected(0,0); let b = chamber_level(0,0); let c = chamber_phase(0,0); let d = chamber_count(\"\"); let e = nest_capacity;";
        var parsed = AntDslCompiler.Compile(source);
        var perception = new Perception(1, 0, Point.FromCells(32, 32), Point.FromCells(32, 32), 0, null, 64, 64, []) {
            ObservedChamberViews = [ChamberView.Hidden(0, 0)]
        };
        var decision = parsed.Execute(perception, new InstructionBudget(512));
        check(decision.Spent > 0 && decision.Memory.Length >= 5 && decision.Memory[0] == 0
              && decision.Memory[1] == 0 && decision.Memory[2] == 0 && decision.Memory[3] == 0,
            "nest/dsl/undiscovered-returns-zero");
        check(ChamberCatalog.PhaseCode("empty") == 0 && ChamberCatalog.PhaseCode("active") == 3,
            "nest/dsl/undiscovered-returns-zero");
    }

    private static void CheckMigration(Action<bool, string> check, WorldConfig config)
    {
        var directory = Path.Combine(Path.GetTempPath(), "antmind-nest-migration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var legacy = Simulation.Create(config with { InitialResourceNodes = 0 }, 7031) with { Tick = 3 };
            var programCatalog = new SpeciesProgramCatalog(config.AntTypes.Select(x => x.Id));
            var oldSave = new SavedGame(5, 18, legacy, programCatalog.Export());
            var raw = JsonSerializer.SerializeToUtf8Bytes(oldSave, new JsonSerializerOptions { IgnoreReadOnlyProperties = true, MaxDepth = 64 });
            var node = JsonNode.Parse(raw) as JsonObject ?? throw new InvalidDataException("Empty save.");
            node["Format"] = 5; node["Protocol"] = 18;
            var worldNode = node["World"] as JsonObject ?? throw new InvalidDataException("Invalid save JSON.");
            foreach (var key in new[] { "creature_seed", "nest_seed", "nest", "institution", "blocked_cells", "observed_chambers" })
                worldNode.Remove(key);
            var configNode = worldNode["Config"] as JsonObject ?? throw new InvalidDataException("Invalid save JSON.");
            foreach (var key in new[] { "Institutions", "Creatures", "NestLayout" }) configNode.Remove(key);
            var path = Path.Combine(directory, "legacy.json");
            var bytes = JsonSerializer.SerializeToUtf8Bytes(node);
            File.WriteAllBytes(path, bytes);
            var migrated = SavedGames.Read(directory, "legacy");
            check(migrated.Format == SavedGames.Format && migrated.Protocol == Protocol.Version
                  && migrated.World.Nest.Cells.Length == NestLayout.CellCount
                  && migrated.World.ObservedChambers.SequenceEqual([CoreIndex])
                  && migrated.World.Nest.ConnectedMask.SequenceEqual(NestLayout.ComputeConnectedMask(migrated.World.Nest.Cells)),
                "nest/migration/save5-empty-layout");
            check(bytes.SequenceEqual(File.ReadAllBytes(path)), "nest/migration/original-bytes-untouched");
            SavedGames.Write(directory, "current", new SavedGame(SavedGames.Format, Protocol.Version, migrated.World, programCatalog.Export()));
            var current = Path.Combine(directory, "current.json");
            var currentBytes = File.ReadAllBytes(current);
            var reread = SavedGames.Read(directory, "current");
            check(reread.World.Nest.Cells.Length == NestLayout.CellCount && currentBytes.SequenceEqual(File.ReadAllBytes(current)),
                "nest/migration/save6-bytes-untouched");
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }

    private static bool Throws(Action action)
    {
        try { action(); return false; } catch { return true; }
    }
}
