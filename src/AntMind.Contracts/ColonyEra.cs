// VERSION=0.1.1
// 0.1.1 (S2): BFS 连通纯函数、整数效果聚合、成本/效果配置、Chamber 白名单与视图；CreatureConfig 保持 S1 形状。
using System.Collections.Immutable;
using System.Text.Json.Serialization;
namespace AntMind.Contracts;

// 地下巢穴 9x9 存档形状（S2 落地 BFS/施工/成本效果；S3 才消费 CreatureConfig）。JSON 名按契约 snake_case。
public sealed record ChamberCell {
    [JsonPropertyName("x")] public int X { get; init; }
    [JsonPropertyName("y")] public int Y { get; init; }
    [JsonPropertyName("chamber_type")] public string ChamberType { get; init; } = "empty";
    [JsonPropertyName("level")] public int Level { get; init; } = 1;
    [JsonPropertyName("phase")] public string Phase { get; init; } = "planned";
    [JsonPropertyName("construction_work")] public int ConstructionWork { get; init; }
    [JsonPropertyName("work_target")] public int WorkTarget { get; init; }
    [JsonPropertyName("discovered")] public bool Discovered { get; init; }
    [JsonPropertyName("wood_reserved")] public int WoodReserved { get; init; }
}

// 单格局部感知投影：Snapshot / Perception / inspect_chamber 共用，永不暴露 Cells 全量。
public sealed record ChamberView(int X, int Y, string ChamberType, int Level, string Phase,
    bool Connected, bool Discovered, int ConstructionWork, int WorkTarget) {
    // 未 discovered 一律返回同级 empty 形状，隐藏真实类型/等级/进度。
    public static ChamberView Hidden(int x, int y) => new(x, y, "empty", 1, "planned", false, false, 0, 0);
    public static ChamberView From(int index, ChamberCell cell, bool connected) =>
        new(cell.X, cell.Y, cell.ChamberType, cell.Level, cell.Phase, connected, cell.Discovered, cell.ConstructionWork, cell.WorkTarget);
}

// 封闭白名单 + 成本/类型码：纯静态表，供 DSL 编译期、命令层与 Checks 共用，不读世界状态。
public static class ChamberCatalog {
    public static readonly ImmutableArray<string> Types = ["empty", "core", "granary", "nursery", "barracks", "workshop", "corridor"];
    public static readonly ImmutableArray<string> Buildable = ["granary", "nursery", "barracks", "workshop", "corridor"];
    public static readonly ImmutableArray<string> Upgradeable = ["granary", "nursery", "barracks", "workshop"];
    public static readonly ImmutableArray<string> Phases = ["planned", "digging", "active", "cancelled"];
    // 整数码："" / 全部 = 0，其余按 Types 序 1..7。DSL 是纯 int，字符串字面量编译期转成码。
    public static int TypeCode(string type) => string.IsNullOrEmpty(type) ? 0 : Types.IndexOf(type) + 1;
    public static string TypeName(int code) => code < 1 || code > Types.Length ? "" : Types[code - 1];
    public static bool IsType(string type) => Types.Contains(type);
    public static bool IsBuildable(string type) => Buildable.Contains(type);
    public static bool IsUpgradeable(string type) => Upgradeable.Contains(type);
    public static int PhaseCode(string phase) => phase switch { "planned" => 1, "digging" => 2, "active" => 3, "cancelled" => 4, _ => 0 };
    // 可通行：phase=active 且非 empty（契约 §3）；planned/digging/cancelled/empty 一律不可通行。
    public static bool Passable(ChamberCell? c) => c is not null && c.Phase == "active" && c.ChamberType != "empty";
    // targetLevel：dig=1，upgrade 到 L2=2 / L3=3。成本/工时 = 表值 × 目标等级（契约 §5）。
    public static (int Wood, int Work) Cost(string type, int targetLevel, NestLayoutConfig config) =>
        (checked(config.WoodCost.GetValueOrDefault(type) * targetLevel),
         checked(config.WorkTarget.GetValueOrDefault(type) * targetLevel));
}

