// VERSION=0.1.0
using System.Collections.Immutable;
using System.Text.Json.Serialization;
namespace AntMind.Contracts;

// 地下巢穴 9x9 存档形状（S1 只定形状与默认布局，BFS/施工规则留 S2）。JSON 名按契约 snake_case。
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

public sealed record NestLayout {
    public const int GridWidth = 9;
    public const int GridHeight = 9;
    public const int CoreIndex = 40; // (4,4), index = y*9+x

    [JsonPropertyName("grid_width")] public int Width { get; init; } = GridWidth;
    [JsonPropertyName("grid_height")] public int Height { get; init; } = GridHeight;
    [JsonPropertyName("cells")] public ImmutableArray<ChamberCell> Cells { get; init; } = DefaultCells();
    [JsonPropertyName("connected_mask")] public ImmutableArray<bool> ConnectedMask { get; init; } = DefaultMask();
    [JsonPropertyName("capacity_bonus")] public int CapacityBonus { get; init; }

    public static ImmutableArray<ChamberCell> DefaultCells() {
        var cells = ImmutableArray.CreateBuilder<ChamberCell>(GridWidth * GridHeight);
        for (var i = 0; i < GridWidth * GridHeight; i++)
            cells.Add(new ChamberCell {
                X = i % GridWidth, Y = i / GridWidth,
                ChamberType = i == CoreIndex ? "core" : "empty",
                Phase = i == CoreIndex ? "active" : "planned",
                Discovered = i == CoreIndex
            });
        return cells.MoveToImmutable();
    }
    public static ImmutableArray<bool> DefaultMask() {
        var mask = ImmutableArray.CreateBuilder<bool>(GridWidth * GridHeight);
        for (var i = 0; i < GridWidth * GridHeight; i++) mask.Add(i == CoreIndex);
        return mask.MoveToImmutable();
    }
    public static NestLayout Default => new();
    public void Validate() {
        if (Width != GridWidth || Height != GridHeight || Cells.IsDefault || Cells.Length != GridWidth * GridHeight ||
            ConnectedMask.IsDefault || ConnectedMask.Length != GridWidth * GridHeight || CapacityBonus < 0)
            throw new ArgumentException("Invalid nest layout.");
        var core = 0;
        for (var i = 0; i < Cells.Length; i++) {
            var c = Cells[i];
            if (c is null || c.X != i % GridWidth || c.Y != i / GridWidth || c.Level is < 1 or > 3 ||
                c.ConstructionWork < 0 || c.WorkTarget < 0 || c.WoodReserved < 0 ||
                c.ChamberType is not ("empty" or "core" or "granary" or "nursery" or "barracks" or "workshop" or "corridor") ||
                c.Phase is not ("planned" or "digging" or "active" or "cancelled"))
                throw new ArgumentException("Invalid nest layout.");
            if (c.ChamberType == "core") {
                core++;
                if (i != CoreIndex || c.Phase != "active" || !c.Discovered || !ConnectedMask[i])
                    throw new ArgumentException("Invalid nest layout.");
            }
        }
        if (core != 1) throw new ArgumentException("Invalid nest layout.");
        for (var i = 0; i < ConnectedMask.Length; i++)
            if (i != CoreIndex && ConnectedMask[i]) throw new ArgumentException("Invalid nest layout.");
    }
}

// content/world.json 的 nestLayout / creatures 段（S1 只存默认形状，S2/S3 才消费）。
public sealed record NestLayoutConfig {
    [JsonPropertyName("grid_width")] public int GridWidth { get; init; } = 9;
    [JsonPropertyName("grid_height")] public int GridHeight { get; init; } = 9;
    [JsonPropertyName("core_x")] public int CoreX { get; init; } = 4;
    [JsonPropertyName("core_y")] public int CoreY { get; init; } = 4;
    [JsonPropertyName("max_level")] public int MaxLevel { get; init; } = 3;
    public void Validate() {
        if (GridWidth != 9 || GridHeight != 9 || CoreX != 4 || CoreY != 4 || MaxLevel != 3)
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
