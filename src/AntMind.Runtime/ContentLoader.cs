using System.Text.Json;
using AntMind.Core;
namespace AntMind.Runtime;
public sealed record ContentPack(int SchemaVersion, string Id, WorldConfig World);
public static class ContentLoader {
    public static WorldConfig LoadDefault() {
        var file = Path.Combine(AppContext.BaseDirectory, "content", "world.json");
        var pack = JsonSerializer.Deserialize<ContentPack>(File.ReadAllText(file), new JsonSerializerOptions { PropertyNameCaseInsensitive = true, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow }) ?? throw new InvalidDataException("Empty content pack.");
        if (pack.SchemaVersion != 15 || string.IsNullOrWhiteSpace(pack.Id) || pack.World is null) throw new InvalidDataException("Unsupported content pack.");
        pack.World.Validate(); return pack.World;
    }
}
