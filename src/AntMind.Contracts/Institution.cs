// VERSION=0.1.0
using System.Collections.Immutable;
using System.Text.Json.Serialization;
namespace AntMind.Contracts;

// 五字段封闭白名单、全局唯一档位整数码表与默认档位。Contracts 层无 UI/MCP 依赖，供 Core/Runtime/Programs/Mcp 共享。
// 纯整数、纯函数：不读 RandomState / 子种子，不落盘、不写持久状态。
public static class InstitutionCatalog {
    public const int MaxHistory = 32;
    public const int MaxReasonLength = 128;
    public const string MigratedReason = "migrated-from-save5";

    public static readonly ImmutableArray<string> Fields =
        ["reserve_policy", "ration_policy", "labor_policy", "defense_policy", "admission_policy"];

    private static readonly ImmutableArray<string>[] TierTable = [
        ["strict", "balanced", "loose"],
        ["queen_first", "weakest_first", "worker_first", "equal"],
        ["balanced", "haul_first", "build_first", "guard_first"],
        ["early", "measured", "late", "never"],
        ["fresh_first", "bulk_first", "near_first", "type_first"]
    ];
    private static readonly string[] Defaults = ["balanced", "weakest_first", "balanced", "measured", "fresh_first"];
    private static readonly string[] Prefix = ["reserve", "ration", "labor", "defense", "admission"];

    private static int FieldIndex(string field) {
        for (var i = 0; i < Fields.Length; i++)
            if (string.Equals(Fields[i], field, StringComparison.Ordinal)) return i;
        return -1;
    }
    public static int FieldCode(string field) => FieldIndex(field) + 1;
    public static string FieldName(int code) => code is >= 1 and <= 5 ? Fields[code - 1] : "";
    public static string Default(string field) { var i = FieldIndex(field); return i < 0 ? "" : Defaults[i]; }
    public static bool IsField(string field) => FieldIndex(field) >= 0;
    public static ImmutableArray<string> Tiers(string field) { var i = FieldIndex(field); return i < 0 ? [] : TierTable[i]; }
    public static bool IsTier(string field, string tier) {
        var i = FieldIndex(field); if (i < 0) return false;
        foreach (var t in TierTable[i]) if (string.Equals(t, tier, StringComparison.Ordinal)) return true;
        return false;
    }
    // 全局唯一档位码 1..19（按 Fields 顺序、各字段 Tiers 顺序编号），0=未知。
    public static int TierCode(string field, string tier) {
        var i = FieldIndex(field); if (i < 0) return 0;
        var offset = 0; for (var k = 0; k < i; k++) offset += TierTable[k].Length;
        var list = TierTable[i];
        for (var j = 0; j < list.Length; j++)
            if (string.Equals(list[j], tier, StringComparison.Ordinal)) return offset + j + 1;
        return 0;
    }
    public static string TierName(int code) {
        if (code < 1) return "";
        var offset = 0;
        for (var i = 0; i < TierTable.Length; i++) {
            if (code <= offset + TierTable[i].Length) return TierTable[i][code - offset - 1];
            offset += TierTable[i].Length;
        }
        return "";
    }
    public static string TierField(int code) {
        if (code < 1) return "";
        var offset = 0;
        for (var i = 0; i < TierTable.Length; i++) {
            if (code <= offset + TierTable[i].Length) return Fields[i];
            offset += TierTable[i].Length;
        }
        return "";
    }
    private static readonly ImmutableDictionary<string, int> BuiltinCodes = BuildBuiltins();
    private static ImmutableDictionary<string, int> BuildBuiltins() {
        var map = ImmutableDictionary<string, int>.Empty.WithComparers(StringComparer.Ordinal);
        for (var i = 0; i < Fields.Length; i++)
            foreach (var t in TierTable[i])
                map = map.SetItem(Prefix[i] + "_" + t, TierCode(Fields[i], t));
        return map;
    }
    public static ImmutableArray<string> BuiltinNames =>
        BuiltinCodes.Keys.OrderBy(x => x, StringComparer.Ordinal).ToImmutableArray();
    public static bool TryBuiltinCode(string name, out int code) => BuiltinCodes.TryGetValue(name, out code);

