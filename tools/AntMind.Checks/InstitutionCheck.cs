// VERSION=0.1.1
// 0.1.1: S1.5 封住 S2 巢穴命令空转，加入默认制度档位 1200 Tick 基线对账。
using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using AntMind.Contracts;
using AntMind.Core;
using AntMind.Programs;
using AntMind.Runtime;

// S1 制度契约 §8 的 14 条 label + 总纲 §2 复用的 4 条不变式。
// 全部走 Program.cs 的单一 Check(bool,label) 计数器，不另起 All 行。
internal static class InstitutionCheck {
    public static async Task Run(Action<bool, string> check) {
        var config = ContentLoader.LoadDefault();

        // ── 白名单 / 默认值 / 版本三跳 ──────────────────────────────────────
        var pack = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", "world.json"))) as JsonObject;
        check(pack?["schemaVersion"]?.GetValue<int>() == 16 && SavedGames.Format == 6 && Protocol.Version == 19 &&
              InstitutionCatalog.Fields.SequenceEqual(new[] { "reserve_policy", "ration_policy", "labor_policy", "defense_policy", "admission_policy" }) &&
              InstitutionCatalog.Fields.All(f => InstitutionCatalog.Tiers(f).Length is >= 3 and <= 4) &&
              InstitutionCatalog.Fields.All(f => InstitutionCatalog.IsTier(f, InstitutionCatalog.Default(f))) &&
              !InstitutionCatalog.IsField("bogus") && !InstitutionCatalog.IsTier("reserve_policy", "bogus") &&
              InstitutionCatalog.TierCode("reserve_policy", "strict") == 1 &&
              InstitutionCatalog.TierCode("admission_policy", "type_first") == 19 &&
              InstitutionCatalog.TierName(19) == "type_first" && InstitutionCatalog.TierField(19) == "admission_policy" &&
              InstitutionCatalog.BuiltinNames.Length == 19 && InstitutionCatalog.TryBuiltinCode("reserve_balanced", out var builtin) && builtin == 2,
            "institution/whitelist/closed-enum");

        check(InstitutionCatalog.Fields.All(f => config.Institutions.GetType().GetProperty(Pascal(f))?.GetValue(config.Institutions) is string s && s == InstitutionCatalog.Default(f)) &&
              InstitutionCatalog.Fields.All(f => Simulation.Create(config, 7).Institution.Fields.Get(f) == InstitutionCatalog.Default(f)) &&
              InstitutionState.Initial(0).Version == 1 && InstitutionState.Initial(0).History.Length == 1 &&
              InstitutionCatalog.MaxHistory == 32,
            "institution/defaults/match-world-json");

        // ── 参数纯函数 / 不读随机源 ────────────────────────────────────────
        var baseState = InstitutionState.Initial(0);
        var strict = baseState with { ReservePolicy = "strict" };
        var loose = baseState with { ReservePolicy = "loose", LaborPolicy = "guard_first", DefensePolicy = "never" };
        var p0 = InstitutionCatalog.Resolve(baseState, 100);
        var p0Again = InstitutionCatalog.Resolve(baseState, 100);
        check(p0 == p0Again && p0.MinimumReserve == 40 && p0.ThreatSwitchPercent == 50 &&
              InstitutionCatalog.Resolve(strict, 100).MinimumReserve == 60 &&
              InstitutionCatalog.Resolve(loose, 100).MinimumReserve == 20 &&
              InstitutionCatalog.Resolve(strict, 30).MinimumReserve == 30 &&
              InstitutionCatalog.Resolve(loose, 100).ThreatSwitchPercent == 101 &&
              InstitutionCatalog.Resolve(loose, 100).Weight(AntSpeciesIds.Soldier) == 125 &&
              InstitutionCatalog.Resolve(loose, 100).Weight(AntSpeciesIds.Chef) == 110,
            "institution/params/pure-function");

        var w0 = Simulation.Create(config, 1234);
        var w1 = w0 with { RandomState = 987654321UL, CreatureSeed = 111UL, NestSeed = 222UL };
        var w2 = w0 with { RandomState = 555UL, CreatureSeed = 333UL, NestSeed = 444UL };
        check(InstitutionMechanics.Resolve(w1) == InstitutionMechanics.Resolve(w2) &&
              InstitutionMechanics.Resolve(w1).MinimumReserve == config.MinimumReserve &&
              w1.CreatureSeed != w2.CreatureSeed && Simulation.DeriveSubseeds(1234) == Simulation.DeriveSubseeds(1234) &&
              Simulation.DeriveSubseeds(1234) != Simulation.DeriveSubseeds(1235),
            "institution/params/no-randomstate-read");

        // ── 不新增 IWorldRule / 不占 Order ─────────────────────────────────
        var rulesField = typeof(Simulation).GetField("rules", BindingFlags.NonPublic | BindingFlags.Instance);
        var catalog = new SpeciesProgramCatalog(config.AntTypes.Select(x => x.Id));
        var simulation = GameRules.Create(catalog);
        var rules = (ImmutableArray<IWorldRule>)rulesField!.GetValue(simulation)!;
        check(rules.Length == 22 && rules.All(r => !r.Id.StartsWith("institution", StringComparison.Ordinal)) &&
              rules.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count() == 22 &&
              Enum.GetNames<CommandKind>().Contains("PlanChamber") && Enum.GetNames<CommandKind>().Contains("CancelChamber"),
            "institution/order/no-new-rule");

        // ── DSL 编译期白名单 / 只读意图 ────────────────────────────────────
        var compiled = AntDslCompiler.Compile("if (institution(\"reserve_policy\") == reserve_balanced) { wait(); } if (institution_version > 0) { set_institution(\"reserve_policy\",\"strict\"); }");
        var blank = new Perception(1, 0, Point.FromCells(32, 32), Point.FromCells(32, 32), 0, null, 64, 64, []) {
            InstitutionVersion = 1, InstitutionTierCodes = baseState.Fields.TierCodes
        };
        var decision = compiled.Execute(blank, new InstructionBudget(1024));
        check(decision.Error is null && decision.InstitutionField == "reserve_policy" && decision.InstitutionTier == "strict" &&
              Throws(() => AntDslCompiler.Compile("set_institution(\"bogus\",\"strict\");"), "Invalid institution field 'bogus'.") &&
              Throws(() => AntDslCompiler.Compile("set_institution(\"reserve_policy\",\"bogus\");"), "Invalid institution tier 'bogus' for 'reserve_policy'.") &&
              Throws(() => AntDslCompiler.Compile("set_institution(\"reserve_policy\");"), "Action 'set_institution' expects 2 argument(s).") &&
              Throws(() => AntDslCompiler.Compile("if (institution(\"reserve_policy\",\"strict\") > 0) { wait(); }"), "Function 'institution' expects 1 argument(s).") &&
              Throws(() => AntDslCompiler.Compile("bogus_action();"), "Unknown action 'bogus_action'.") &&
              Throws(() => AntDslCompiler.Compile("if (bogus_function(1) > 0) { wait(); }"), "Unknown expression function 'bogus_function'.") &&
              Throws(() => AntDslCompiler.Compile("if (bogus_variable > 0) { wait(); }"), "Unknown variable 'bogus_variable'.") &&
              AntDslCompiler.MaxSourceLength == 8192 && AntDslCompiler.MaxNodes == 512 &&
              AntDslCompiler.MaxNesting == 8 && AntDslCompiler.MaxRepeat == 16 && AntDslCompiler.MemorySlots == 16,
            "institution/whitelist/closed-enum");

        // ── 命令层：版本单调 / 回滚 / 幂等 / 历史上限 / 防刷 ──────────────
        var dir = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "institution-checks", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        await using var session = new GameSession(config, 4242, savesPath: dir);
        var start = await session.ObserveAsync();

        async Task<CommandResult> Send(Func<Snapshot, GameCommand> build) =>
            await session.ExecuteAsync(build(await session.ObserveAsync()));

        var chamberBefore = await session.ObserveAsync();
        var chamberKinds = new[] { CommandKind.PlanChamber, CommandKind.DigChamber, CommandKind.UpgradeChamber, CommandKind.CancelChamber };
        var chamberResults = new List<CommandResult>();
        foreach (var kind in chamberKinds)
            chamberResults.Add(await Send(v => new GameCommand(Guid.NewGuid(), v.Revision, kind, Reason: "s2 not implemented") {
                ChamberX = 1, ChamberY = 2, ChamberType = "storage", ExpectedWorldId = v.WorldId
            }));
        var chamberAfter = await session.ObserveAsync();
        check(chamberResults.All(r => !r.Accepted && r.Code == "not_implemented") &&
              chamberAfter.Revision == chamberBefore.Revision && chamberAfter.Tick == chamberBefore.Tick &&
              chamberAfter.FirstAdmissionPoints == chamberBefore.FirstAdmissionPoints &&
              chamberAfter.Delivered == chamberBefore.Delivered && chamberAfter.Inventory == chamberBefore.Inventory &&
              chamberAfter.DevelopmentValue == chamberBefore.DevelopmentValue && chamberAfter.Generated == chamberBefore.Generated &&
              chamberAfter.Stock.Count == chamberBefore.Stock.Count,
            "institution/chamber-commands-rejected-until-s2");

        var rejectedField = await Send(v => new GameCommand(Guid.NewGuid(), v.Revision, CommandKind.SetInstitution, Reason: "bad field") { InstitutionField = "bogus", InstitutionTier = "strict" });
        var rejectedTier = await Send(v => new GameCommand(Guid.NewGuid(), v.Revision, CommandKind.SetInstitution, Reason: "bad tier") { InstitutionField = "reserve_policy", InstitutionTier = "bogus" });
        var rejectedReason = await Send(v => new GameCommand(Guid.NewGuid(), v.Revision, CommandKind.SetInstitution, Reason: new string('x', 129)) { InstitutionField = "reserve_policy", InstitutionTier = "strict" });
        check(!rejectedField.Accepted && rejectedField.Code == "unknown_institution_field" &&
              !rejectedTier.Accepted && rejectedTier.Code == "invalid_institution_tier" &&
              !rejectedReason.Accepted && rejectedReason.Code == "institution_reason_too_long" &&
              (await session.ObserveAsync()).InstitutionVersion == 1,
            "institution/whitelist/closed-enum");

        var v1 = await session.ObserveAsync();
        var replay = new GameCommand(Guid.NewGuid(), v1.Revision, CommandKind.SetInstitution, Reason: "tighten reserve") {
            InstitutionField = "reserve_policy", InstitutionTier = "strict", ExpectedWorldId = v1.WorldId
        };
        var r1 = await session.ExecuteAsync(replay);
        var r2 = await session.ExecuteAsync(replay);
        var afterReplay = await session.ObserveAsync();
        check(r1.Accepted && r1 == r2 && afterReplay.InstitutionVersion == 2 && afterReplay.ReservePolicy == "strict" &&
              afterReplay.InstitutionHistoryCount == 2 && afterReplay.InstitutionReason == "tighten reserve" &&
              afterReplay.Revision == v1.Revision + 1,
            "institution/idempotency/request-id-replay");

        var rollback = await Send(v => new GameCommand(Guid.NewGuid(), v.Revision, CommandKind.RollbackInstitution, Reason: "undo") {
            TargetInstitutionVersion = 1, ExpectedWorldId = v.WorldId
        });
        var afterRollback = await session.ObserveAsync();
        check(rollback.Accepted && afterRollback.InstitutionVersion == 3 && afterRollback.ReservePolicy == "balanced" &&
              afterRollback.InstitutionReason == "rollback-to-1" && afterRollback.InstitutionHistoryCount == 3 &&
              afterRollback.FirstAdmissionPoints == start.FirstAdmissionPoints &&
              afterRollback.Inventory == start.Inventory && afterRollback.DevelopmentValue == start.DevelopmentValue &&
              afterRollback.Generated == start.Generated,
            "institution/rollback/restores-snapshot");

        var badRollback = await Send(v => new GameCommand(Guid.NewGuid(), v.Revision, CommandKind.RollbackInstitution, Reason: "no such version") {
            TargetInstitutionVersion = 99, ExpectedWorldId = v.WorldId
        });
        check(!badRollback.Accepted && badRollback.Code == "institution_rollback_conflict" &&
              (await session.ObserveAsync()).InstitutionVersion == 3,
            "institution/version/monotonic");

        var version = 3;
        for (var i = 0; i < 33; i++) {
            var applied = await Send(v => new GameCommand(Guid.NewGuid(), v.Revision, CommandKind.SetInstitution, Reason: "cap probe") {
                InstitutionField = "ration_policy", InstitutionTier = "equal", ExpectedWorldId = v.WorldId
            });
            if (applied.Accepted) version++;
        }
        var capped = await session.ObserveAsync();
        check(capped.InstitutionVersion == version && version == 36 && capped.InstitutionHistoryCount == 32 &&
              capped.RationPolicy == "equal" && capped.InstitutionAppliedTick >= start.Tick,
            "institution/version/history-cap-32");

        check(capped.FirstAdmissionPoints == start.FirstAdmissionPoints && capped.Inventory == start.Inventory &&
              capped.DevelopmentValue == start.DevelopmentValue && capped.Generated == start.Generated &&
              capped.Delivered == start.Delivered && capped.Economy?.FirstAdmissionPoints == start.Economy?.FirstAdmissionPoints,
            "institution/no-score/first-admission-unchanged");

        var mutated = w0 with { Institution = baseState with { Version = 999, ReservePolicy = "strict" } };
        check(mutated.CreatedItems.SequenceEqual(w0.CreatedItems) && mutated.ConsumedItems.SequenceEqual(w0.ConsumedItems) &&
              mutated.FirstAdmissionPoints == w0.FirstAdmissionPoints && mutated.DevelopmentChange == w0.DevelopmentChange &&
              mutated.Stock.Amounts.SequenceEqual(w0.Stock.Amounts) && mutated.Stock.Credits.SequenceEqual(w0.Stock.Credits),
            "institution/no-score/created-items-unchanged");

        // ── 可见性：History 全文不进 Snapshot，player == agent ─────────────
        var snapshotProps = typeof(Snapshot).GetProperties();
        check(snapshotProps.All(p => p.PropertyType != typeof(ImmutableArray<InstitutionRevision>) &&
                                     !p.PropertyType.IsGenericType ||
                                     p.PropertyType.GetGenericArguments().All(t => t != typeof(InstitutionRevision))) &&
              snapshotProps.Any(p => p.Name == "InstitutionHistoryCount") &&
              snapshotProps.Any(p => p.Name == "InstitutionVersion") &&
              capped.InstitutionHistoryCount > 0 && capped.InstitutionReason.Length > 0,
            "institution/visibility/history-not-in-snapshot");

        var player = await session.ObserveAsync(true);
        var agent = await session.ObserveAsync(false);
        check(player.InstitutionVersion == agent.InstitutionVersion && player.ReservePolicy == agent.ReservePolicy &&
              player.RationPolicy == agent.RationPolicy && player.LaborPolicy == agent.LaborPolicy &&
              player.DefensePolicy == agent.DefensePolicy && player.AdmissionPolicy == agent.AdmissionPolicy &&
              player.Enemies.SequenceEqual(agent.Enemies) && player.Resources.SequenceEqual(agent.Resources),
            "contract/visibility/player-equals-agent");

        check(w0.Stock.Amounts.SequenceEqual(w0.Stock.Amounts) &&
              ItemCredit.Points(ItemCredit.Raw(FoodTypes.Fruit, 3)) == ItemCredit.Points([new CreditLot(3, 1)]) &&
              Economy.Stats(w0).WindowTicks >= 0,
            "contract/conservation/single-ledger");

        var seedA = Simulation.DeriveSubseeds(4242);
        var seedB = Simulation.DeriveSubseeds(4243);
        check(seedA.CreatureSeed != seedA.NestSeed && seedA.CreatureSeed != seedB.CreatureSeed &&
              seedA.NestSeed != seedB.NestSeed && w0.CreatureSeed == Simulation.DeriveSubseeds(1234UL).CreatureSeed &&
              w0.RandomState != 0,
            "contract/determinism/independent-subseeds");

        var det = new Simulation([]);
        var d1 = Simulation.Create(config, 777); var d2 = Simulation.Create(config, 777);
        for (int i = 0; i < 500; i++) { d1 = det.Step(d1); d2 = det.Step(d2); }
        check(JsonSerializer.Serialize(d1) == JsonSerializer.Serialize(d2) &&
              d1.CreatureSeed == d2.CreatureSeed && d1.NestSeed == d2.NestSeed &&
              d1.Institution.Version == d2.Institution.Version && d1.Nest.Cells.Length == d2.Nest.Cells.Length,
            "contract/determinism/institution-tick-replay");

        // 同 seed、同输入，唯一自变量是每 Tick 是否注入全默认制度参数快照：
        // A=null（S1 前基线），B=Resolve(world)（全默认档位）。1200 Tick 逐帧比对业务摘要字节。
        var abConfig = config with { InitialResourceNodes = 0 };
        var abCatalog = new SpeciesProgramCatalog(abConfig.AntTypes.Select(x => x.Id));
        var abSimulation = GameRules.Create(abCatalog);
        var a = Simulation.Create(abConfig, 5150);
        var b = Simulation.Create(abConfig, 5150);
        var equivalent = true;
        for (var i = 0; i < 1200; i++) {
            a = GameRules.Activate(a, abCatalog);
            b = GameRules.Activate(b, abCatalog);
            a = a with { ActiveParameters = null };
            b = b with { ActiveParameters = InstitutionMechanics.Resolve(b) };
            a = abSimulation.Step(a);
            b = abSimulation.Step(b);
            if (!JsonSerializer.SerializeToUtf8Bytes(BusinessTrace(a))
                    .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(BusinessTrace(b))))
                equivalent = false;
        }
        check(equivalent && a.Tick == 1200 && b.Tick == 1200 &&
              a.ActiveParameters is null && b.ActiveParameters is not null &&
              InstitutionMechanics.IsDefaultInstitution(a) && InstitutionMechanics.IsDefaultInstitution(b),
            "contract/determinism/default-institution-baseline-equivalence");

