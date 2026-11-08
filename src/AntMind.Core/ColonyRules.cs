using System.Collections.Immutable;
using AntMind.Contracts;
namespace AntMind.Core;

public sealed class EatingRule : IWorldRule {
    public string Id => "core.eating";
    public int Order => 32;
    public WorldState Apply(WorldState world) {
        foreach(var original in world.Ants) {
            var ant=original;
            if(ant.HealUntilTick==0 && ant.Intent!=AntAction.Eat) continue;
            var definition=world.Config.Foods.FirstOrDefault(x=>x.Id==ant.EatFood);
            var cargo=ant.Carry>0 && ant.CarryFood==ant.EatFood;
            var atNest=CombatMath.Distance(ant.Position,((QueenObject)world.Objects.Find("queen",1)!).Position)<=Point.UnitsPerCell;
            var error=ant.LastDamageTick==world.Tick ? "eating_interrupted" : ant.Health>=ant.MaxHealth ? "already_healthy" :
                definition is null || !definition.Edible ? "invalid_food_type" : !cargo && !atNest ? "no_carried_food" :
                !cargo && (world.Stock.Get(ant.EatFood)<1 || world.Inventory-1<world.Config.MinimumReserve) ? "insufficient_food" : null;
            if(error is not null) ant=ant with { HealUntilTick=0, Intent=AntAction.Wait, ActionStatus=ActionStatus.Failed,ActionResult=error };
            else if(ant.HealUntilTick==0) ant=ant with { HealUntilTick=checked(world.Tick+world.Config.EatingTicks),
                Intent=AntAction.Wait,Target=null,ActionStatus=ActionStatus.Running,ActionResult="eating" };
            else if(world.Tick>=ant.HealUntilTick) {
                var restored=Math.Min(definition!.HealAmount,ant.MaxHealth-ant.Health);
                if(cargo) ant=ant with { Carry=ant.Carry-1,CargoCredits=ItemCredit.Split(Economy.Credits(ant),1).Left,CargoTracked=true };
                else world=world with { Stock=world.Stock.Add(ant.EatFood,-1) };
                ant=ant with { Health=ant.Health+restored,HealUntilTick=0,Intent=AntAction.Wait,NextDecisionTick=world.Tick+1,
                    ActionStatus=ActionStatus.Succeeded,ActionResult="ate_"+ant.EatFood };
                world=Economy.Consumed(world,ant.EatFood,1);
                world=world with { FoodEaten=checked(world.FoodEaten+1),HealthRestored=checked(world.HealthRestored+restored) };
            }
            world=world with { Objects=world.Objects.Put(ant) };
        }
        return world;
    }
}

public sealed class ClaimRule : IWorldRule {
    public string Id=>"core.claim";
    public int Order=>16;
    public WorldState Apply(WorldState world) {
        var claims=world.Claims.Where(c=>c.ExpiresTick>world.Tick && world.Ants.Any(a=>a.Id==c.AntId) &&
            world.Resources.Any(f=>f.Id==c.FoodId)).ToList();
        foreach(var ant in world.Ants) {
            if(!ant.ReleaseClaim && ant.ClaimFoodId==0) continue;
            var error="";
            if(ant.ReleaseClaim) claims.RemoveAll(x=>x.AntId==ant.Id);
            if(ant.ClaimFoodId!=0) {
                var food=world.Resources.FirstOrDefault(f=>f.Id==ant.ClaimFoodId &&
                    CombatMath.Distance(f.Position,ant.Position)<=world.Config.AntType(ant.Species).VisionCells*Point.UnitsPerCell);
                if(food is null) error="food_not_visible";
                else if(ant.ClaimQuantity is <1 or >100 || ant.ClaimDuration is <1 or >1200) error="invalid_claim";
                else {
                    var available=Math.Max(0,food.Amount-claims.Where(c=>c.FoodId==food.Id && c.AntId!=ant.Id).Sum(c=>c.Quantity));
                    if(available==0) error="food_fully_claimed";
                    else {
                        claims.RemoveAll(c=>c.AntId==ant.Id);
                        claims.Add(new(ant.Id,food.Id,Math.Min(available,ant.ClaimQuantity),checked(world.Tick+ant.ClaimDuration)));
                    }
                }
            }
            world=world with { Objects=world.Objects.Put(ant with { ClaimFoodId=0,ReleaseClaim=false,
                ClaimQuantity=0,ClaimDuration=0,ActionStatus=error.Length==0?ActionStatus.Succeeded:ActionStatus.Failed,
                ActionResult=error.Length==0?"claim_updated":error }) };
        }
        return world with { Claims=claims.OrderBy(c=>c.AntId).ToImmutableArray() };
    }
}

// Run after combat and pickup, so deaths/depletion cannot leave public reservations alive for a Tick.
public sealed class ClaimCleanupRule : IWorldRule {
    public string Id=>"core.claim-cleanup";
    public int Order=>46;
    public WorldState Apply(WorldState world) {
        var result=ImmutableArray.CreateBuilder<ResourceClaim>();
        var remaining=world.Resources.ToDictionary(f=>f.Id,f=>f.Amount);
        foreach(var claim in world.Claims.OrderBy(c=>c.AntId)) {
            if(claim.ExpiresTick<=world.Tick || !world.Ants.Any(a=>a.Id==claim.AntId)) continue;
            var quantity=Math.Min(claim.Quantity,remaining.GetValueOrDefault(claim.FoodId));
            if(quantity<=0)continue;
            result.Add(claim with { Quantity=quantity });remaining[claim.FoodId]-=quantity;
        }
        return world with { Claims=result.ToImmutable() };
    }
}

public sealed class FollowRule : IWorldRule {
    public string Id=>"core.follow";
    public int Order=>19;
    public WorldState Apply(WorldState world) {
        foreach(var ant in world.Ants.Where(a=>a.Intent==AntAction.Follow)) {
            var ally=world.Ants.FirstOrDefault(a=>a.Id==ant.FollowAntId && a.Id!=ant.Id &&
                CombatMath.Distance(a.Position,ant.Position)<=world.Config.AntType(ant.Species).VisionCells*Point.UnitsPerCell);
            var valid=ally is not null && ant.FollowDistance is >=0 and <=16000;
            var distance=ally is null?0:CombatMath.Distance(ant.Position,ally.Position);
            var target=valid && distance>ant.FollowDistance ? new Point(
                ally!.Position.X+(int)((long)(ant.Position.X-ally.Position.X)*ant.FollowDistance/distance),
                ally.Position.Y+(int)((long)(ant.Position.Y-ally.Position.Y)*ant.FollowDistance/distance)) : ant.Position;
            world=world with { Objects=world.Objects.Put(ant with { Target=valid?target:null,
                Intent=valid && distance>ant.FollowDistance?AntAction.Move:AntAction.Wait,
                ActionStatus=valid?ActionStatus.Succeeded:ActionStatus.Failed,ActionResult=valid?"following":"ally_not_visible" }) };
        }
        return world;
    }
}