    // 本 Tick 只读参数快照：纯标量、值相等，不读 RandomState / 子种子，不落盘。
    public static InstitutionParameters Resolve(InstitutionState s, int initialStock) {
        var (scout, hauler, soldier, chef, builder) = s.LaborPolicy switch {
            "haul_first" => (100, 125, 100, 100, 80),
            "build_first" => (100, 80, 100, 100, 125),
            "guard_first" => (80, 100, 125, 110, 100),
            _ => (100, 100, 100, 100, 100)
        };
        return new InstitutionParameters(
            MinimumReserve: Math.Clamp(s.ReservePolicy switch { "strict" => 60, "loose" => 20, _ => 40 }, 0, initialStock),
            RationRank: s.RationPolicy switch { "queen_first" => 0, "weakest_first" => 1, "worker_first" => 2, "equal" => 3, _ => 1 },
            AdmissionRank: s.AdmissionPolicy switch { "fresh_first" => 0, "bulk_first" => 1, "near_first" => 2, "type_first" => 3, _ => 0 },
            ScoutWeight: scout, HaulerWeight: hauler, SoldierWeight: soldier,
            ChefWeight: chef, BuilderWeight: builder,
            ThreatSwitchPercent: s.DefensePolicy switch { "early" => 25, "late" => 75, "never" => 101, _ => 50 });
    }
}

// 五字段快照（History revision.fields 与 Snapshot 只读投影共用）。显式属性确保 snake_case 反序列化稳定。
public sealed record InstitutionFields {
    [JsonPropertyName("reserve_policy")] public string ReservePolicy { get; init; } = "balanced";
    [JsonPropertyName("ration_policy")] public string RationPolicy { get; init; } = "weakest_first";
    [JsonPropertyName("labor_policy")] public string LaborPolicy { get; init; } = "balanced";
    [JsonPropertyName("defense_policy")] public string DefensePolicy { get; init; } = "measured";
    [JsonPropertyName("admission_policy")] public string AdmissionPolicy { get; init; } = "fresh_first";
    public static InstitutionFields Default => new();
    public string Get(string field) => field switch {
        "reserve_policy" => ReservePolicy, "ration_policy" => RationPolicy, "labor_policy" => LaborPolicy,
        "defense_policy" => DefensePolicy, "admission_policy" => AdmissionPolicy, _ => ""
    };
    public InstitutionFields With(string field, string tier) => field switch {
        "reserve_policy" => this with { ReservePolicy = tier },
        "ration_policy" => this with { RationPolicy = tier },
        "labor_policy" => this with { LaborPolicy = tier },
        "defense_policy" => this with { DefensePolicy = tier },
        "admission_policy" => this with { AdmissionPolicy = tier },
        _ => this
    };
    public void Validate() {
        if (!InstitutionCatalog.IsTier("reserve_policy", ReservePolicy) ||
            !InstitutionCatalog.IsTier("ration_policy", RationPolicy) ||
            !InstitutionCatalog.IsTier("labor_policy", LaborPolicy) ||
            !InstitutionCatalog.IsTier("defense_policy", DefensePolicy) ||
            !InstitutionCatalog.IsTier("admission_policy", AdmissionPolicy))
            throw new ArgumentException("Invalid institution state.");
    }
    public ImmutableArray<int> TierCodes => [
        InstitutionCatalog.TierCode("reserve_policy", ReservePolicy),
        InstitutionCatalog.TierCode("ration_policy", RationPolicy),
        InstitutionCatalog.TierCode("labor_policy", LaborPolicy),
        InstitutionCatalog.TierCode("defense_policy", DefensePolicy),
        InstitutionCatalog.TierCode("admission_policy", AdmissionPolicy)
    ];
}

public sealed record InstitutionRevision {
    [JsonPropertyName("version")] public int Version { get; init; } = 1;
    [JsonPropertyName("applied_tick")] public long AppliedTick { get; init; }
    [JsonPropertyName("reason")] public string Reason { get; init; } = InstitutionCatalog.MigratedReason;
    [JsonPropertyName("fields")] public InstitutionFields Fields { get; init; } = InstitutionFields.Default;
}

// 全群共享、带版本的持久制度状态。Version 严格单调递增；History 上限 32，超出丢最旧。
public sealed record InstitutionState {
    [JsonPropertyName("reserve_policy")] public string ReservePolicy { get; init; } = "balanced";
    [JsonPropertyName("ration_policy")] public string RationPolicy { get; init; } = "weakest_first";
    [JsonPropertyName("labor_policy")] public string LaborPolicy { get; init; } = "balanced";
    [JsonPropertyName("defense_policy")] public string DefensePolicy { get; init; } = "measured";
    [JsonPropertyName("admission_policy")] public string AdmissionPolicy { get; init; } = "fresh_first";
    [JsonPropertyName("version")] public int Version { get; init; } = 1;
    [JsonPropertyName("applied_tick")] public long AppliedTick { get; init; }
    [JsonPropertyName("reason")] public string Reason { get; init; } = InstitutionCatalog.MigratedReason;
    [JsonPropertyName("history")] public ImmutableArray<InstitutionRevision> History { get; init; } = DefaultHistory();

