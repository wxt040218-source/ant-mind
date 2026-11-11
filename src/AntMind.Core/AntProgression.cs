using System.Collections.Immutable;
using AntMind.Contracts;
namespace AntMind.Core;

public sealed record VeterancyConfig {
    public ImmutableArray<int> Thresholds {get;init;}=[0,30,120];
    public ImmutableArray<int> MovePercent {get;init;}=[100,110,125];
    public ImmutableArray<int> WorkPercent {get;init;}=[100,125,150];
    public ImmutableArray<int> DamagePercent {get;init;}=[100,125,150];
    public ImmutableArray<int> HealthPercent {get;init;}=[100,125,150];
    public int HarvestExperience {get;init;}=1;
    public int DeliveryExperience {get;init;}=1;
    public int ExploreExperience {get;init;}=1;
    public int HitExperience {get;init;}=1;
    public int KillExperience {get;init;}=5;
    public int WorkUnitsPerExperience {get;init;}=10;
    public void Validate() {
        if(Thresholds.IsDefault || Thresholds.Length!=3 || Thresholds[0]!=0 || Thresholds[1]<1 || Thresholds[2]<=Thresholds[1] || Thresholds[2]>1000000 ||
            new[]{MovePercent,WorkPercent,DamagePercent,HealthPercent}.Any(a=>a.IsDefault || a.Length!=3 || a[0]!=100 || a.Any(x=>x is <100 or >300) || a.Zip(a.Skip(1)).Any(p=>p.Second<p.First)) ||
            new[]{HarvestExperience,DeliveryExperience,ExploreExperience,HitExperience,KillExperience}.Any(x=>x is <0 or >100) || WorkUnitsPerExperience is <1 or >1000)
            throw new ArgumentException("Invalid three-level ant progression.");
    }
    public ImmutableArray<AntLevelDefinition> Definitions()=>Enumerable.Range(0,3).Select(i=>new AntLevelDefinition(i+1,Thresholds[i],MovePercent[i],WorkPercent[i],DamagePercent[i],HealthPercent[i])).ToImmutableArray();
}
public sealed record ExperienceEvent(int AntId,int Amount,string Work,bool EffectiveWork=false);

public static class AntProgression {
    public static int LevelFor(int experience,VeterancyConfig config)=>experience>=config.Thresholds[2]?3:experience>=config.Thresholds[1]?2:1;
    public static int MaxHealth(WorldState w,AntView a)=>checked(w.Config.AntType(a.Species).MaxHealth*w.Config.Veterancy.HealthPercent[a.Level-1]/100);
    public static int AttackDamage(WorldState w,AntView a)=>checked(w.Config.AntType(a.Species).AttackDamage*w.Config.Veterancy.DamagePercent[a.Level-1]/100);
    public static WorldState Record(WorldState w,int antId,int amount,string work,bool effectiveWork=false)=>amount<=0?w:
        w with {PendingExperience=w.PendingExperience.Add(new(antId,amount,work,effectiveWork))};
    public static int Cell(WorldConfig config,Point p)=>checked(p.Y/1000*config.Width+p.X/1000);
}

// New territory is rewarded once for the colony, in ant-ID order. Standing still and revisits earn nothing.
public sealed class ExplorationExperienceRule : IWorldRule {
    public string Id=>"core.exploration-experience";public int Order=>47;
    public WorldState Apply(WorldState w) {
        var cells=w.ExploredCells.ToBuilder();
        foreach(var a in w.Ants)if(cells.Add(AntProgression.Cell(w.Config,a.Position)))
            w=AntProgression.Record(w,a.Id,w.Config.Veterancy.ExploreExperience,"explore");
        return w with {ExploredCells=cells.ToImmutable()};
    }
}

// Events settle after all physical rules. Never write an ant in an earlier rule's stale local builder.
public sealed class AntExperienceRule : IWorldRule {
    public string Id=>"core.ant-experience";public int Order=>48;
    public WorldState Apply(WorldState w) {
        var config=w.Config.Veterancy;
        foreach(var group in w.PendingExperience.GroupBy(e=>e.AntId).OrderBy(g=>g.Key)) {
            if(w.Objects.Find("ant",group.Key) is not AntView {Health:>0} a)continue;
            foreach(var e in group) {
                var xp=e.Amount;var workCount=1;
                if(e.EffectiveWork) {
                    var units=checked(a.WorkExperienceRemainder+e.Amount);xp=units/config.WorkUnitsPerExperience;
                    workCount=xp;a=a with {WorkExperienceRemainder=units%config.WorkUnitsPerExperience};
                }
                if(xp==0)continue;
                var total=(int)Math.Min(config.Thresholds[2],(long)a.Experience+xp);
                var level=AntProgression.LevelFor(total,config);
                var previousMax=AntProgression.MaxHealth(w,a);var previousLevel=a.Level;
                a=a with {Experience=total,Level=level,CompletedWork=checked(a.CompletedWork+workCount),LastExperienceTick=w.Tick,LastExperienceWork=e.Work};
                // A promotion increases capacity for health, never grants a free heal.
                if(level>previousLevel)a=a with {MaxHealth=checked(a.MaxHealth+AntProgression.MaxHealth(w,a)-previousMax)};
            }
            w=w with {Objects=w.Objects.Put(a)};
        }
        return w with {PendingExperience=[]};
    }
}
