using System.Collections.Immutable;
using AntMind.Contracts;

namespace AntMind.Core;

public static class ResourcePlacement {
    public static WorldState TryAdd(WorldState world) {
        var config = world.Config;
        var available = config.GroundResourceCap - world.Resources.Sum(r => r.Amount);
        if (world.Resources.Length >= config.ResourceNodeCap || available < config.ResourceMin) return world;
        var random = world.RandomState;
        var nest = Point.FromCells(world.Config.Width / 2, world.Config.Height / 2);
        for (int attempt = 0; attempt < 128; attempt++) {
            var x = (int)(RandomSource.Next(ref random) % (uint)world.Config.Width);
            var y = (int)(RandomSource.Next(ref random) % (uint)world.Config.Height);
            var p = Point.FromCells(x, y);
            long dx = p.X - nest.X, dy = p.Y - nest.Y;
            if (dx * dx + dy * dy < (long)config.NestExclusionCells * config.NestExclusionCells * Point.UnitsPerCell * Point.UnitsPerCell ||
                world.Resources.Any(r => r.Position == p)) continue;
            var amount = Math.Min(config.ResourceMin + (int)(RandomSource.Next(ref random) % (uint)(config.ResourceMax - config.ResourceMin + 1)), available);
            return Economy.Created(world with {
                RandomState = random,
                Resources = world.Resources.Add(new(world.NextResourceId, p, amount, world.Tick, amount)),
                NextResourceId = checked(world.NextResourceId + 1),
                Generated = checked(world.Generated + amount)
            },FoodTypes.Fruit,amount);
        }
        return world with { RandomState = random };
    }
}

public sealed class MovementRule : IWorldRule {
    public string Id => "core.move";
    public int Order => 20;
    public WorldState Apply(WorldState world) {
        var ants = world.Ants.ToBuilder();
        for (int i = 0; i < ants.Count; i++) {
            var ant = ants[i];
            if (ant.Target is not { } target || ant.Intent != AntAction.Move) continue;
            var x = Math.Clamp(target.X, 0, (world.Config.Width - 1) * Point.UnitsPerCell);
            var y = Math.Clamp(target.Y, 0, (world.Config.Height - 1) * Point.UnitsPerCell);
            var species = world.Config.AntType(ant.Species);
            // Speed depends on absolute load, not the currently unlocked capacity.
            var capacity = checked(species.CarryCapacity *
                (ant.Species == AntSpeciesIds.Hauler ? world.Config.CarryMultipliers[^1] : 1));
            var load = Math.Clamp(ant.Carry, 0, capacity);
            var speedLoss = (species.EmptyMoveUnitsPerTick - species.LoadedMoveUnitsPerTick) * load /
                capacity;
            var distancePerTick = checked((species.EmptyMoveUnitsPerTick-speedLoss)*world.Config.Veterancy.MovePercent[ant.Level-1]/100);
            var destination=new Point(x,y);
            var next=PointMath.Towards(ant.Position,destination,distancePerTick);
            var arrived=next==destination;
            ants[i] = ant with { Position=next, Target=destination, Intent = arrived ? AntAction.Wait : AntAction.Move,
                ActionStatus = arrived ? ActionStatus.Succeeded : ActionStatus.Running,
                ActionResult = arrived ? "arrived" : "moving" };
        }
        return world with { Ants = ants.ToImmutable() };
    }
}

