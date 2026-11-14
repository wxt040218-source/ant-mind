using System.Collections.Immutable;
using System.Text.Json;
using AntMind.Contracts;
using AntMind.Core;
namespace AntMind.Runtime;

public sealed record SavedProgram(string Species,int Active,int? Pending,ImmutableArray<SavedProgramVersion> Versions);
public sealed record SavedProgramVersion(int Version,string Reason,string Source);
public sealed record SavedGame(int Format,int Protocol,WorldState World,ImmutableArray<SavedProgram> Programs,int Speed=1);
public sealed record SaveInfo(string Slot,long Bytes,DateTime ModifiedUtc);
public static class SavedGames {
    public const int Format=6;
    private const int MaxBytes=32*1024*1024;
    private static readonly JsonSerializerOptions Json=new() {IgnoreReadOnlyProperties=true,MaxDepth=64,UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow};
    public static string DefaultDirectory=>Path.Combine(AppContext.BaseDirectory,"saves");
    public static bool ValidSlot(string key)=>key.Length is >0 and <=40 && key.All(c=>char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
    public static void Write(string directory,string slot,SavedGame data) {
        if(!ValidSlot(slot))throw new ArgumentException("Invalid save slot.");
        var bytes=JsonSerializer.SerializeToUtf8Bytes(data,Json);
        if(bytes.Length>MaxBytes)throw new InvalidDataException("Save exceeds size limit.");
        Directory.CreateDirectory(directory);var target=Path.Combine(directory,slot+".json");var temp=target+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(bytes);file.Flush(true);}File.Move(temp,target,true);}
        finally {if(File.Exists(temp))File.Delete(temp);}
    }
    public static SavedGame Read(string directory,string slot) {
        if(!ValidSlot(slot))throw new ArgumentException("Invalid save slot.");
        var file=new FileInfo(Path.Combine(directory,slot+".json"));
        if(!file.Exists || file.Length>MaxBytes)throw new InvalidDataException("Save missing or too large.");
        var save=JsonSerializer.Deserialize<SavedGame>(SaveMigration.NormalizeLegacyJson(File.ReadAllText(file.FullName)),Json)??throw new InvalidDataException("Empty save.");
        save=SaveMigration.Upgrade(save);Validate(save);return save;
    }
    public static ImmutableArray<SaveInfo> List(string directory)=>!Directory.Exists(directory)?[]:Directory.EnumerateFiles(directory,"*.json")
        .Select(p=>new FileInfo(p)).Where(f=>ValidSlot(Path.GetFileNameWithoutExtension(f.Name)))
        .OrderBy(f=>f.Name,StringComparer.Ordinal).Select(f=>new SaveInfo(Path.GetFileNameWithoutExtension(f.Name),f.Length,f.LastWriteTimeUtc)).ToImmutableArray();
    public static void Validate(SavedGame save) {
        if(save.Format!=Format || save.Protocol!=Contracts.Protocol.Version || save.World is null || save.World.Config is null || save.Programs.IsDefaultOrEmpty || save.Speed is not (1 or 2 or 5))throw new InvalidDataException("Unsupported save format.");
        var w=save.World;w.Config.Validate();
        if(w.Tick<0 || w.RandomState==0 || w.BrainLevel<1 || w.BrainLevel>w.Config.BrainBudgets.Length || w.NetworkLevel<1 || w.NetworkLevel>w.Config.NetworkRanges.Length ||
            w.HaulerCarryLevel<1 || w.HaulerCarryLevel>w.Config.CarryMultipliers.Length || w.Eggs<0 || w.DevelopmentChange<0 || w.ProcessingValueAdded<0 || w.FirstAdmissionPoints<0 || w.Objects.Records.Count>100000 || w.Objects.Find("queen",1) is not QueenObject)
            throw new InvalidDataException("Invalid world state.");
        if(w.ExploredCells.Any(c=>c<0 || c>=w.Config.Width*w.Config.Height) || w.PendingExperience.Any(e=>e.Amount<0))throw new InvalidDataException("Invalid exploration or experience events.");
        foreach(var item in w.Objects.Records) {
            var o=item.Value;
            if(item.Key!=$"{o.Kind}:{o.Id}" || o.Id<1 || o.Position.X<0 || o.Position.Y<0 || o.Position.X>(w.Config.Width-1)*1000 || o.Position.Y>(w.Config.Height-1)*1000)throw new InvalidDataException("Invalid object.");
            if(o is AntView a && (!w.Config.AntTypes.Any(x=>x.Id==a.Species) || a.Carry<0 || a.Carry>w.CarryCapacity(a.Species) || a.Memory.IsDefault || a.Memory.Length!=16 || a.Health<0 || a.Health>a.MaxHealth))throw new InvalidDataException("Invalid ant.");
            if(o is AntView rank && (rank.Level is <1 or >3 || rank.Experience<0 || rank.Experience>w.Config.Veterancy.Thresholds[2] || rank.Level!=AntProgression.LevelFor(rank.Experience,w.Config.Veterancy) || rank.CompletedWork<0 || rank.WorkExperienceRemainder<0 || rank.WorkExperienceRemainder>=w.Config.Veterancy.WorkUnitsPerExperience || rank.WorkRateRemainder is <0 or >99))throw new InvalidDataException("Invalid ant progression.");
            if(o is AntView cargo)ValidateCredits(cargo.CargoCredits,cargo.Carry);
            if(o is AntView meal && (meal.EatHolderId < -1 || meal.HealUntilTick>0 && meal.EatHolderId>0 && w.Objects.Find("building",meal.EatHolderId) is not BuildingView))throw new InvalidDataException("Invalid eating source.");
            if(o is ResourceView batch)ValidateCredits(batch.Credits,batch.Amount);
            if(o is ResourceView f && (f.Amount<0 || f.Amount>f.MaxCapacity || !w.Config.Foods.Any(x=>x.Id==f.FoodType)))throw new InvalidDataException("Invalid food.");
            if(o is SourceView s && (s.Amount<0 || s.Amount>s.Capacity || s.Capacity<1 || s.GrowthTicks<0 || !w.Config.Foods.Any(x=>x.Id==s.ItemType)))throw new InvalidDataException("Invalid source.");
            if(o is BuildingView materialSite && materialSite.ConstructionItem.Length>0 && !w.Config.Foods.Any(x=>x.Id==materialSite.ConstructionItem && !x.Edible))throw new InvalidDataException("Invalid construction material.");
            if(o is BuildingView b) {ValidateStore(b.Store,w.Config);ValidateStore(b.Output,w.Config);ValidateStore(b.WorkInProgress,w.Config);if(b.Phase is not ("blueprint" or "ready" or "cancelled") || b.ConstructionWork<0 || b.RecipeWork<0 || b.Recipe.Length>0 && !w.Config.Production.Recipes.Any(x=>x.Id==b.Recipe) || !w.Config.Production.Buildings.Any(x=>x.Id==b.Type))throw new InvalidDataException("Invalid building.");}
            if(o is EnemyView e && (!w.Config.Creatures.Kinds.Contains(e.CreatureKind) ||
                !w.Config.Foods.Any(f=>f.Id==e.CarcassFoodType) || e.BlockCells<0 || e.HerdId<0 ||
                e.LastGrazeTick>w.Tick || e.TargetChamberX is < -1 or >= NestLayout.GridWidth ||
                e.TargetChamberY is < -1 or >= NestLayout.GridHeight))throw new InvalidDataException("Invalid creature kind.");
        }
        try {w.Institution.Validate();w.Nest.Validate();}catch(ArgumentException){throw new InvalidDataException("Invalid colony era state.");}
        var observed=w.ObservedChambers;
        if(w.CreatureSeed==0 || w.NestSeed==0 || w.Institution.AppliedTick>w.Tick ||
            w.BlockedCells.IsDefault || w.BlockedCells.Any(c=>c<0 || c>=w.Config.Width*w.Config.Height) ||
            observed.IsDefault || observed.Any(c=>c<0 || c>=NestLayout.CellCount) ||
            observed.Skip(1).Where((c,i)=>c<=observed[i]).Any() ||
            !observed.SequenceEqual(Enumerable.Range(0,NestLayout.CellCount).Where(i=>w.Nest.Cells[i].Discovered)))
            throw new InvalidDataException("Invalid colony era state.");
        ValidateStore(w.Stock,w.Config);
        if(w.NextResourceId<=w.Objects.All<ResourceView>().Select(x=>x.Id).DefaultIfEmpty(0).Max() || w.NextEnemyId<=w.Objects.All<EnemyView>().Select(x=>x.Id).DefaultIfEmpty(0).Max() || w.NextBuildingId<=w.Objects.All<BuildingView>().Select(x=>x.Id).DefaultIfEmpty(0).Max())throw new InvalidDataException("Invalid next identity.");
        if(!save.Programs.Select(x=>x.Species).Order().SequenceEqual(w.Config.AntTypes.Select(x=>x.Id).Order()))throw new InvalidDataException("Program registry mismatch.");
    }
    private static void ValidateCredits(ImmutableArray<CreditLot> credits,long quantity) {
        if(credits.IsDefault || credits.Any(l=>l.Quantity<1 || l.PointsPerUnit<1) || credits.Sum(l=>l.Quantity)>quantity)throw new InvalidDataException("Invalid provenance.");
        _=ItemCredit.Points(credits);
    }
    private static void ValidateStore(FoodStore s,WorldConfig config) {
        foreach(var lots in s.Credits)ValidateCredits(lots.Value,s.Get(lots.Key));
        if(s.Amounts.Any(x=>x.Value<0 || !config.Foods.Any(f=>f.Id==x.Key)) || s.Credits.Any(x=>x.Value.Any(l=>l.Quantity<1 || l.PointsPerUnit<1) || x.Value.Sum(l=>l.Quantity)>s.Get(x.Key)))throw new InvalidDataException("Invalid item ledger.");
    }
}
public sealed partial class GameSession {
    private readonly string saveDirectory;
    private Guid worldId=Guid.NewGuid();
    private bool requireWorldId;
    public ImmutableArray<SaveInfo> ListSaves()=>SavedGames.List(saveDirectory);
    public EvaluationReport EvaluateSave(EvaluationRequest request,CancellationToken ct=default) {
        if(request.Ticks is <1 or >1200)throw new ArgumentException("Evaluation ticks must be 1..1200.");
        var save=SavedGames.Read(saveDirectory,request.Slot);
        EvaluationBranch Run(bool candidate) {
            var catalog=new SpeciesProgramCatalog(save.World.Config.AntTypes.Select(x=>x.Id));catalog.Restore(save.Programs);
            if(candidate && request.Source.Length>0 && !catalog.TryQueueApply(request.Species,request.Source,"isolated evaluation",out _,out var detail))throw new ArgumentException(detail);
            var simulation=GameRules.Create(catalog);var branch=save.World;var errors=0;
            for(var tick=0;tick<request.Ticks;tick++) {ct.ThrowIfCancellationRequested();branch=simulation.Step(GameRules.Activate(branch,catalog));errors+=branch.Ants.Count(a=>a.RecentDecisions.Any(d=>d.Tick==branch.Tick && d.Error is not null));if(branch.IsDefeated)break;}
            var delta=branch.Config.Foods.ToImmutableDictionary(x=>x.Id,x=>branch.Stock.Get(x.Id)-save.World.Stock.Get(x.Id));
            return new(save.World.Tick,branch.Tick,branch.FirstAdmissionPoints-save.World.FirstAdmissionPoints,branch.Delivered-save.World.Delivered,branch.AntsLost-save.World.AntsLost,errors,delta) {ProcessingValueAdded=branch.ProcessingValueAdded-save.World.ProcessingValueAdded,DevelopmentValueDelta=branch.DevelopmentValue-save.World.DevelopmentValue};
        }
        return new(request.Slot,request.Ticks,Run(false),Run(true));
    }
    private CommandResult ApplyProductionCommand(GameCommand c) {
        CommandResult Reject(string code)=>new(c.RequestId,false,code,revision);
        if(string.IsNullOrWhiteSpace(c.Reason) || c.Reason.Length>256)return Reject("reason_required");
        if(c.Kind==CommandKind.SaveGame || c.Kind==CommandKind.LoadGame) {
            if(!SavedGames.ValidSlot(c.Key))return Reject("invalid_save_slot");
            try {
                if(c.Kind==CommandKind.SaveGame)SavedGames.Write(saveDirectory,c.Key,new(SavedGames.Format,Protocol.Version,world,programCatalog.Export(),speed));
                else {
                    var save=SavedGames.Read(saveDirectory,c.Key);
                    programCatalog.Restore(save.Programs); // Compiles/validates all entries before any mutation.
                    world=save.World;speed=save.Speed;worldId=Guid.NewGuid();requireWorldId=true;paused=true;receipts.Clear();
                }
                revision++;return new(c.RequestId,true,c.Kind==CommandKind.SaveGame?"saved":"loaded_paused",revision);
            } catch(Exception ex) when(ex is IOException or JsonException or ArgumentException or FormatException or OverflowException or NullReferenceException or InvalidOperationException or KeyNotFoundException) {return new(c.RequestId,false,"invalid_save",revision,ex.Message);}
        }
        if(!world.Config.Production.Enabled)return Reject("production_disabled");
        if(c.Kind==CommandKind.PlanBuild) {
            var def=world.Config.Production.Buildings.FirstOrDefault(x=>x.Id==c.Key);
            if(def is null || c.Location is not {} p || p.X<0 || p.Y<0 || p.X>(world.Config.Width-1)*1000 || p.Y>(world.Config.Height-1)*1000)return Reject("invalid_blueprint");
            if(world.NetworkLevel<def.RequiredNetworkLevel)return Reject("technology_locked");
            if(world.Objects.All<BuildingView>().Length>=64)return Reject("building_limit");
            if(!BuildingPlacement.IsFree(p,((QueenObject)world.Objects.Find("queen",1)!).Position,world.Config.Width,world.Config.Height,world.Objects.All<BuildingView>()))return Reject("site_occupied");
            var b=new BuildingView(world.NextBuildingId,p,def.Id) {ConstructionItem=def.MaterialType};
            // A free, nonphysical plan never tests unknown map objects or teleports materials.
            world=world with {Objects=world.Objects.Put(b),ObservedBuildings=world.ObservedBuildings.Add(b.Id,b),NextBuildingId=world.NextBuildingId+1};
            revision++;return new(c.RequestId,true,"blueprint_created",revision,b.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        if(world.Objects.Find("building",c.Value) is not BuildingView building)return Reject("building_not_found");
        if(building.Phase=="cancelled")return Reject("already_cancelled");
        // Cancel unfinished construction or a batch. Materials remain physically at the site.
        if(building.Phase=="ready" && building.Recipe.Length==0)return Reject("no_pending_batch");
        var store=building.Store;
        foreach(var item in building.WorkInProgress.Amounts)store=store.Put(item.Key,item.Value,building.WorkInProgress.Credits.GetValueOrDefault(item.Key,[]));
        building=building with {Store=store,WorkInProgress=new(),Recipe="",RecipeWork=0,Phase=building.Phase=="blueprint"?"cancelled":building.Phase};
        world=world with {Objects=world.Objects.Put(building),ObservedBuildings=world.ObservedBuildings.SetItem(building.Id,building)};
        revision++;return new(c.RequestId,true,"cancelled_materials_retained",revision);
    }
}
