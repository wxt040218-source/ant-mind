using AntMind.Contracts;

namespace AntMind.Core;

public sealed record CombatConfig {
    public bool Enabled { get; init; }
    public int SpawnIntervalTicks { get; init; } = 1200;
    public int EnemyCap { get; init; } = 3;
    public int EnemyHealth { get; init; } = 18;
    public int EnemyDamage { get; init; } = 2;
    public int EnemySpeed { get; init; } = 70;
    public int EnemyVisionCells { get; init; } = 8;
    public int AttackCooldownTicks { get; init; } = 20;
    public int QueenHealth { get; init; } = 100;
    public int CorpseMin { get; init; } = 1;
    public int CorpseMax { get; init; } = 5;
    public void Validate() {
        if (SpawnIntervalTicks < 1 || EnemyCap is < 1 or > 32 || EnemyHealth is < 1 or > 10000 ||
            EnemyDamage is < 1 or > 1000 || EnemySpeed is < 1 or > 1000 || EnemyVisionCells is < 1 or > 32 ||
            AttackCooldownTicks < 1 || QueenHealth is < 1 or > 100000 || CorpseMin < 1 || CorpseMax < CorpseMin || CorpseMax > 5)
            throw new ArgumentException("Invalid combat configuration.");
    }
}
public static class CombatMath {
    public static long Distance(Point a, Point b) => Math.Max(Math.Abs((long)a.X-b.X),Math.Abs((long)a.Y-b.Y));
    public static Point Move(Point from,Point target,int speed)=>PointMath.Towards(from,target,speed);
}
public sealed class EnemySpawnRule : IWorldRule {
    public string Id => "core.enemy-spawn";
    public int Order => 0;
    public WorldState Apply(WorldState world) {
        var config=world.Config.Combat;
        if (!config.Enabled || world.Tick%config.SpawnIntervalTicks!=0 || world.Enemies.Length>=config.EnemyCap) return world;
        var random=world.RandomState;
        var side=(int)(RandomSource.Next(ref random)%4);
        var offset=RandomSource.Next(ref random);
        var position=side switch {
            0=>Point.FromCells(0,(int)(offset%(uint)world.Config.Height)),
            1=>Point.FromCells(world.Config.Width-1,(int)(offset%(uint)world.Config.Height)),
            2=>Point.FromCells((int)(offset%(uint)world.Config.Width),0),
            _=>Point.FromCells((int)(offset%(uint)world.Config.Width),world.Config.Height-1)
        };
        var food=config.CorpseMin+(int)(RandomSource.Next(ref random)%(uint)(config.CorpseMax-config.CorpseMin+1));
        return world with { Objects=world.Objects.Put(new EnemyView(world.NextEnemyId,position,config.EnemyHealth,config.EnemyHealth,food) { CreatedTick=world.Tick }),
            NextEnemyId=checked(world.NextEnemyId+1),RandomState=random };
    }
}
public sealed class EnemyMovementRule : IWorldRule {
    public string Id => "core.enemy-move";
    public int Order => 21;
    public WorldState Apply(WorldState world) {
        var queen=(QueenObject)world.Objects.Find("queen",1)!;
        var database=world.Objects;
        foreach (var enemy in world.Enemies) {
            var target=queen.Position;
            if (CombatMath.Distance(enemy.Position,queen.Position)>Point.UnitsPerCell) {
                var ant=world.Ants.Where(x=>CombatMath.Distance(x.Position,enemy.Position)<=world.Config.Combat.EnemyVisionCells*Point.UnitsPerCell)
                    .OrderBy(x=>CombatMath.Distance(x.Position,enemy.Position)).ThenBy(x=>x.Id).FirstOrDefault();
                if (ant is not null) target=ant.Position;
            }
            if (CombatMath.Distance(enemy.Position,target)>Point.UnitsPerCell)
                database=database.Put(enemy with { Position=CombatMath.Move(enemy.Position,target,world.Config.Combat.EnemySpeed) });
        }
        return world with { Objects=database };
    }
}
public sealed class CombatRule : IWorldRule {
    public string Id => "core.combat";
    public int Order => 25;
    public WorldState Apply(WorldState world) {
        // Stable ant-ID order resolves attacks first. A killed enemy cannot retaliate or drop twice.
        foreach (var ant in InstitutionMechanics.GuardFirst(world, world.Ants.Where(x=>x.Intent==AntAction.Attack))) {
            var type=world.Config.AntType(ant.Species);
            var enemy=world.Objects.Find("enemy",ant.AttackTargetId) as EnemyView;
            var error=type.AttackDamage==0 ? "cannot_attack" : world.Tick-ant.LastAttackTick<type.AttackCooldownTicks ? "attack_cooldown" :
                enemy is null || enemy.Health<=0 ? "enemy_not_found" : CombatMath.Distance(ant.Position,enemy.Position)>Point.UnitsPerCell ? "enemy_out_of_reach" : null;
            if (error is null) {
                world=AntProgression.Record(world,ant.Id,world.Config.Veterancy.HitExperience,"attack");
                enemy=enemy! with { Health=Math.Max(0,enemy!.Health-AntProgression.AttackDamage(world,ant)) };
                if (enemy.Health==0) {
                    world=AntProgression.Record(world,ant.Id,world.Config.Veterancy.KillExperience,"kill");
                    var corpseId=world.NextResourceId;
                    world=AddFood(world,enemy.Position,enemy.FoodAmount,ResourceOrigin.Carcass,FoodTypes.Meat,enemy.Id);
                    enemy=enemy with { CorpseFoodId=corpseId };
                    world=world with { EnemiesDefeated=checked(world.EnemiesDefeated+1) };
                }
                world=world with { Objects=world.Objects.Put(enemy) };
            }
            world=world with { Objects=world.Objects.Put(ant with { Intent=AntAction.Wait, Target=null,
                ActionStatus=error is null ? ActionStatus.Succeeded : ActionStatus.Failed,
                ActionResult=error ?? "attacked", LastAttackTick=error is null ? world.Tick : ant.LastAttackTick }) };
        }
        foreach (var enemy in world.Enemies) {
            if (world.IsDefeated) break;
            if (world.Tick-enemy.LastAttackTick<world.Config.Combat.AttackCooldownTicks) continue;
            var queen=(QueenObject)world.Objects.Find("queen",1)!;
            var attacked=false;
            if (CombatMath.Distance(enemy.Position,queen.Position)<=Point.UnitsPerCell) {
                world=world with { Objects=world.Objects.Put(queen with { Health=Math.Max(0,queen.Health-world.Config.Combat.EnemyDamage) }) };
                attacked=true;
            } else {
                var ant=world.Ants.Where(x=>CombatMath.Distance(x.Position,enemy.Position)<=Point.UnitsPerCell)
                    .OrderBy(x=>CombatMath.Distance(x.Position,enemy.Position)).ThenBy(x=>x.Id).FirstOrDefault();
                if (ant is not null) {
                    var hurt=ant with { Health=Math.Max(0,ant.Health-world.Config.Combat.EnemyDamage), LastDamageTick=world.Tick, HealUntilTick=0,
                        Intent=ant.HealUntilTick>0 ? AntAction.Wait : ant.Intent,
                        ActionResult=ant.HealUntilTick>0 ? "eating_interrupted" : ant.ActionResult,
                        ActionStatus=ant.HealUntilTick>0 ? ActionStatus.Failed : ant.ActionStatus };
                    if (hurt.Health==0) {
                        world=AddFood(world,ant.Position,ant.Carry,ResourceOrigin.Dropped,ant.CarryFood, credits:Economy.Credits(ant));
                        hurt=hurt with { Carry=0,CargoCredits=[],CargoTracked=true,WorkTicksRemaining=0, Intent=AntAction.Wait,Target=null };
                        world=world with { AntsLost=checked(world.AntsLost+1) };
                    }
                    world=world with { Objects=world.Objects.Put(hurt) }; attacked=true;
                }
            }
            if (attacked) world=world with { Objects=world.Objects.Put(enemy with { LastAttackTick=world.Tick }) };
        }
        return world;
    }
    private static WorldState AddFood(WorldState world, Point position, int quantity, ResourceOrigin origin, string foodType, int enemyId=0, System.Collections.Immutable.ImmutableArray<CreditLot>? credits=null) {
        var remainingCredits=credits??ItemCredit.Raw(foodType,quantity);
        while(quantity>0) {
            var amount=Math.Min(quantity,world.Config.ResourceMax);
            var part=ItemCredit.Split(remainingCredits,amount);remainingCredits=part.Left;
            var food=new ResourceView(world.NextResourceId,position,amount,world.Tick,amount) {
                Credits=part.Taken,CreditTracked=true,FoodType=foodType, Origin=origin, IsDiscovered=true,DiscoveredTick=world.Tick,LastObservedAmount=amount,LastObservedTick=world.Tick,
                Properties=System.Collections.Immutable.ImmutableDictionary<string,string>.Empty.Add("source_enemy_id",enemyId.ToString(System.Globalization.CultureInfo.InvariantCulture))
            };
            world=world with { Resources=world.Resources.Add(food), NextResourceId=checked(world.NextResourceId+1),
                Generated=origin==ResourceOrigin.Carcass ? checked(world.Generated+amount) : world.Generated };
            if(origin==ResourceOrigin.Carcass)world=Economy.Created(world,foodType,amount);
            quantity-=amount;
        }
        return world;
    }
}
