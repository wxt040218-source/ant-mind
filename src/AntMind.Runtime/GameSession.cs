using System.Collections.Immutable;
using System.Threading.Channels;
using AntMind.Contracts;
using AntMind.Core;
using AntMind.Programs;
namespace AntMind.Runtime;
// The actor owns all writes. Immutable snapshots are the only state crossing this boundary.
public sealed partial class GameSession : IAsyncDisposable {
    private sealed record Request(GameCommand? Command, TaskCompletionSource<object> Completion, bool Player, AgentPresence? Agent = null,Func<object>? Query=null);
    private readonly Channel<Request> queue = Channel.CreateBounded<Request>(new BoundedChannelOptions(256) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly Dictionary<Guid, (GameCommand Command, CommandResult Result)> receipts = [];
    private readonly Dictionary<Guid, (string Name, long Timestamp)> agentSessions = [];
    private readonly TimeProvider clock;
    private readonly Simulation simulation;
    private readonly SpeciesProgramCatalog programCatalog;
    private readonly Task actor;
    private WorldState world;
    private long revision;
    private bool paused = true;
    private int speed = 1;
    public GameSession(WorldConfig config, ulong seed, IEnumerable<IWorldRule>? extraRules = null, TimeProvider? timeProvider = null, string? savesPath = null) {
        saveDirectory=savesPath??SavedGames.DefaultDirectory;
        clock = timeProvider ?? TimeProvider.System;
        world = new MilestoneRule().Apply(Simulation.Create(config, seed));
        programCatalog = new(config.AntTypes.Select(x => x.Id));
        simulation = GameRules.Create(programCatalog,extraRules);
        eventJournal.Reset(worldId,AgentFrame());
        actor = RunAsync();
    }
    public async Task<Snapshot> ObserveAsync(bool player = false, CancellationToken ct = default) => (Snapshot)await Submit(null, player, ct);
    public async Task<CommandResult> ExecuteAsync(GameCommand command, CancellationToken ct = default) => (CommandResult)await Submit(command, false, ct);
    public async Task<bool> UpdateAgentPresenceAsync(AgentPresence agent, CancellationToken ct = default) =>
        (bool)await Submit(null, false, ct, agent);
    private async Task<object> Submit(GameCommand? command, bool player, CancellationToken ct, AgentPresence? agent = null) {
        var completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        await queue.Writer.WriteAsync(new(command, completion, player, agent), ct);
        // Once enqueued, cancellation only stops waiting; requestId permits retrieving the same result.
        return await completion.Task.WaitAsync(ct);
    }
    private async Task RunAsync() {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
        Task<bool> pulse = timer.WaitForNextTickAsync().AsTask();
        Task<bool> ready = queue.Reader.WaitToReadAsync().AsTask();
        try {
            while (true) {
                await Task.WhenAny(pulse, ready);
                if (ready.IsCompleted) {
                    if (!await ready) break;
                    // Bound command work so a continuous caller cannot starve simulation ticks.
                    for (int n = 0; n < 64 && queue.Reader.TryRead(out var request); n++) {
                        try { request.Completion.SetResult(request.Query is {} query?query():request.Agent is { } agent ? UpdatePresence(agent) : request.Command is null ? Project(request.Player) : ApplyAndNotify(request.Command)); }
                        catch (Exception ex) { request.Completion.SetException(ex); }
                    }
                    ready = queue.Reader.WaitToReadAsync().AsTask();
                }
                if (pulse.IsCompleted) {
                    if (!await pulse) break;
                    if (!paused) for (int i = 0; i < speed && !paused; i++) Advance();
                    pulse = timer.WaitForNextTickAsync().AsTask();
                }
            }
        } catch (Exception ex) {
            queue.Writer.TryComplete(ex);
            while (queue.Reader.TryRead(out var pending)) pending.Completion.TrySetException(ex);
            throw;
        }
    }
    private CommandResult Apply(GameCommand command) {
        CommandResult Reject(string code) => new(command.RequestId, false, code, revision);
        if (command.RequestId == Guid.Empty) return Reject("invalid_request_id");
        if(command.ExpectedWorldId is {} expectedWorld && expectedWorld!=worldId || requireWorldId && command.ExpectedWorldId!=worldId)return Reject("world_conflict");
        if (receipts.TryGetValue(command.RequestId, out var previous))
            return EquivalentCommands(previous.Command,command) ? previous.Result : Reject("request_id_reused");
        if (receipts.Count >= 100_000) return Reject("receipt_capacity");
        CommandResult result;
        if (command.ExpectedRevision == revision && (command.Kind is CommandKind.SaveGame or CommandKind.LoadGame || !world.IsDefeated && command.Kind is CommandKind.PlanBuild or CommandKind.CancelBuild)) {
            result=ApplyProductionCommand(command);receipts.Add(command.RequestId,(command,result));return result;
        }
        if(command.ExpectedRevision==revision && !world.IsDefeated && command.Kind is CommandKind.ConfigureGroup or CommandKind.CreateGroup or CommandKind.UpdateGroup or CommandKind.DeleteGroup or CommandKind.AssignAnt or CommandKind.AssignAnts or CommandKind.SetGroupParameter or CommandKind.RemoveGroupParameter or CommandKind.SetGroupPoint or CommandKind.RemoveGroupPoint) {
            result=ApplyGroupCommand(command); receipts.Add(command.RequestId,(command,result));return result;
        }
        if(command.ExpectedRevision==revision && !world.IsDefeated && command.Kind is CommandKind.SetInstitution or CommandKind.RollbackInstitution) {
            result=ApplyInstitutionCommand(command); receipts.Add(command.RequestId,(command,result));return result;
        }
        // 勘误 v0.1.1（S1.5）：4 个巢穴 CommandKind 的数值已冻结，但真实行为 S2 才接入。
        // S2 之前一律早拒「未实现」，杜绝合法请求落进通用 switch 空 case 后静默丢弃却返回
        // Accepted=true / 白推进 revision（假接受 no-op）。枚举值保留不删，S2 换成真路由。
        if (command.ExpectedRevision == revision && command.Kind is CommandKind.PlanChamber or CommandKind.DigChamber or CommandKind.UpgradeChamber or CommandKind.CancelChamber) {
            result=Reject("not_implemented");receipts.Add(command.RequestId,(command,result));return result;
        }
        if (command.ExpectedRevision != revision) result = Reject("revision_conflict");
        else if (world.IsDefeated && command.Kind is not (CommandKind.Pause or CommandKind.Speed or CommandKind.ApplyProgram or CommandKind.RollbackProgram)) result = Reject("colony_defeated");
        else if (!Enum.IsDefined(command.Kind) ||
            command.Kind == CommandKind.Speed && command.Value is not (1 or 2 or 5) ||
            command.Kind == CommandKind.Visibility && command.Value is not (0 or 1) ||
            command.Kind == CommandKind.Step && !paused) result = Reject("invalid_command");
        else if (command.Kind is CommandKind.UpgradeNetwork or CommandKind.ProduceEgg or CommandKind.HatchEgg or CommandKind.UpgradeCarry or CommandKind.UpgradeBrain or CommandKind.ApplyProgram or CommandKind.RollbackProgram &&
            (string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > 256)) result = Reject("reason_required");
        else if (command.Kind == CommandKind.ClearGroupProgram) result = Reject("group_programs_removed");
        else if (command.Kind == CommandKind.Visibility) result = Reject("discovery_only");
        else if (command.Kind == CommandKind.SetQueenRange) result = Reject("queen_range_requires_network_upgrade");
        else if (command.Kind == CommandKind.SetMinimumReserve && command.Value is < 0 or > 1000000000) result = Reject("invalid_reserve");
        else if (command.Kind == CommandKind.SetPopulationCap && (command.Value < world.Ants.Length || command.Value > 100)) result = Reject("invalid_population_cap");
        else if (command.Kind is CommandKind.ApplyProgram or CommandKind.RollbackProgram && command.GroupId!=0) result=Reject("species_program_only");
        else if (command.Kind == CommandKind.ApplyProgram &&
            !QueueProgram(command,out var programError))
            result = new(command.RequestId, false, "invalid_program", revision, programError);
        else if (command.Kind == CommandKind.RollbackProgram &&
            !programCatalog.TryQueueRollback(command.Species, command.Value, command.Reason, out var rollbackError))
            result = new(command.RequestId, false, rollbackError, revision);
        else if (command.Kind == CommandKind.Recruit) result = Reject("recruit_replaced_by_eggs");
        else if (command.Kind == CommandKind.HatchEgg && !world.Config.AntTypes.Any(x => x.Id == command.Species)) result = Reject("invalid_species");
        else if (command.Kind == CommandKind.HatchEgg && world.NetworkLevel<world.Config.AntType(command.Species).RequiredNetworkLevel) result=Reject("technology_locked");
        else if (command.Kind == CommandKind.HatchEgg && world.Ants.Length >= world.Config.PopulationCap) result = Reject("population_cap");
        else if (command.Kind == CommandKind.HatchEgg && world.Eggs < 1) result = Reject("no_eggs");
        else if (command.Kind == CommandKind.ProduceEgg && world.EggsProduced == int.MaxValue) result = Reject("egg_capacity");
        else if (command.Kind == CommandKind.ProduceEgg &&
            world.DevelopmentValue - world.Config.RecruitmentCost < EffectiveMinimumReserve) result = Reject("insufficient_stock");
        else if (command.Kind == CommandKind.UpgradeCarry && command.Species != AntSpeciesIds.Hauler) result = Reject("carry_upgrade_species");
        else if (command.Kind == CommandKind.UpgradeCarry && world.HaulerCarryLevel >= world.Config.CarryMultipliers.Length) result = Reject("max_carry_level");
        else if (command.Kind == CommandKind.UpgradeCarry && world.DevelopmentValue - world.Config.CarryUpgradeCosts[world.HaulerCarryLevel] < EffectiveMinimumReserve) result = Reject("insufficient_stock");
        else if (command.Kind == CommandKind.UpgradeNetwork && world.NetworkLevel >= world.Config.NetworkRanges.Length) result = Reject("max_network_level");
        else if (command.Kind == CommandKind.UpgradeNetwork && world.DevelopmentValue - world.Config.NetworkUpgradeCosts[world.NetworkLevel] < EffectiveMinimumReserve) result = Reject("insufficient_stock");
        else if (command.Kind == CommandKind.UpgradeBrain && world.BrainLevel >= world.Config.BrainBudgets.Length)
            result = Reject("max_brain_level");
        else if (command.Kind == CommandKind.UpgradeBrain &&
            world.DevelopmentValue - world.Config.BrainCosts[world.BrainLevel] < EffectiveMinimumReserve)
            result = Reject("insufficient_stock");
        else {
            switch(command.Kind) {
                case CommandKind.UpgradeNetwork:
                    var networkCost = world.Config.NetworkUpgradeCosts[world.NetworkLevel];
                    world=TechnologyProgression.Pay(world,networkCost);
                    world = world with { NetworkLevel = world.NetworkLevel + 1,
                        NetworkUpgradeSpent = checked(world.NetworkUpgradeSpent + networkCost) };
                    break;
                case CommandKind.SetMinimumReserve:
                    world = world with { Config = world.Config with { MinimumReserve = command.Value } };
                    world = world with { ActiveParameters = InstitutionMechanics.Resolve(world) };
                    break;
                case CommandKind.SetPopulationCap: world = world with { Config = world.Config with { PopulationCap = command.Value } }; break;
                case CommandKind.Pause: paused = true; break;
                case CommandKind.Resume: paused = false; break;
                case CommandKind.Step: Advance(); break;
                case CommandKind.Speed: speed = command.Value; break;
                case CommandKind.ProduceEgg:
                    world=TechnologyProgression.Pay(world,world.Config.RecruitmentCost);
                    world = world with { Eggs = checked(world.Eggs + 1), EggsProduced = checked(world.EggsProduced + 1),
                        RecruitmentSpent = checked(world.RecruitmentSpent + world.Config.RecruitmentCost) };
                    break;
                case CommandKind.HatchEgg:
                    world = world with { Ants = world.Ants.Add(new(world.Objects.All<AntView>().Select(x=>x.Id).DefaultIfEmpty(0).Max() + 1,
                        Point.FromCells(world.Config.Width / 2, world.Config.Height / 2), 0,
                        Species: command.Species, ProgramVersion: world.ActiveProgramVersions.GetValueOrDefault(command.Species),
                        Memory: Enumerable.Repeat(0, AntDslCompiler.MemorySlots).ToImmutableArray(),
                        RecentDecisions: ImmutableArray<AntDecisionLogEntry>.Empty) { CreatedTick = world.Tick, Health = world.Config.AntType(command.Species).MaxHealth, MaxHealth = world.Config.AntType(command.Species).MaxHealth }),
                        Eggs = world.Eggs - 1, EggsHatched = checked(world.EggsHatched + 1) };
                    break;
                case CommandKind.UpgradeCarry:
                    var carryCost = world.Config.CarryUpgradeCosts[world.HaulerCarryLevel];
                    world=TechnologyProgression.Pay(world,carryCost);
                    world = world with { HaulerCarryLevel = world.HaulerCarryLevel + 1,
                        CarryUpgradeSpent = checked(world.CarryUpgradeSpent + carryCost) };
                    break;
                case CommandKind.UpgradeBrain:
                    var cost = world.Config.BrainCosts[world.BrainLevel];
                    world=TechnologyProgression.Pay(world,cost);
                    world = world with { BrainLevel = world.BrainLevel + 1,
                        UpgradeSpent = checked(world.UpgradeSpent + cost) };
                    break;
                case CommandKind.ApplyProgram:
                case CommandKind.RollbackProgram:
                    break; // The catalog activates queued changes at the next simulation Tick boundary.
            }
            world = new MilestoneRule().Apply(world);
            if (command.Kind != CommandKind.Step) revision++;
            result = new(command.RequestId, true, "applied", revision);
        }
        receipts.Add(command.RequestId, (command, result)); return result;
    }
    private void Advance() {
        world=GameRules.Activate(world,programCatalog);
        world = world with { ActiveParameters = InstitutionMechanics.Resolve(world) };
        world = simulation.Step(world); revision++;
        if (world.IsDefeated) paused = true;
        eventJournal.Observe(AgentFrame());
    }
    private bool UpdatePresence(AgentPresence agent) {
        RemoveExpiredAgents();
        if (agent.SessionId == Guid.Empty || string.IsNullOrWhiteSpace(agent.Name) || agent.Name.Length > 80) return false;
        if (!agent.Connected) return agentSessions.Remove(agent.SessionId);
        if (agentSessions.Count >= 32 && !agentSessions.ContainsKey(agent.SessionId)) return false;
        agentSessions[agent.SessionId] = (agent.Name, clock.GetTimestamp());
        return true;
    }
    private void RemoveExpiredAgents() {
        foreach (var entry in agentSessions.ToArray())
            if (clock.GetElapsedTime(entry.Value.Timestamp) >= TimeSpan.FromSeconds(6)) agentSessions.Remove(entry.Key);
    }
    private Snapshot Project(bool player) {
        RemoveExpiredAgents();
        // Both player and Agent receive only discovered, last-observed records. Never expose hidden totals.
        var resources = world.Objects.All<ResourceView>().Where(x => x.IsDiscovered)
            .Select(x => x with { Credits=[],CreditTracked=true,Amount = x.LastObservedAmount, LastSeenTick = x.LastObservedTick }).ToImmutableArray();
        var species = world.Config.AntTypes.OrderBy(x => x.Id, StringComparer.Ordinal).Select(type =>
            new AntSpeciesSnapshot(type.Id, type.Name, type.EmptyMoveUnitsPerTick, type.LoadedMoveUnitsPerTick,
                world.CarryCapacity(type.Id), type.VisionCells, world.Ants.Count(a => a.Species == type.Id),
                world.DeliveredBySpecies.GetValueOrDefault(type.Id)) {
                RequiredNetworkLevel=type.RequiredNetworkLevel,Unlocked=world.NetworkLevel>=type.RequiredNetworkLevel,WorkRate=type.WorkRate,MaxHealth = type.MaxHealth, AttackDamage = type.AttackDamage,
                CarryLevel = type.Id == AntSpeciesIds.Hauler ? world.HaulerCarryLevel : 1,
                NextCarryUpgradeCost = type.Id == AntSpeciesIds.Hauler && world.HaulerCarryLevel < world.Config.CarryMultipliers.Length ? world.Config.CarryUpgradeCosts[world.HaulerCarryLevel] : 0,
                NextCarryCapacity = type.Id == AntSpeciesIds.Hauler && world.HaulerCarryLevel < world.Config.CarryMultipliers.Length ? checked(type.CarryCapacity * world.Config.CarryMultipliers[world.HaulerCarryLevel]) : 0
            }).ToImmutableArray();
        return new(Protocol.Version, revision, world.Tick, paused, speed, Visibility.Discovered, world.Config.Width, world.Config.Height,
            Point.FromCells(world.Config.Width / 2, world.Config.Height / 2), world.Ants.Select(a => a with { ResourceReports=[],QueenKnowledge=[],SourceReports=[],QueenSources=[],BuildingReports=[],QueenBuildings=[],QueenConnected = QueenCommunicationRule.Connected(world, a.Position) }).ToImmutableArray(), resources,
            world.Inventory, world.Delivered, null, world.BrainLevel,
            world.RecruitmentSpent, world.UpgradeSpent, species, programCatalog.Snapshot(), world.QueenRangeCells,
            world.QueenResources.Where(x => x.Amount > 0).ToImmutableArray()) {
                AntLevels=world.Config.Veterancy.Definitions(),TechnologyEra=TechnologyProgression.Era(world.Config,world.NetworkLevel),TechnologyEras=world.Config.TechnologyEras,StoredFoodValue=world.StoredFoodValue,DevelopmentChange=world.DevelopmentChange,DevelopmentValue=world.DevelopmentValue,ProcessingValueAdded=world.ProcessingValueAdded,WorldId=worldId,FirstAdmissionPoints=world.FirstAdmissionPoints,Economy=Economy.Stats(world),
                Sources=world.Objects.All<SourceView>().Where(x=>x.IsDiscovered).Select(x=>x with {Amount=x.LastObservedAmount}).ToImmutableArray(),Buildings=world.ObservedBuildings.Values.OrderBy(x=>x.Id).ToImmutableArray(),
                Recipes=world.Config.Production.Enabled?world.Config.Production.Recipes:[],BuildingTypes=world.Config.Production.Enabled?world.Config.Production.Buildings:[],Stock=world.Stock.Amounts, FoodTypes=world.Config.Foods, FoodEaten=world.FoodEaten, HealthRestored=world.HealthRestored,
                Groups=world.Groups, Claims=world.Claims.Where(c=>c.ExpiresTick>world.Tick && resources.Any(f=>f.Id==c.FoodId)).ToImmutableArray(),
                Enemies = world.Objects.All<EnemyView>().Where(x=>x.IsDiscovered).Select(x=>x with { Position=x.LastKnownPosition, Health=x.LastKnownHealth }).ToImmutableArray(),
                DeadAnts = world.Objects.All<AntView>().Where(x=>x.Health<=0).ToImmutableArray(),
                Defeated = world.IsDefeated, EnemiesDefeated = world.EnemiesDefeated, AntsLost = world.AntsLost,
                Milestones = MilestoneRule.Snapshot(world),
                AgentConnected = agentSessions.Count > 0,
                ConnectedAgents = agentSessions.Values.Select(x => x.Name).Order(StringComparer.Ordinal).ToImmutableArray(),
                Queen = (QueenObject?)world.Objects.Find("queen", 1),
                NetworkLevel = world.NetworkLevel, MaxNetworkLevel = world.Config.NetworkRanges.Length,
                NextNetworkRangeCells = world.NetworkLevel < world.Config.NetworkRanges.Length ? world.Config.NetworkRanges[world.NetworkLevel] : 0,
                NextNetworkUpgradeCost = world.NetworkLevel < world.Config.NetworkRanges.Length ? world.Config.NetworkUpgradeCosts[world.NetworkLevel] : 0,
                NetworkUpgradeSpent = world.NetworkUpgradeSpent,
                Technologies = TechnologyProgression.Unlocks(world),
                CarryUpgradeSpent = world.CarryUpgradeSpent, Eggs = world.Eggs, EggsProduced = world.EggsProduced, EggsHatched = world.EggsHatched,
                EggProductionCost = world.Config.RecruitmentCost,
                MinimumReserve = world.Config.MinimumReserve, PopulationCap = world.Config.PopulationCap,
                RecruitmentCost = world.Config.RecruitmentCost,
                InstructionBudget = world.Config.BrainBudgets[world.BrainLevel - 1],
                DecisionInterval = world.Config.BrainIntervals[world.BrainLevel - 1],
                NextUpgradeCost = world.BrainLevel < 5 ? world.Config.BrainCosts[world.BrainLevel] : 0, InstitutionVersion = world.Institution.Version, InstitutionAppliedTick = world.Institution.AppliedTick, InstitutionReason = world.Institution.Reason, InstitutionHistoryCount = world.Institution.History.Length, ReservePolicy = world.Institution.ReservePolicy, RationPolicy = world.Institution.RationPolicy, LaborPolicy = world.Institution.LaborPolicy, DefensePolicy = world.Institution.DefensePolicy, AdmissionPolicy = world.Institution.AdmissionPolicy
            };
    }
    public async ValueTask DisposeAsync() { queue.Writer.TryComplete(); await actor; }
}