public sealed class InteractionRule : IWorldRule {
    public string Id => "core.interaction";
    public int Order => 30;
    public WorldState Apply(WorldState world) {
        var ants = world.Ants.ToBuilder();
        var resources = world.Resources.ToBuilder();
        var stock = world.Stock;
        var delivered = world.Delivered;
        var bySpecies = world.DeliveredBySpecies.ToBuilder();
        var nextId = world.NextResourceId;
        var nest = Point.FromCells(world.Config.Width/2, world.Config.Height/2);
        var order = InstitutionMechanics.InteractionIndices(world, world.Ants);
        for (var step = 0; step < order.Length; step++) {
            var i = order[step];
            var ant = ants[i];
            if (ant.Intent is not (AntAction.Pickup or AntAction.Deliver or AntAction.Drop)) continue;
            var result = "invalid_interaction";
            var success = false;
            if (world.Tick-ant.LastInteractionTick < world.Config.InteractionCooldownTicks) result = "interaction_cooldown";
            else if (ant.Intent == AntAction.Pickup) {
                var capacity = world.CarryCapacity(ant.Species);
                var index = -1;
                for (var j = 0; j < resources.Count; j++)
                    if (resources[j].Amount > 0 && (ant.PickupFoodId==0 || resources[j].Id==ant.PickupFoodId) && (ant.Carry == 0 || resources[j].FoodType == ant.CarryFood) && Near(ant.Position,resources[j].Position)) { index = j; break; }
                if (ant.Carry >= capacity) result = "capacity_full";
                else if (index < 0) result = "no_food_in_reach";
                else {
                    var resource = resources[index];
                    var quantity = Math.Min(capacity-ant.Carry, resource.Amount);
                    var credit=ItemCredit.Split(Economy.Credits(resource),quantity);
                    if (resource.Amount == quantity) resources.RemoveAt(index);
                    else resources[index] = resource with { Amount = resource.Amount-quantity,Credits=credit.Left,CreditTracked=true };
                    ant = ant with { CargoCredits=ItemCredit.Merge(Economy.Credits(ant),credit.Taken),CargoTracked=true,Carry = ant.Carry+quantity, CarryFood = resource.FoodType };
                    success = true; result = "picked_up";
                }
            } else if (ant.Intent == AntAction.Deliver) {
                if (ant.Carry == 0) result = "empty_load";
                else if (!Near(ant.Position,nest)) result = "not_at_nest";
                else {
                    stock = stock.Add(ant.CarryFood,ant.Carry);
                    world=Economy.Admit(world,ant,ant.CarryFood,ant.Carry,Economy.Credits(ant));
                    var edible=world.Config.Foods.Any(x=>x.Id==ant.CarryFood && x.Edible);
                    delivered = checked(delivered+(edible?ant.Carry:0));
                    bySpecies[ant.Species] = checked(bySpecies.GetValueOrDefault(ant.Species)+(edible?ant.Carry:0));
                    ant = ant with { Carry = 0,CargoCredits=[],CargoTracked=true };
                    success = true; result = "delivered";
                }
            } else if (ant.Carry == 0) result = "empty_load";
            else {
                var remaining = ant.Carry;
                var remainingCredits=Economy.Credits(ant);
                for (var j = 0; j < resources.Count && remaining > 0; j++) {
                    var food = resources[j];
                    if (food.Position != ant.Position || food.FoodType != ant.CarryFood) continue;
                    var quantity = Math.Min(remaining, food.MaxCapacity - food.Amount);
                    if (quantity <= 0) continue;
                    var credit=ItemCredit.Split(remainingCredits,quantity);remainingCredits=credit.Left;
                    resources[j] = food with { Amount = food.Amount + quantity,Credits=ItemCredit.Merge(Economy.Credits(food),credit.Taken),CreditTracked=true };
                    remaining -= quantity;
                }
                // Dropped cargo may create several independent piles; never enlarge an existing capacity.
                while (remaining > 0) {
                    var quantity = Math.Min(remaining, world.Config.ResourceMax);
                    var credit=ItemCredit.Split(remainingCredits,quantity);remainingCredits=credit.Left;
                    resources.Add(new ResourceView(nextId, ant.Position, quantity, world.Tick, quantity) {
                        Credits=credit.Taken,CreditTracked=true,FoodType = ant.CarryFood, Origin = ResourceOrigin.Dropped, IsDiscovered = true, DiscoveredTick = world.Tick, LastObservedAmount = quantity, LastObservedTick = world.Tick
                    });
                    nextId = checked(nextId + 1); remaining -= quantity;
                }
                ant = ant with { Carry = 0,CargoCredits=[],CargoTracked=true };
                success = true; result = "dropped";
            }
            ants[i] = ant with { Intent = AntAction.Wait, Target = null, ActionStatus = success ? ActionStatus.Succeeded : ActionStatus.Failed,
                ActionResult = result, LastInteractionTick = success ? world.Tick : ant.LastInteractionTick };
        }
        return world with { Ants = ants.ToImmutable(), Resources = resources.ToImmutable(), Stock = stock,
            Delivered = delivered, DeliveredBySpecies = bySpecies.ToImmutable(), NextResourceId = nextId,
            Claims=world.Claims.Where(c=>!ants.Any(a=>a.Id==c.AntId && a.ActionResult=="picked_up" && a.LastInteractionTick==world.Tick)).ToImmutableArray() };
    }
    private static bool Near(Point a, Point b) => Math.Abs((long)a.X-b.X) <= Point.UnitsPerCell && Math.Abs((long)a.Y-b.Y) <= Point.UnitsPerCell;
}

public sealed class ResourceRefreshRule : IWorldRule {
    public string Id => "core.resource-refresh";
    public int Order => 40;
    public WorldState Apply(WorldState world) => world.Tick % world.Config.ResourceRefreshTicks == 0 ? ResourcePlacement.TryAdd(world) : world;
}
