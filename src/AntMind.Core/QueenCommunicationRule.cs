using System.Collections.Immutable;
using AntMind.Contracts;
namespace AntMind.Core;

// Passive sensing records only this ant's observations; it never sends or downloads data.
public sealed class LocalObservationRule : IWorldRule {
    public string Id => "core.local-observation";
    public int Order => 5;
    public WorldState Apply(WorldState world) {
        var ants = world.Ants.ToBuilder();
        for (var i = 0; i < ants.Count; i++) {
            var ant = ants[i];
            var vision = (long)world.Config.AntType(ant.Species).VisionCells * Point.UnitsPerCell;
            bool Visible(Point p) => Math.Abs((long)p.X-ant.Position.X) <= vision && Math.Abs((long)p.Y-ant.Position.Y) <= vision;
            var reports = ant.ResourceReports.ToDictionary(x => x.Id);
            foreach (var known in reports.Values.Concat(ant.QueenKnowledge).ToArray())
                if (Visible(known.Position) && !world.Resources.Any(x => x.Id == known.Id))
                    reports[known.Id] = known with { Amount = 0, LastSeenTick = world.Tick };
            foreach (var item in world.Resources.Where(x => Visible(x.Position)))
                reports[item.Id] = item with { LastSeenTick = world.Tick };
            ants[i] = ant with { QueenConnected = QueenCommunicationRule.Connected(world, ant.Position),
                SourceReports=ant.SourceReports.Concat(world.Objects.All<SourceView>().Where(x=>Visible(x.Position)).Select(x=>x with {LastObservedTick=world.Tick})).GroupBy(x=>x.Id).Select(g=>g.Last()).OrderBy(x=>x.Id).ToImmutableArray(),
                BuildingReports=ant.BuildingReports.Concat(world.Objects.All<BuildingView>().Where(x=>Visible(x.Position)).Select(x=>x with {LastObservedTick=world.Tick})).GroupBy(x=>x.Id).Select(g=>g.Last()).OrderBy(x=>x.Id).ToImmutableArray(),
                ResourceReports = reports.Values.OrderBy(x => x.Id).ToImmutableArray() };
        }
        // S2：ObservedChambers 只按 Nest.Cells 的 Discovered 升序稳定投影，不新增 IWorldRule、不暴露 Cells。
        var observed = world.Nest.Cells.IsDefault || world.Nest.Cells.Length != NestLayout.CellCount
            ? world.ObservedChambers
            : Enumerable.Range(0, NestLayout.CellCount).Where(i => world.Nest.Cells[i].Discovered).ToImmutableArray();
        return world with { Ants = ants.ToImmutable(), ObservedChambers = observed };
    }
}

public sealed class QueenCommunicationRule : IWorldRule {
    public string Id => "core.queen-communication";
    public int Order => 15;
    public static bool Connected(WorldState world, Point position) {
        if (!world.IsTechnologyUnlocked("queen.communication")) return false;
        var config = world.Config;
        var nest = Point.FromCells(config.Width / 2, config.Height / 2);
        long dx = (long)position.X-nest.X, dy = (long)position.Y-nest.Y;
        long radius = (long)world.QueenRangeCells * Point.UnitsPerCell;
        return dx*dx + dy*dy <= radius*radius;
    }
    public WorldState Apply(WorldState world) {
        var sources=world.QueenSources.ToDictionary(x=>x.Id);var buildings=world.QueenBuildings.ToDictionary(x=>x.Id);
        var queen = world.QueenResources.ToDictionary(x => x.Id);
        var ants = world.Ants.ToBuilder();
        bool Allowed(AntView ant) => Connected(world, ant.Position) &&
            world.Tick-ant.LastQueenCommunicationTick >= InstitutionMechanics.DefenseCooldown(world);
        // All explicit uploads settle before downloads, in stable ant order.
        for (var i = 0; i < ants.Count; i++) {
            var ant = ants[i];
            if (!ant.ReportDiscovery) continue;
            if (Allowed(ant)) {
                foreach (var report in ant.ResourceReports.OrderBy(x => x.Id))
                    if (!queen.TryGetValue(report.Id, out var old) || report.LastSeenTick >= old.LastSeenTick)
                        queen[report.Id] = report;
                foreach(var s in ant.SourceReports)if(!sources.TryGetValue(s.Id,out var oldSource) || s.LastObservedTick>=oldSource.LastObservedTick)sources[s.Id]=s;
                foreach(var b in ant.BuildingReports)if(!buildings.TryGetValue(b.Id,out var oldBuilding) || b.LastObservedTick>=oldBuilding.LastObservedTick)buildings[b.Id]=b;
                ants[i] = ant with { SourceReports=[],BuildingReports=[],ResourceReports = [], QueenReportStatus = ActionStatus.Succeeded };
            } else ants[i] = ant with { QueenReportStatus = ActionStatus.Failed };
        }
        for (var i = 0; i < ants.Count; i++) {
            var ant = ants[i];
            if (ant.RequestQueenInfo)
                ant = Allowed(ant)
                    ? ant with {KnownTechnologyLevel=world.NetworkLevel,QueenSources=sources.Values.OrderBy(x=>x.Id).ToImmutableArray(),QueenBuildings=buildings.Values.OrderBy(x=>x.Id).ToImmutableArray(), QueenKnowledge = queen.Values.Where(x => x.Amount > 0).OrderBy(x => x.Id).ToImmutableArray(),
                        ReceivedOrder=world.Groups.FirstOrDefault(g=>g.Id==ant.GroupId),
                        QueenClaims=world.Claims.Where(c=>c.ExpiresTick>world.Tick && queen.ContainsKey(c.FoodId)).ToImmutableArray(),
                        QueenInfoTick = world.Tick, QueenRequestStatus = ActionStatus.Succeeded }
                    : ant with { QueenRequestStatus = ActionStatus.Failed };
            var succeeded = ant.ReportDiscovery && ant.QueenReportStatus == ActionStatus.Succeeded ||
                ant.RequestQueenInfo && ant.QueenRequestStatus == ActionStatus.Succeeded;
            ants[i] = ant with { LastQueenCommunicationTick = succeeded ? world.Tick : ant.LastQueenCommunicationTick,
                QueenConnected = Connected(world, ant.Position), RequestQueenInfo = false, ReportDiscovery = false };
        }
        return world with { QueenSources=sources.Values.OrderBy(x=>x.Id).ToImmutableArray(),QueenBuildings=buildings.Values.OrderBy(x=>x.Id).ToImmutableArray(),Ants = ants.ToImmutable(), QueenResources = queen.Values.OrderBy(x => x.Id).ToImmutableArray() };
    }
}
