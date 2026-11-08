using System.Collections.Immutable;
using AntMind.Contracts;

namespace AntMind.Core;

public sealed record MilestoneDefinition(string Id, string Title, string Description, string Metric, long Target,
    ImmutableArray<string> Prerequisites = default, int TimeLimitTicks = 0) {
    public ImmutableArray<string> Prerequisites { get; init; } = Prerequisites.IsDefault ? [] : Prerequisites;
}
public sealed record MilestoneProgress(long StartedTick, long? CompletedTick = null, bool Failed = false);

// Declarative objectives use permitted colony metrics, not hidden food counts or positions.
public sealed class MilestoneRule : IWorldRule {
    public string Id => "core.milestones";
    public int Order => 50;
    public static ImmutableArray<MilestoneDefinition> Defaults { get; } = [
        new("discover-5", "探路先锋", "发现 5 处天然食物", "discovered_food", 5),
        new("deliver-50", "稳定供给", "累计送回 50 份食物", "delivered", 50),
        new("population-20", "蚁群初成", "让蚁群达到 20 只", "population", 20, ["deliver-50"]),
        new("network-3", "网络时代", "蚁群网络达到 Lv.3", "network_level", 3, ["discover-5"]),
        new("supply-sprint", "运输冲刺", "解锁后 10 分钟内累计总交付达到 200", "delivered", 200, ["deliver-50"], 12000)
    ];
    private static readonly HashSet<string> Metrics = ["discovered_food", "delivered", "population", "network_level", "brain_level"];
    public static void Validate(ImmutableArray<MilestoneDefinition> definitions) {
        if (definitions.IsDefault || definitions.Length > 64 || definitions.Any(x => x is null ||
            string.IsNullOrWhiteSpace(x.Id) || x.Id.Length > 80 || string.IsNullOrWhiteSpace(x.Title) || x.Title.Length > 80 ||
            string.IsNullOrWhiteSpace(x.Description) || x.Description.Length > 256 || !Metrics.Contains(x.Metric) ||
            x.Target < 1 || x.TimeLimitTicks < 0 || x.Prerequisites.IsDefault || x.Prerequisites.Length > 64) ||
            definitions.Select(x=>x.Id).Distinct(StringComparer.Ordinal).Count() != definitions.Length)
            throw new ArgumentException("Invalid milestone definitions.");
        _ = Ordered(definitions).ToArray();
    }
    private static IEnumerable<MilestoneDefinition> Ordered(ImmutableArray<MilestoneDefinition> definitions) {
        var byId = definitions.ToDictionary(x=>x.Id, StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<MilestoneDefinition>();
        void Visit(string id) {
            if (visited.Contains(id)) return;
            if (!byId.TryGetValue(id, out var definition) || !visiting.Add(id)) throw new ArgumentException("Missing or cyclic milestone prerequisite.");
            foreach (var dependency in definition.Prerequisites) Visit(dependency);
            visiting.Remove(id); visited.Add(id); result.Add(definition);
        }
        foreach (var definition in definitions) Visit(definition.Id);
        return result;
    }
    public static long Current(WorldState world, string metric) => metric switch {
        "discovered_food" => world.Objects.All<ResourceView>().Count(x=>x.IsDiscovered && x.Origin == ResourceOrigin.Natural),
        "delivered" => world.Delivered, "population" => world.Ants.Length,
        "network_level" => world.NetworkLevel, "brain_level" => world.BrainLevel,
        _ => throw new ArgumentException("Unknown milestone metric.")
    };
    public WorldState Apply(WorldState world) {
        var progress = world.MilestoneProgress;
        foreach (var definition in Ordered(world.Config.Milestones)) {
            if (!definition.Prerequisites.All(id=>progress.TryGetValue(id, out var parent) && parent.CompletedTick is not null)) continue;
            var entry = progress.GetValueOrDefault(definition.Id) ?? new(world.Tick);
            if (entry.CompletedTick is null && !entry.Failed) {
                // Exact deadline is inclusive; completion after the deadline cannot turn failure into success.
                if (definition.TimeLimitTicks > 0 && world.Tick > entry.StartedTick + definition.TimeLimitTicks) entry = entry with { Failed = true };
                else if (Current(world, definition.Metric) >= definition.Target) entry = entry with { CompletedTick = world.Tick };
            }
            progress = progress.SetItem(definition.Id, entry);
        }
        return world with { MilestoneProgress = progress };
    }
    public static ImmutableArray<MilestoneSnapshot> Snapshot(WorldState world) => world.Config.Milestones.Select(definition => {
        var entry = world.MilestoneProgress.GetValueOrDefault(definition.Id);
        return new MilestoneSnapshot(definition.Id, definition.Title, definition.Description, definition.Metric, definition.Target,
            Math.Min(definition.Target, Current(world, definition.Metric)),
            entry is null ? "locked" : entry.CompletedTick is not null ? "completed" : entry.Failed ? "failed" : "active",
            entry?.StartedTick, entry?.CompletedTick,
            entry is not null && definition.TimeLimitTicks > 0 ? entry.StartedTick + definition.TimeLimitTicks : null);
    }).ToImmutableArray();
}
