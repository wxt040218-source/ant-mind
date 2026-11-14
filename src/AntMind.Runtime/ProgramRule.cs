using System.Collections.Immutable;
using AntMind.Contracts;
using AntMind.Core;
using AntMind.Programs;

namespace AntMind.Runtime;

public sealed class ProgramRule : IWorldRule {
    private readonly ImmutableDictionary<string, IAntProgram> programs;
    private readonly IAntProgram? fallback;
    private readonly SpeciesProgramCatalog? catalog;

    public ProgramRule(IAntProgram program) {
        fallback = program;
        programs = ImmutableDictionary<string, IAntProgram>.Empty.WithComparers(StringComparer.Ordinal);
    }

    public ProgramRule(SpeciesProgramCatalog catalog) {
        this.catalog = catalog;
        programs = ImmutableDictionary<string, IAntProgram>.Empty.WithComparers(StringComparer.Ordinal);
    }

    public ProgramRule(IReadOnlyDictionary<string, IAntProgram> programs, IAntProgram? fallback = null) {
        this.programs = programs.ToImmutableDictionary(StringComparer.Ordinal);
        this.fallback = fallback;
    }

    public string Id => "runtime.program";
    public int Order => 10;
    public WorldState Apply(WorldState world) {
        var ants = world.Ants.ToBuilder();
        for (int i = 0; i < ants.Count; i++) {
            var ant = ants[i];
            if (world.Tick < ant.NextDecisionTick || ant.HealUntilTick>0 || ant.WorkTicksRemaining>0) continue;
            var program = catalog?.ResolveActive(ant.Species);
            if (program is null && !programs.TryGetValue(ant.Species, out program))
                program = fallback ?? throw new InvalidOperationException($"No behavior program is registered for ant species '{ant.Species}'.");
            var perception=Perceive(world,ant);
            var level = world.BrainLevel - 1;
            var budget = new InstructionBudget(world.Config.BrainBudgets[level]);
            var memory = ant.Memory.IsDefault
                ? Enumerable.Repeat(0, AntDslCompiler.MemorySlots).ToImmutableArray()
                : ant.Memory;
            Decision decision;
            try {
                decision = program.Execute(perception, budget);
                if (decision.Spent > budget.Initial - budget.Remaining || decision.Spent < 0 ||
                    !decision.Memory.IsDefault && decision.Memory.Length != AntDslCompiler.MemorySlots)
                    decision = new("invalid-program-result", budget.Initial - budget.Remaining,
                        ant.Target, ant.Intent, Memory: memory, Error: "program_returned_invalid_result");
            } catch (Exception ex) {
                decision = new("program-error", budget.Initial - budget.Remaining,
                    ant.Target, ant.Intent, Memory: memory, Error: ex.GetType().Name);
            }
            var recent = ant.RecentDecisions.IsDefault
                ? ImmutableArray<AntDecisionLogEntry>.Empty
                : ant.RecentDecisions;
            var programVersion=catalog?.ActiveVersion(ant.Species)??ant.ProgramVersion;
            recent = recent.Add(new(world.Tick, programVersion, decision.Branch, decision.Spent, decision.Error));
            if (recent.Length > 20) recent = recent.RemoveAt(0);
            ants[i] = ant with { Target = decision.Target, Intent = decision.Action, AttackTargetId = decision.AttackTargetId,
                Production=decision.Production,PickupFoodId=decision.PickupFoodId,EatFood=decision.EatFood,EatHolderId=decision.EatHolderId,FollowAntId=decision.FollowAntId,FollowDistance=decision.FollowDistance,
                ClaimFoodId=decision.ClaimFoodId,ClaimQuantity=decision.ClaimQuantity,ClaimDuration=decision.ClaimDuration,ReleaseClaim=decision.ReleaseClaim,
                RequestQueenInfo = decision.RequestQueenInfo, ReportDiscovery = decision.ReportDiscovery,
                ActionStatus = decision.Branch == "wait" ? ActionStatus.Succeeded : ant.ActionStatus,
                ActionResult = decision.Branch == "wait" ? "waiting" : ant.ActionResult,
                Memory = decision.Memory.IsDefault ? memory : decision.Memory,
                LastDecisionBranch = decision.Branch, LastInstructionSpent = decision.Spent,
                LastError = decision.Error, RecentDecisions = recent,
                ProgramVersion=programVersion,
                ChamberX=decision.ChamberX,ChamberY=decision.ChamberY,ChamberTypeCode=decision.ChamberTypeCode,ChamberAction=decision.ChamberAction,
                NextDecisionTick = world.Tick + world.Config.BrainIntervals[level] };
        }
        return world with { Ants = ants.ToImmutable() };
    }
    public static Perception Perceive(WorldState world,AntView ant) {
        var species=world.Config.AntType(ant.Species);var nest=Point.FromCells(world.Config.Width/2,world.Config.Height/2);
        var nearby = world.Resources.Where(r =>
            Math.Abs((long)r.Position.X - ant.Position.X) <= (long)species.VisionCells * Point.UnitsPerCell &&
            Math.Abs((long)r.Position.Y - ant.Position.Y) <= (long)species.VisionCells * Point.UnitsPerCell).ToImmutableArray();
        return new Perception(ant.Id, world.Tick, ant.Position, nest, ant.Carry, ant.Target,
            world.Config.Width, world.Config.Height, nearby,
            ant.Species, world.CarryCapacity(ant.Species), species.VisionCells,
            ant.Intent, ant.Memory.IsDefault ? Enumerable.Repeat(0, AntDslCompiler.MemorySlots).ToImmutableArray() : ant.Memory,
            QueenCommunicationRule.Connected(world, ant.Position),
            ant.QueenKnowledge, ant.QueenInfoTick, ant.ActionStatus, ant.QueenRequestStatus, ant.QueenReportStatus) {
                Level=ant.Level,Experience=ant.Experience,ExperienceToNext=ant.Level==3?0:world.Config.Veterancy.Thresholds[ant.Level]-ant.Experience,MovePercent=world.Config.Veterancy.MovePercent[ant.Level-1],WorkPercent=world.Config.Veterancy.WorkPercent[ant.Level-1],AttackDamage=AntProgression.AttackDamage(world,ant),TechnologyLevel=ant.KnownTechnologyLevel,Recipes=world.Config.Production.Enabled?world.Config.Production.Recipes:[],Sources=world.Objects.All<SourceView>().Where(s=>CombatMath.Distance(s.Position,ant.Position)<=species.VisionCells*1000).ToImmutableArray(),
                BuildingTypes=world.Config.Production.Buildings,Buildings=world.Objects.All<BuildingView>().Where(b=>CombatMath.Distance(b.Position,ant.Position)<=species.VisionCells*1000).ToImmutableArray(),QueenSources=ant.QueenSources,QueenBuildings=ant.QueenBuildings,
                Health=ant.Health, MaxHealth=ant.MaxHealth, CarryFood=ant.CarryFood,
                GroupId=ant.GroupId, Order=ant.ReceivedOrder, QueenClaims=ant.QueenClaims,
                FoodDefinitions=world.Config.Foods,
                NestStock=CombatMath.Distance(ant.Position,nest)<=Point.UnitsPerCell?world.Stock.Amounts:ImmutableDictionary<string,long>.Empty,
                NearbyAllies=world.Ants.Where(a=>a.Id!=ant.Id && CombatMath.Distance(a.Position,ant.Position)<=species.VisionCells*Point.UnitsPerCell)
                    .Select(a=>new AllyView(a.Id,a.Species,a.GroupId,a.Position,a.Health,a.MaxHealth)).ToImmutableArray(),
                NearbyClaims=world.Claims.Where(c=>c.ExpiresTick>world.Tick && nearby.Any(f=>f.Id==c.FoodId)).ToImmutableArray(),
                NearbyEnemies=world.Enemies.Where(e=>CombatMath.Distance(e.Position,ant.Position)<=species.VisionCells*Point.UnitsPerCell).ToImmutableArray(),
                InstitutionVersion = world.Institution.Version,
                InstitutionTierCodes = world.Institution.Fields.TierCodes,
                NestCapacity = world.Config.NestLayout.NestCapacityBase + world.Nest.CapacityBonus,
                ObservedChamberViews = world.ObservedChambers.IsDefault ? [] : world.ObservedChambers
                    .Where(i => i >= 0 && i < NestLayout.CellCount && world.Nest.Cells[i].Discovered)
                    .Select(i => ChamberView.From(i, world.Nest.Cells[i], world.Nest.ConnectedMask[i])).ToImmutableArray()
            };
    }

}
