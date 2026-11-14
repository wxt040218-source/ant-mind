using System.Collections.Immutable;
using AntMind.Contracts;
using AntMind.Core;
using AntMind.Programs;
namespace AntMind.Runtime;
public static class GameRules {
    internal static AntView PrepareProgramActivation(AntView ant,int version)=>ant with {
        ProgramVersion=version,Target=null,Intent=AntAction.Wait,NextDecisionTick=0,WorkTicksRemaining=0,HealUntilTick=0,EatHolderId=-1,RequestQueenInfo=false,ReportDiscovery=false,
        Memory=Enumerable.Repeat(0,AntDslCompiler.MemorySlots).ToImmutableArray(),LastDecisionBranch="program-replaced",LastInstructionSpent=0,LastError=null
    };
    public static Simulation Create(SpeciesProgramCatalog catalog,IEnumerable<IWorldRule>? extra=null)=>new(new IWorldRule[] {
        new ExplorationExperienceRule(),new AntExperienceRule(),new ActivitySamplingRule(),new SourceGrowthRule(),new ProductionActionRule(),new WorkRule(),new EconomyStatisticsRule(),new EnemySpawnRule(),new LocalObservationRule(),new QueenCommunicationRule(),new ProgramRule(catalog),new MovementRule(),new EnemyMovementRule(),new ClaimRule(),new FollowRule(),new CombatRule(),new InteractionRule(),new EatingRule(),new ResourceRefreshRule(),new ResourceDiscoveryRule(),new ClaimCleanupRule(),new MilestoneRule(),new NestConstructionRule()
    }.Concat(extra??[]));
    public static WorldState Activate(WorldState world,SpeciesProgramCatalog catalog) {
        var activated = catalog.ActivatePending();
        if (activated.Count > 0) {
            var ants = world.Ants.ToBuilder();
            for (var i = 0; i < ants.Count; i++) {
                var ant = ants[i];
                if(!activated.TryGetValue(ant.Species,out var version)) continue;
                ants[i] = PrepareProgramActivation(ant,version);
            }
            world = world with { Ants = ants.ToImmutable(), ActiveProgramVersions = world.ActiveProgramVersions.SetItems(activated) };
        }
        return world;
    }
}