    public static ImmutableArray<InstitutionRevision> DefaultHistory() =>
        [new InstitutionRevision { Version = 1, AppliedTick = 0, Reason = InstitutionCatalog.MigratedReason, Fields = InstitutionFields.Default }];
    public static InstitutionState Initial(long tick) => new() {
        AppliedTick = tick,
        History = [new InstitutionRevision { Version = 1, AppliedTick = tick, Reason = InstitutionCatalog.MigratedReason, Fields = InstitutionFields.Default }]
    };
    public InstitutionFields Fields => new() {
        ReservePolicy = ReservePolicy, RationPolicy = RationPolicy, LaborPolicy = LaborPolicy,
        DefensePolicy = DefensePolicy, AdmissionPolicy = AdmissionPolicy
    };
    public void Validate() {
        Fields.Validate();
        if (Version < 1 || AppliedTick < 0 || string.IsNullOrEmpty(Reason) || Reason.Length > InstitutionCatalog.MaxReasonLength ||
            History.IsDefault || History.Length < 1 || History.Length > InstitutionCatalog.MaxHistory)
            throw new ArgumentException("Invalid institution state.");
        foreach (var r in History) {
            if (r is null || r.Version < 1 || r.AppliedTick < 0 || string.IsNullOrEmpty(r.Reason) ||
                r.Reason.Length > InstitutionCatalog.MaxReasonLength || r.Fields is null)
                throw new ArgumentException("Invalid institution state.");
            r.Fields.Validate();
        }
    }
}

// 每 Tick 由 GameSession.Advance 注入的只读执行上下文，[JsonIgnore]、不落盘、纯标量值相等。
public sealed record InstitutionParameters(
    int MinimumReserve, int RationRank, int AdmissionRank,
    int ScoutWeight, int HaulerWeight, int SoldierWeight, int ChefWeight, int BuilderWeight,
    int ThreatSwitchPercent) {
    public static InstitutionParameters Default => InstitutionCatalog.Resolve(new InstitutionState(), 100);
    public int Weight(string species) => species switch {
        AntSpeciesIds.Scout => ScoutWeight, AntSpeciesIds.Hauler => HaulerWeight,
        AntSpeciesIds.Soldier => SoldierWeight, AntSpeciesIds.Chef => ChefWeight,
        AntSpeciesIds.Builder => BuilderWeight, _ => 100
    };
}

// content/world.json 的 institutions 段（默认档位）。字段名按契约 snake_case。
public sealed record InstitutionConfig {
    [JsonPropertyName("reserve_policy")] public string ReservePolicy { get; init; } = "balanced";
    [JsonPropertyName("ration_policy")] public string RationPolicy { get; init; } = "weakest_first";
    [JsonPropertyName("labor_policy")] public string LaborPolicy { get; init; } = "balanced";
    [JsonPropertyName("defense_policy")] public string DefensePolicy { get; init; } = "measured";
    [JsonPropertyName("admission_policy")] public string AdmissionPolicy { get; init; } = "fresh_first";
    public void Validate() {
        if (!InstitutionCatalog.IsTier("reserve_policy", ReservePolicy) ||
            !InstitutionCatalog.IsTier("ration_policy", RationPolicy) ||
            !InstitutionCatalog.IsTier("labor_policy", LaborPolicy) ||
            !InstitutionCatalog.IsTier("defense_policy", DefensePolicy) ||
            !InstitutionCatalog.IsTier("admission_policy", AdmissionPolicy))
            throw new ArgumentException("Invalid institutions.");
    }
    public InstitutionState Initial(long tick) {
        var fields = new InstitutionFields {
            ReservePolicy = ReservePolicy, RationPolicy = RationPolicy, LaborPolicy = LaborPolicy,
            DefensePolicy = DefensePolicy, AdmissionPolicy = AdmissionPolicy
        };
        return new InstitutionState {
            ReservePolicy = ReservePolicy, RationPolicy = RationPolicy, LaborPolicy = LaborPolicy,
            DefensePolicy = DefensePolicy, AdmissionPolicy = AdmissionPolicy,
            Version = 1, AppliedTick = tick, Reason = InstitutionCatalog.MigratedReason,
            History = [new InstitutionRevision { Version = 1, AppliedTick = tick, Reason = InstitutionCatalog.MigratedReason, Fields = fields }]
        };
    }
}
