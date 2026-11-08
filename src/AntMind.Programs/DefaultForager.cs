namespace AntMind.Programs;
public sealed class DefaultForager : IAntProgram {
    private readonly IAntProgram chef=AntDslCompiler.Compile(DefaultProgramSources.Chef);
    private readonly IAntProgram scout = AntDslCompiler.Compile(DefaultProgramSources.Scout);
    private readonly IAntProgram hauler = AntDslCompiler.Compile(DefaultProgramSources.Hauler);
    private readonly IAntProgram soldier = AntDslCompiler.Compile(DefaultProgramSources.Soldier);
    public Decision Execute(Perception perception, InstructionBudget budget) =>
        (perception.Species switch { AntMind.Contracts.AntSpeciesIds.Chef=>chef, AntMind.Contracts.AntSpeciesIds.Soldier=>soldier, AntMind.Contracts.AntSpeciesIds.Hauler=>hauler, _=>scout }).Execute(perception, budget);
}
public sealed class DefaultHauler : IAntProgram {
    private readonly IAntProgram program = AntDslCompiler.Compile(DefaultProgramSources.Hauler);
    public Decision Execute(Perception perception, InstructionBudget budget) => program.Execute(perception, budget);
}