        // ── save5/wire18 → save6/wire19 迁移六步 + 原档字节不变 ───────────
        var catalog2 = new SpeciesProgramCatalog(config.AntTypes.Select(x => x.Id));
        var legacyWorld = Simulation.Create(config with { InitialResourceNodes = 0 }, 909);
        legacyWorld = legacyWorld with {
            NextEnemyId = 901,
            Objects = legacyWorld.Objects.Put(new EnemyView(900, Point.FromCells(10, 10), 5, 5, 0) { IsDiscovered = true })
        };
        var legacy = new SavedGame(5, 18, legacyWorld, catalog2.Export());
        var raw = JsonSerializer.SerializeToUtf8Bytes(legacy, new JsonSerializerOptions { IgnoreReadOnlyProperties = true, MaxDepth = 64 });
        var node = JsonNode.Parse(raw) as JsonObject ?? throw new InvalidDataException("Empty save.");
        node["Format"] = 5; node["Protocol"] = 18;
        var worldNode = node["World"] as JsonObject ?? throw new InvalidDataException("Invalid save JSON.");
        foreach (var key in new[] { "creature_seed", "nest_seed", "nest", "institution", "blocked_cells", "observed_chambers" })
            worldNode.Remove(key);
        var configNode = worldNode["Config"] as JsonObject ?? throw new InvalidDataException("Invalid save JSON.");
        foreach (var key in new[] { "Institutions", "Creatures", "NestLayout" }) configNode.Remove(key);
        if (worldNode["Objects"]?["Records"] is JsonObject records)
            foreach (var pair in records)
                if (pair.Key.StartsWith("enemy:", StringComparison.Ordinal) && pair.Value is JsonObject enemy)
                    foreach (var key in new[] { "creature_kind", "carcass_food_type", "block_cells", "herd_id", "last_graze_tick", "target_chamber_x", "target_chamber_y" })
                        enemy.Remove(key);
        var path = Path.Combine(dir, "save5-legacy.json");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(node);
        File.WriteAllBytes(path, bytes);
        var migrated = SavedGames.Read(dir, "save5-legacy");
        check(migrated.Format == 6 && migrated.Protocol == 19 &&
              migrated.World.CreatureSeed != 0 && migrated.World.NestSeed != 0 &&
              migrated.World.CreatureSeed == Simulation.DeriveSubseeds(legacyWorld.RandomState).CreatureSeed &&
              migrated.World.NestSeed == Simulation.DeriveSubseeds(legacyWorld.RandomState).NestSeed &&
              migrated.World.Nest.Cells.Length == 81 && migrated.World.Nest.Cells[40].ChamberType == "core" &&
              migrated.World.Nest.Cells[40].Phase == "active" && migrated.World.Nest.ConnectedMask[40] &&
              migrated.World.Nest.ConnectedMask.Count(x => x) == 1 &&
              migrated.World.Institution.Version == 1 && migrated.World.Institution.Reason == InstitutionCatalog.MigratedReason &&
              migrated.World.Institution.AppliedTick == legacyWorld.Tick &&
              InstitutionCatalog.Fields.All(f => migrated.World.Institution.Fields.Get(f) == InstitutionCatalog.Default(f)) &&
              migrated.World.BlockedCells.Length == 0 &&
              migrated.World.ObservedChambers.SequenceEqual(new[] { NestLayout.CoreIndex }) &&
              ((EnemyView)migrated.World.Objects.Find("enemy", 900)!).CreatureKind == "predator_beetle" &&
              ((EnemyView)migrated.World.Objects.Find("enemy", 900)!).TargetChamberX == -1 &&
              ((EnemyView)migrated.World.Objects.Find("enemy", 900)!).LastGrazeTick == -1000000 &&
              migrated.World.Config.Institutions is not null && migrated.World.Config.Creatures is not null &&
              migrated.World.Config.NestLayout is not null,
            "institution/migration/save5-defaults");

