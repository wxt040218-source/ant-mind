using System.Collections.Immutable;
using AntMind.Contracts;
using AntMind.Programs;

namespace AntMind.Runtime;

// Mutated only by GameSession's single-writer actor.
public sealed class SpeciesProgramCatalog {
    private sealed record VersionEntry(ProgramVersionSnapshot View, string Source, IAntProgram Program);
    private sealed record PendingEntry(int Version, string Reason);
    private readonly Dictionary<string, List<VersionEntry>> versions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> active = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PendingEntry> pending = new(StringComparer.Ordinal);

    public SpeciesProgramCatalog(IEnumerable<string> species) {
        foreach (var id in species.OrderBy(x => x, StringComparer.Ordinal)) {
            var source = id switch { AntSpeciesIds.Chef=>DefaultProgramSources.Chef, AntSpeciesIds.Soldier => DefaultProgramSources.Soldier, AntSpeciesIds.Hauler => DefaultProgramSources.Hauler, _ => DefaultProgramSources.Scout };
            var program = AntDslCompiler.Compile(source);
            versions.Add(id, [new(new(0, "built-in default"), source, program)]);
            active.Add(id, 0);
        }
    }

    public IAntProgram ResolveActive(string species) => versions[species][active[species]].Program;
    public int ActiveVersion(string species) => active[species];
    public int? PendingVersion(string species) => pending.TryGetValue(species, out var value) ? value.Version : null;

    public bool TryQueueApply(string species, string source, string reason, out int version, out string detail) {
        version = -1; detail = "";
        if (!versions.TryGetValue(species, out var history)) { detail = "invalid_species"; return false; }
        if (pending.ContainsKey(species)) { detail = "program_update_pending"; return false; }
        if (history.Count >= 32) { detail = "program_history_full"; return false; }
        try {
            var program = AntDslCompiler.Compile(source);
            version = history.Count;
            history.Add(new(new(version, reason), source, program));
            pending.Add(species, new(version, reason));
            return true;
        } catch (FormatException ex) {
            detail = ex.Message;
            return false;
        } catch (ArgumentException ex) {
            detail = ex.Message;
            return false;
        }
    }

    public bool TryQueueRollback(string species, int version, string reason, out string detail) {
        detail = "";
        if (!versions.TryGetValue(species, out var history)) { detail = "invalid_species"; return false; }
        if (pending.ContainsKey(species)) { detail = "program_update_pending"; return false; }
        if (version < 0 || version >= history.Count) { detail = "program_version_not_found"; return false; }
        if (active[species] == version) { detail = "program_already_active"; return false; }
        if (history.Count >= 32) { detail = "program_history_full"; return false; }
        var target = history[version];
        var newVersion = history.Count;
        history.Add(new(new(newVersion, $"rollback to v{version}: {reason}"), target.Source, target.Program));
        pending.Add(species, new(newVersion, reason));
        return true;
    }

    public ImmutableDictionary<string, int> ActivatePending() {
        var activated = ImmutableDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);
        foreach (var item in pending.OrderBy(x => x.Key, StringComparer.Ordinal)) {
            active[item.Key] = item.Value.Version;
            activated.Add(item.Key, item.Value.Version);
        }
        pending.Clear();
        return activated.ToImmutable();
    }

    public ImmutableArray<SavedProgram> Export()=>versions.Keys.Order(StringComparer.Ordinal).Select(id=>new SavedProgram(id,active[id],PendingVersion(id),versions[id].Select(v=>new SavedProgramVersion(v.View.Version,v.View.Reason,v.Source)).ToImmutableArray())).ToImmutableArray();
    public void Restore(ImmutableArray<SavedProgram> saved) {
        var restored=new Dictionary<string,List<VersionEntry>>(StringComparer.Ordinal);
        foreach(var slot in saved) {
            if(slot.Versions.IsDefaultOrEmpty || slot.Versions.Length>32 || slot.Active<0 || slot.Active>=slot.Versions.Length || slot.Pending is {} pendingVersion && (pendingVersion<0 || pendingVersion>=slot.Versions.Length) || !slot.Versions.Select(x=>x.Version).SequenceEqual(Enumerable.Range(0,slot.Versions.Length)))throw new FormatException("Invalid saved programs.");
            restored.Add(slot.Species,slot.Versions.Select(v=>new VersionEntry(new(v.Version,v.Reason),v.Source,AntDslCompiler.Compile(v.Source))).ToList());
        }
        versions.Clear();active.Clear();pending.Clear();
        foreach(var slot in saved) {versions.Add(slot.Species,restored[slot.Species]);active.Add(slot.Species,slot.Active);if(slot.Pending is {} v)pending.Add(slot.Species,new(v,"restored"));}
    }

    public ImmutableArray<SpeciesProgramSnapshot> Snapshot() => versions.Keys.OrderBy(x => x, StringComparer.Ordinal)
        .Select(species => new SpeciesProgramSnapshot(species, active[species], PendingVersion(species),
            versions[species][active[species]].Source,
            versions[species].Select(x => x.View).ToImmutableArray())).ToImmutableArray();
}
