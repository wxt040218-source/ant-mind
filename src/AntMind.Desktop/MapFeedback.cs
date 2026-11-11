using System.Diagnostics;
using AntMind.Contracts;
namespace AntMind.Desktop;

// Presentation-only impulses derived from consecutive permitted snapshots. No world/actor handle.
internal enum MapFeedbackKind { Gather, Deliver, Work, Attack, Hurt, Heal, Complete, Spawn }
internal sealed record MapPulse(string Kind,int Id,Point Position,MapFeedbackKind Style,double Started,int Amount=0,Point? Origin=null,Point? Direction=null) {
    public const double Duration=.62;
    public double Progress(double now)=>Math.Clamp((now-Started)/Duration,0,1);
}
internal readonly record struct MapPose(double X,double Y,double ScaleX,double ScaleY) {
    public static MapPose Rest=>new(0,0,1,1);
}
internal sealed class MapFeedback {
    private readonly Dictionary<(string,int),MapPulse> poses=[];
    private readonly List<MapPulse> accents=[];
    public static double Now=>Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;
    public IReadOnlyList<MapPulse> Accents=>accents;
    public bool Active=>poses.Count>0 || accents.Count>0;
    public void Clear() {poses.Clear();accents.Clear();}
    public void Trim(double now) {
        foreach(var key in poses.Where(p=>now-p.Value.Started>=MapPulse.Duration).Select(p=>p.Key).ToArray())poses.Remove(key);
        accents.RemoveAll(p=>now-p.Started>=MapPulse.Duration);
    }
    private void Add(string kind,int id,Point position,MapFeedbackKind style,double now,int amount=0,Point? origin=null,Point? direction=null) {
        if(poses.TryGetValue((kind,id),out var active)) {
            if(style==MapFeedbackKind.Work && now-active.Started<.42)return;
            if(active.Style==MapFeedbackKind.Hurt && now-active.Started<.18)return;
        }
        var pulse=new MapPulse(kind,id,position,style,now,amount,origin,direction);
        poses[(kind,id)]=pulse;accents.Add(pulse);
        if(accents.Count>256)accents.RemoveRange(0,accents.Count-256);
    }
    public MapPose Pose(string kind,int id,double now) {
        if(!poses.TryGetValue((kind,id),out var pulse))return MapPose.Rest;
        var p=pulse.Progress(now);var spring=Math.Sin(p*Math.PI*3.5)*Math.Pow(1-p,2);var hop=Math.Sin(p*Math.PI)*Math.Pow(1-p,1.5);
        return pulse.Style switch {
            MapFeedbackKind.Hurt=>new(Math.Sin(p*Math.PI*8)*.10*Math.Pow(1-p,2),0,1+.14*spring,1-.16*spring),
            MapFeedbackKind.Attack=>Lunge(pulse,p,spring),
            MapFeedbackKind.Deliver=>new(0,-.26*hop,1+.24*spring,1-.22*spring),
            MapFeedbackKind.Gather=>new(0,-.20*hop,1-.15*spring,1+.22*spring),
            MapFeedbackKind.Work=>new(0,-.06*hop,1+.12*spring,1-.15*spring),
            MapFeedbackKind.Heal=>new(0,-.10*hop,1+.10*spring,1+.10*spring),
            _=>new(0,-.24*hop,1-.18*spring,1+.24*spring)
        };
    }
    private static MapPose Lunge(MapPulse pulse,double p,double spring) {
        double dx=1,dy=0;
        if(pulse.Direction is {} goal) {dx=(double)goal.X-pulse.Position.X;dy=(double)goal.Y-pulse.Position.Y;var length=Math.Sqrt(dx*dx+dy*dy);if(length>0){dx/=length;dy/=length;}else{dx=1;dy=0;}}
        var push=.32*Math.Sin(p*Math.PI)*Math.Pow(1-p,2);
        return new(dx*push,dy*push,1+.15*spring,1-.12*spring);
    }
    public void Observe(Snapshot? before,Snapshot? after,double now) {
        Trim(now);
        if(before is null || after is null || before.WorldId!=after.WorldId || after.Tick<before.Tick || after.Tick-before.Tick>120) {Clear();return;}
        if(after.Tick==before.Tick)return;
        var oldAnts=before.Ants.ToDictionary(a=>a.Id);var newAnts=after.Ants.ToDictionary(a=>a.Id);
        var sites=after.Buildings.ToDictionary(b=>b.Id);
        foreach(var ant in after.Ants) {
            if(!oldAnts.TryGetValue(ant.Id,out var old)) {Add("ant",ant.Id,ant.Position,MapFeedbackKind.Spawn,now);continue;}
            var gained=ant.Carry-old.Carry;var lost=old.Carry-ant.Carry;
            if(gained>0)Add("ant",ant.Id,ant.Position,MapFeedbackKind.Gather,now);
            if(lost>0 && ant.Health<=old.Health && ant.LastInteractionTick>old.LastInteractionTick) {
                Add("ant",ant.Id,ant.Position,MapFeedbackKind.Deliver,now);
                var receiver=after.Ants.FirstOrDefault(a=>a.Id==ant.Production.ObjectId && a.Id!=ant.Id && oldAnts.TryGetValue(a.Id,out var previous) && a.Carry>previous.Carry && Near(a.Position,ant.Position));
                if(ant.ActionResult=="delivered" || ant.ActionResult.StartsWith("items_moved:",StringComparison.Ordinal) && ant.Production.ObjectId==0 && Near(ant.Position,after.Nest))
                    Add("queen",1,after.Nest,MapFeedbackKind.Deliver,now,lost,ant.Position);
                else if(receiver is not null)Add("ant",receiver.Id,receiver.Position,MapFeedbackKind.Deliver,now,lost,ant.Position);
                else if(ant.ActionResult.StartsWith("items_moved:",StringComparison.Ordinal) && sites.TryGetValue(ant.Production.ObjectId,out var site) && Near(site.Position,ant.Position))
                    Add("building",site.Id,site.Position,MapFeedbackKind.Deliver,now,lost,ant.Position);
            }
            if(ant.CompletedWork>old.CompletedWork && ant.ActionResult=="working")Add("ant",ant.Id,ant.Position,MapFeedbackKind.Work,now);
            if(ant.LastAttackTick>old.LastAttackTick)Add("ant",ant.Id,ant.Position,MapFeedbackKind.Attack,now,direction:after.Enemies.FirstOrDefault(e=>e.Id==ant.AttackTargetId)?.Position);
            if(ant.Health>old.Health)Add("ant",ant.Id,ant.Position,MapFeedbackKind.Heal,now);
            if(ant.Health<old.Health)Add("ant",ant.Id,ant.Position,MapFeedbackKind.Hurt,now);
            if(ant.Level>old.Level)Add("ant",ant.Id,ant.Position,MapFeedbackKind.Complete,now);
        }
        foreach(var ant in before.Ants.Where(a=>!newAnts.ContainsKey(a.Id)))Add("ant",ant.Id,ant.Position,MapFeedbackKind.Hurt,now);
        var oldFood=before.Resources.ToDictionary(r=>r.Id);
        foreach(var food in after.Resources)if(oldFood.TryGetValue(food.Id,out var old) && food.Amount<old.Amount)Add("food",food.Id,food.Position,MapFeedbackKind.Gather,now);
        var oldSources=before.Sources.ToDictionary(s=>s.Id);
        foreach(var source in after.Sources)if(oldSources.TryGetValue(source.Id,out var old) && source.Amount<old.Amount)Add("source",source.Id,source.Position,MapFeedbackKind.Gather,now);
        var oldSites=before.Buildings.ToDictionary(b=>b.Id);
        foreach(var b in after.Buildings)if(oldSites.TryGetValue(b.Id,out var old)) {
            if(b.Phase=="ready" && old.Phase=="blueprint" || b.Output.Total>old.Output.Total)Add("building",b.Id,b.Position,MapFeedbackKind.Complete,now);
            else if(b.ConstructionWork>old.ConstructionWork || b.RecipeWork>old.RecipeWork)Add("building",b.Id,b.Position,MapFeedbackKind.Work,now);
        }
        var oldEnemies=before.Enemies.ToDictionary(e=>e.Id);
        foreach(var enemy in after.Enemies)if(oldEnemies.TryGetValue(enemy.Id,out var old) && enemy.LastSeenTick>old.LastSeenTick) {
            if(enemy.LastSeenTick==after.Tick && enemy.LastAttackTick>old.LastAttackTick)Add("enemy",enemy.Id,enemy.Position,MapFeedbackKind.Attack,now);
            if(enemy.Health<old.Health)Add("enemy",enemy.Id,enemy.Position,MapFeedbackKind.Hurt,now);
        }
        if(after.Queen is {} queen && before.Queen is {} previousQueen && queen.Health<previousQueen.Health)Add("queen",1,queen.Position,MapFeedbackKind.Hurt,now);
    }
    private static bool Near(Point a,Point b)=>Math.Abs((long)a.X-b.X)<=1000 && Math.Abs((long)a.Y-b.Y)<=1000;
}