// 效果聚合：按 index 升序、仅连通且 active 的房间计入，整数 basis=100，不读 Tick/种子/WorldState（契约 §4）。
public sealed record NestEffects(int CapacityBonus, int EggCostBasis, int SoldierAttackBasis,
    int SoldierDefenseBasis, int ProcessSlots, int ProcessSpeedBasis) {
    public static NestEffects Aggregate(ImmutableArray<ChamberCell> cells, ImmutableArray<bool> connected, NestLayoutConfig config) {
        var capacity = 0; var slots = 0;
        var eggBasis = config.EggCostBasis; var attack = config.AttackBasis;
        var defense = config.DefenseBasis; var speed = config.ProcessSpeedBasis;
        var hasNursery = false; var hasBarracks = false; var hasWorkshop = false;
        if (cells.IsDefault || connected.IsDefault) return new(0, eggBasis, attack, defense, 0, speed);
        var n = Math.Min(cells.Length, connected.Length);
        for (var i = 0; i < n; i++) { // 行优先 index 升序，天然稳定序
            if (!connected[i]) continue;
            var c = cells[i];
            if (c is null || c.Phase != "active") continue;
            switch (c.ChamberType) {
                case "granary": capacity = checked(capacity + config.CapacityPerLevel * c.Level); break;
                case "nursery": {
                    var basis = config.EggCostBasis - config.EggCostStep * (c.Level - 1);
                    eggBasis = hasNursery ? Math.Min(eggBasis, basis) : basis; hasNursery = true; break;
                }
                case "barracks": {
                    var a = config.AttackBasis + config.AttackStep * c.Level;
                    var d = config.DefenseBasis + config.DefenseStep * c.Level;
                    attack = hasBarracks ? Math.Max(attack, a) : a;
                    defense = hasBarracks ? Math.Max(defense, d) : d; hasBarracks = true; break;
                }
                case "workshop": {
                    slots = checked(slots + config.ProcessSlotsPerLevel * c.Level);
                    var s = config.ProcessSpeedBasis + config.ProcessSpeedStep * (c.Level - 1);
                    speed = hasWorkshop ? Math.Max(speed, s) : s; hasWorkshop = true; break;
                }
            }
        }
        return new(capacity, eggBasis, attack, defense, slots, speed);
    }
}

public sealed record NestLayout {
    public const int GridWidth = 9;
    public const int GridHeight = 9;
    public const int CoreIndex = 40; // (4,4), index = y*9+x
    public const int CellCount = GridWidth * GridHeight;

    [JsonPropertyName("grid_width")] public int Width { get; init; } = GridWidth;
    [JsonPropertyName("grid_height")] public int Height { get; init; } = GridHeight;
    [JsonPropertyName("cells")] public ImmutableArray<ChamberCell> Cells { get; init; } = DefaultCells();
    [JsonPropertyName("connected_mask")] public ImmutableArray<bool> ConnectedMask { get; init; } = DefaultMask();
    [JsonPropertyName("capacity_bonus")] public int CapacityBonus { get; init; }

    public static ImmutableArray<ChamberCell> DefaultCells() {
        var cells = ImmutableArray.CreateBuilder<ChamberCell>(CellCount);
        for (var i = 0; i < CellCount; i++)
            cells.Add(new ChamberCell {
                X = i % GridWidth, Y = i / GridWidth,
                ChamberType = i == CoreIndex ? "core" : "empty",
                Phase = i == CoreIndex ? "active" : "planned",
                Discovered = i == CoreIndex
            });
        return cells.MoveToImmutable();
    }
    public static ImmutableArray<bool> DefaultMask() {
        var mask = ImmutableArray.CreateBuilder<bool>(CellCount);
        for (var i = 0; i < CellCount; i++) mask.Add(i == CoreIndex);
        return mask.MoveToImmutable();
    }
    public static NestLayout Default => new();