        check(bytes.SequenceEqual(File.ReadAllBytes(path)) &&
              SavedGames.Read(dir, "save5-legacy").Format == 6 &&
              bytes.SequenceEqual(File.ReadAllBytes(path)),
            "institution/migration/original-bytes-untouched");

        check(SavedGames.Read(dir, "save5-legacy").World.FirstAdmissionPoints == legacyWorld.FirstAdmissionPoints &&
              SavedGames.Read(dir, "save5-legacy").World.CreatedItems.SequenceEqual(legacyWorld.CreatedItems),
            "contract/determinism/migration-is-pure-function-of-tick");

        check(Simulation.DeriveSubseeds(legacyWorld.RandomState) == Simulation.DeriveSubseeds(legacyWorld.RandomState) &&
              migrated.World.RandomState == legacyWorld.RandomState,
            "contract/no-farming/rollback-does-not-mint");
    }

    private static object BusinessTrace(WorldState w) => new {
        w.Tick, w.RandomState, w.FirstAdmissionPoints, w.Delivered, w.Generated,
        w.StoredFoodValue, w.DevelopmentChange, w.DevelopmentValue, w.ProcessingValueAdded,
        w.FoodEaten, w.HealthRestored, CreatedItems = SortedLongs(w.CreatedItems), ConsumedItems = SortedLongs(w.ConsumedItems),
        Amounts = SortedLongs(w.Stock.Amounts), Credits = SortedCredits(w.Stock.Credits),
        Ants = w.Ants.Select(a => new { a.Id, x = a.Position.X, y = a.Position.Y, a.Health, a.Carry }).ToArray(),
        EconomyEvents = w.EconomyEvents.Select(e => new { e.Tick, e.Metric, e.Item, e.Quantity, e.AntId, e.Species, e.GroupId }).ToArray()
    };

    private static KeyValuePair<string, long>[] SortedLongs(ImmutableDictionary<string, long> source) =>
        source.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray();

    private static object[] SortedCredits(ImmutableDictionary<string, ImmutableArray<CreditLot>> source) =>
        source.OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => new object[] { x.Key, x.Value.Select(lot => new { lot.Quantity, lot.PointsPerUnit }).ToArray() })
            .ToArray();

    private static string Pascal(string field) => field switch {
        "reserve_policy" => "ReservePolicy", "ration_policy" => "RationPolicy", "labor_policy" => "LaborPolicy",
        "defense_policy" => "DefensePolicy", "admission_policy" => "AdmissionPolicy", _ => ""
    };

    private static bool Throws(Action action, string fragment) {
        try { action(); return false; }
        catch (Exception ex) { return ex.Message.Contains(fragment, StringComparison.Ordinal); }
    }
}
