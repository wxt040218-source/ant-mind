namespace AntMind.Programs;
public static class DefaultProgramSources {
    public static string Chef { get; } = Read("chef");
    public static string Soldier { get; } = Read("soldier");
    public static string Scout { get; } = Read("scout");
    public static string Hauler { get; } = Read("hauler");
    private static string Read(string species) {
        using var stream = typeof(DefaultProgramSources).Assembly.GetManifestResourceStream($"AntMind.Programs.{species}.ant")
            ?? throw new InvalidOperationException("Missing built-in strategy.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