    // BFS 4 邻接（上→右→下→左，无对角），仅 active 非 empty 可通行，从 CoreIndex 起，纯函数（契约 §3）。
    public static ImmutableArray<bool> ComputeConnectedMask(ImmutableArray<ChamberCell> cells) {
        var mask = ImmutableArray.CreateBuilder<bool>(CellCount);
        for (var i = 0; i < CellCount; i++) mask.Add(false);
        if (cells.IsDefault || cells.Length != CellCount || !ChamberCatalog.Passable(cells[CoreIndex]))
            return mask.MoveToImmutable();
        var visited = new bool[CellCount];
        var queue = new Queue<int>();
        visited[CoreIndex] = true; queue.Enqueue(CoreIndex);
        while (queue.Count > 0) {
            var idx = queue.Dequeue();
            mask[idx] = true;
            var x = idx % GridWidth; var y = idx / GridWidth;
            if (y > 0) Enqueue((y - 1) * GridWidth + x);          // 上
            if (x < GridWidth - 1) Enqueue(y * GridWidth + x + 1); // 右
            if (y < GridHeight - 1) Enqueue((y + 1) * GridWidth + x); // 下
            if (x > 0) Enqueue(y * GridWidth + x - 1);             // 左
            void Enqueue(int n) {
                if (visited[n] || !ChamberCatalog.Passable(cells[n])) return;
                visited[n] = true; queue.Enqueue(n);
            }
        }
        return mask.MoveToImmutable();
    }

    // Only construction write points call this: recompute is never a per-tick unconditional pass.
    public NestLayout Recompute(NestLayoutConfig config) {
        var mask = ComputeConnectedMask(Cells);
        return this with { ConnectedMask = mask, CapacityBonus = NestEffects.Aggregate(Cells, mask, config).CapacityBonus };
    }

    public void Validate() {
        if (Width != GridWidth || Height != GridHeight || Cells.IsDefault || Cells.Length != CellCount ||
            ConnectedMask.IsDefault || ConnectedMask.Length != CellCount || CapacityBonus < 0)
            throw new ArgumentException("Invalid nest layout.");
        var core = 0;
        for (var i = 0; i < Cells.Length; i++) {
            var c = Cells[i];
            if (c is null || c.X != i % GridWidth || c.Y != i / GridWidth || c.Level is < 1 or > 3 ||
                c.ConstructionWork < 0 || c.WorkTarget < 0 || c.WoodReserved < 0 ||
                !ChamberCatalog.IsType(c.ChamberType) || !ChamberCatalog.Phases.Contains(c.Phase))
                throw new ArgumentException("Invalid nest layout.");
            if (c.ChamberType == "core") {
                core++;
                if (i != CoreIndex || c.Phase != "active" || !c.Discovered || !ConnectedMask[i])
                    throw new ArgumentException("Invalid nest layout.");
            }
        }
        if (core != 1) throw new ArgumentException("Invalid nest layout.");
        // S2：唯一 core 硬拒放宽为「ConnectedMask 必须与 BFS 逐位相等」，一挖房间即可存档。
        var expected = ComputeConnectedMask(Cells);
        for (var i = 0; i < ConnectedMask.Length; i++)
            if (ConnectedMask[i] != expected[i]) throw new ArgumentException("Invalid nest layout.");
    }
}

// content/world.json 的 nestLayout 段（成本/效果键 S2 补齐，schemaVersion 仍 16）。
public sealed record NestLayoutConfig {
    [JsonPropertyName("grid_width")] public int GridWidth { get; init; } = 9;
    [JsonPropertyName("grid_height")] public int GridHeight { get; init; } = 9;
    [JsonPropertyName("core_x")] public int CoreX { get; init; } = 4;
    [JsonPropertyName("core_y")] public int CoreY { get; init; } = 4;
    [JsonPropertyName("max_level")] public int MaxLevel { get; init; } = 3;
    [JsonPropertyName("chamber_types")] public ImmutableArray<string> ChamberTypes { get; init; } = ChamberCatalog.Types;
    [JsonPropertyName("wood_cost")] public ImmutableDictionary<string, int> WoodCost { get; init; } =
        ImmutableDictionary<string, int>.Empty.WithComparers(StringComparer.Ordinal)
            .Add("granary", 6).Add("nursery", 8).Add("barracks", 10).Add("workshop", 12).Add("corridor", 2);
    [JsonPropertyName("work_target")] public ImmutableDictionary<string, int> WorkTarget { get; init; } =
        ImmutableDictionary<string, int>.Empty.WithComparers(StringComparer.Ordinal)
            .Add("granary", 100).Add("nursery", 140).Add("barracks", 160).Add("workshop", 200).Add("corridor", 40);
    [JsonPropertyName("capacity_per_level")] public int CapacityPerLevel { get; init; } = 40;
    [JsonPropertyName("egg_cost_basis")] public int EggCostBasis { get; init; } = 100;
    [JsonPropertyName("egg_cost_step")] public int EggCostStep { get; init; } = 15;
    [JsonPropertyName("attack_basis")] public int AttackBasis { get; init; } = 100;
    [JsonPropertyName("attack_step")] public int AttackStep { get; init; } = 20;
    [JsonPropertyName("defense_basis")] public int DefenseBasis { get; init; } = 100;
    [JsonPropertyName("defense_step")] public int DefenseStep { get; init; } = 10;
    [JsonPropertyName("process_slots_per_level")] public int ProcessSlotsPerLevel { get; init; } = 1;
    [JsonPropertyName("process_speed_basis")] public int ProcessSpeedBasis { get; init; } = 100;
    [JsonPropertyName("process_speed_step")] public int ProcessSpeedStep { get; init; } = 25;
    [JsonPropertyName("nest_capacity_base")] public int NestCapacityBase { get; init; } = 1200;
    public void Validate() {
        if (GridWidth != 9 || GridHeight != 9 || CoreX != 4 || CoreY != 4 || MaxLevel != 3 ||
            ChamberTypes.IsDefault || !ChamberTypes.SequenceEqual(ChamberCatalog.Types) ||
            WoodCost is null || WorkTarget is null ||
            ChamberCatalog.Buildable.Any(t => !WoodCost.ContainsKey(t) || !WorkTarget.ContainsKey(t) ||
                WoodCost[t] < 1 || WorkTarget[t] < 1) ||
            WoodCost.Count != 5 || WorkTarget.Count != 5 ||
            CapacityPerLevel != 40 || EggCostBasis != 100 || EggCostStep != 15 ||
            AttackBasis != 100 || AttackStep != 20 || DefenseBasis != 100 || DefenseStep != 10 ||
            ProcessSlotsPerLevel != 1 || ProcessSpeedBasis != 100 || ProcessSpeedStep != 25 ||
            NestCapacityBase != 1200)
            throw new ArgumentException("Invalid nest layout.");
    }
}

public sealed record CreatureKindDefinition {
    [JsonPropertyName("kind")] public string Kind { get; init; } = "predator_beetle";
    [JsonPropertyName("health")] public int Health { get; init; } = 18;
    [JsonPropertyName("damage")] public int Damage { get; init; } = 2;
    [JsonPropertyName("speed")] public int Speed { get; init; } = 70;
    [JsonPropertyName("vision")] public int Vision { get; init; } = 8;
    [JsonPropertyName("carcass_food_type")] public string CarcassFoodType { get; init; } = "meat";
    [JsonPropertyName("corpse_min")] public int CorpseMin { get; init; } = 1;
    [JsonPropertyName("corpse_max")] public int CorpseMax { get; init; } = 3;
}

public sealed record CreatureConfig {
    [JsonPropertyName("creature_seed_derivation")] public string CreatureSeedDerivation { get; init; } = "colony-era-contract-2.1";
    [JsonPropertyName("spawn_interval_ticks")] public int SpawnIntervalTicks { get; init; } = 1200;
    [JsonPropertyName("enemy_cap")] public int EnemyCap { get; init; } = 3;
    [JsonPropertyName("migration_interval_ticks")] public int MigrationIntervalTicks { get; init; } = 2400;
    [JsonPropertyName("herd_size")] public int HerdSize { get; init; } = 3;
    [JsonPropertyName("herd_cap")] public int HerdCap { get; init; } = 9;
    [JsonPropertyName("graze_interval_ticks")] public int GrazeIntervalTicks { get; init; } = 600;
    [JsonPropertyName("graze_quantity")] public int GrazeQuantity { get; init; } = 1;
    [JsonPropertyName("blocked_cell_speed_basis")] public int BlockedCellSpeedBasis { get; init; } = 50;
    [JsonPropertyName("kinds")] public ImmutableArray<string> Kinds { get; init; } =
        ["predator_beetle", "pantry_moth", "road_blocker", "aphid", "migration_swarm"];
    [JsonPropertyName("kind_parameters")] public ImmutableArray<CreatureKindDefinition> KindParameters { get; init; } = DefaultKindParameters();
    [JsonPropertyName("default_kind")] public string DefaultKind { get; init; } = "predator_beetle";
    [JsonPropertyName("default_carcass_food_type")] public string DefaultCarcassFoodType { get; init; } = "meat";
    public static ImmutableArray<CreatureKindDefinition> DefaultKindParameters() => [
        new() { Kind = "predator_beetle", Health = 18, Damage = 2, Speed = 70, Vision = 8, CarcassFoodType = "meat", CorpseMin = 1, CorpseMax = 3 },
        new() { Kind = "pantry_moth", Health = 12, Damage = 0, Speed = 70, Vision = 0, CarcassFoodType = "fiber", CorpseMin = 1, CorpseMax = 2 },
        new() { Kind = "road_blocker", Health = 24, Damage = 0, Speed = 0, Vision = 0, CarcassFoodType = "fiber", CorpseMin = 1, CorpseMax = 3 },
        new() { Kind = "aphid", Health = 8, Damage = 0, Speed = 0, Vision = 0, CarcassFoodType = "meat", CorpseMin = 1, CorpseMax = 1 },
        new() { Kind = "migration_swarm", Health = 18, Damage = 2, Speed = 70, Vision = 8, CarcassFoodType = "meat", CorpseMin = 1, CorpseMax = 3 }
    ];
    public void Validate() {
        if (Kinds.IsDefault || Kinds.Length != 5 || Kinds.Distinct(StringComparer.Ordinal).Count() != 5 ||
            Kinds.Any(x => x is not ("predator_beetle" or "pantry_moth" or "road_blocker" or "aphid" or "migration_swarm")) ||
            DefaultKind != "predator_beetle" || DefaultCarcassFoodType != "meat" ||
            CreatureSeedDerivation != "colony-era-contract-2.1" || SpawnIntervalTicks != 1200 || EnemyCap != 3 ||
            MigrationIntervalTicks < 1 || HerdSize < 1 || HerdCap < HerdSize || GrazeIntervalTicks < 1 || GrazeQuantity < 1 ||
            BlockedCellSpeedBasis is < 1 or > 100 || KindParameters.IsDefault || KindParameters.Length != 5)
            throw new ArgumentException("Invalid creature kind.");
        for (var i = 0; i < KindParameters.Length; i++) {
            var p = KindParameters[i];
            if (p is null || p.Kind != Kinds[i] || p.Health < 1 || p.Damage < 0 || p.Speed < 0 || p.Vision < 0 ||
                p.CarcassFoodType is not ("meat" or "fiber") || p.CorpseMin < 1 || p.CorpseMax < p.CorpseMin)
                throw new ArgumentException("Invalid creature kind.");
        }
    }
}
