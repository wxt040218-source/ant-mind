using System.Collections.Immutable;
using AntMind.Contracts;

namespace AntMind.Programs;

public static class AntDslCompiler {
    public const int MaxSourceLength = 8192;
    public const int MaxNodes = 512;
    public const int MaxNesting = 8;
    public const int MaxRepeat = 16;
    public const int MemorySlots = 16;

    public static IAntProgram Compile(string source) {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Length is 0 or > MaxSourceLength) throw new FormatException($"Program source must contain 1 to {MaxSourceLength} characters.");
        var parser = new AntDslParser(source);
        var statements = parser.Parse();
        return new AntDslProgram(statements, parser.Locals);
    }
}

internal sealed class AntDslProgram(IReadOnlyList<DslStatement> statements, IReadOnlyCollection<string> locals) : IAntProgram {
    public Decision Execute(Perception perception, InstructionBudget budget) {
        var memory = perception.Memory.IsDefault
            ? Enumerable.Repeat(0, AntDslCompiler.MemorySlots).ToImmutableArray()
            : perception.Memory;
        if (memory.Length != AntDslCompiler.MemorySlots)
            return new("program-error", budget.Initial - budget.Remaining, perception.CurrentTarget,
                perception.CurrentAction, Memory: memory, Error: "memory_size_invalid");
        var context = new DslExecution(perception, budget, memory, locals);
        try {
            foreach (var statement in statements) statement.Execute(context);
            return context.Decision("dsl-complete");
        } catch (DslBudgetException) {
            return context.Decision("budget-exceeded", "instruction_budget_exceeded");
        } catch (Exception ex) when (ex is DslRuntimeException or OverflowException or DivideByZeroException) {
            return context.Decision("program-error", ex.Message);
        }
    }
}

internal sealed class DslBudgetException : Exception;
internal sealed class DslRuntimeException(string message) : Exception(message);

internal sealed class DslExecution {
    private readonly Perception perception;
    private readonly InstructionBudget budget;
    private readonly Dictionary<string, int> variables;
    private readonly ImmutableArray<int>.Builder memory;
    private readonly Dictionary<(bool Queen,bool Job),ResourceView?> resourceCache=[];
    private bool supplyScanned;
    private (BuildingView? Site,string Food) supply=(null,"");
    public DslExecution(Perception perception, InstructionBudget budget,
        ImmutableArray<int> memory, IReadOnlyCollection<string> locals) {
        this.perception = perception;
        this.budget = budget;
        this.memory = memory.ToBuilder();
        variables = locals.ToDictionary(x => x, _ => 0, StringComparer.Ordinal);
        Target = perception.CurrentTarget;
        Action = perception.CurrentAction == AntAction.Move ? AntAction.Move : AntAction.Wait;
    }
    public ProductionIntent Production {get;private set;}=new();
    public string EatFood { get; private set; } = FoodTypes.Fruit;
    public int EatHolderId {get;private set;}=-1;
    public int PickupFoodId { get; private set; }
    public int FollowAntId { get; private set; }
    public int FollowDistance { get; private set; }
    public int ClaimFoodId { get; private set; }
    public int ClaimQuantity { get; private set; }
    public int ClaimDuration { get; private set; }
    public bool ReleaseClaim { get; private set; }
    public int AttackTargetId { get; private set; }
    public bool RequestQueenInfo { get; private set; }
    public bool ReportDiscovery { get; private set; }
    public string InstitutionField { get; private set; } = "";
    public string InstitutionTier { get; private set; } = "";
    public Point? Target { get; private set; }
    public AntAction Action { get; private set; }
    public string Branch { get; private set; } = "dsl-idle";
    public Perception Perception => perception;

    public void Spend(int amount = 1) {
        if (!budget.TrySpend(amount)) throw new DslBudgetException();
    }

    public int Variable(string name) {
        Spend();
        return name switch {
            "ant_level"=>perception.Level,"experience"=>perception.Experience,"experience_to_next"=>perception.ExperienceToNext,
            "move_percent"=>perception.MovePercent,"work_percent"=>perception.WorkPercent,"attack_damage"=>perception.AttackDamage,
            "technology_level"=>perception.TechnologyLevel,
            "food_balanced"=>FoodCode(FoodTypes.Balanced),"food_royal"=>FoodCode(FoodTypes.Royal),
            "food_wood"=>FoodCode(FoodTypes.Wood),
            "food_fiber"=>FoodCode(FoodTypes.Fiber),"food_concentrate"=>FoodCode(FoodTypes.Concentrate),"food_meal"=>FoodCode(FoodTypes.Meal),
            "species_builder"=>perception.Species==AntSpeciesIds.Builder?1:0,
            "species_chef"=>perception.Species==AntSpeciesIds.Chef?1:0,
            "source_count"=>perception.Sources.Length,"building_count"=>perception.Buildings.Length,
            "queen_source_count"=>perception.QueenSources.Length,"queen_building_count"=>perception.QueenBuildings.Length,
            "true" => 1,
            "false" => 0,
            "ant_id" => perception.AntId,
            "tick" => checked((int)(perception.Tick % int.MaxValue)),
            "x" => perception.Position.X,
            "y" => perception.Position.Y,
            "has_target"=>Target.HasValue?1:0,
            "target_x"=>Target?.X??0,
            "target_y"=>Target?.Y??0,
            "target_distance"=>Target is {} destination?(int)Math.Min(int.MaxValue,PointMath.Distance(perception.Position,destination)):int.MaxValue,
            "target_reached"=>Target==perception.Position?1:0,
            "nest_x" => perception.Nest.X,
            "nest_y" => perception.Nest.Y,
            "width" => perception.Width,
            "height" => perception.Height,
            "carry" => perception.Carry,
            "capacity" => perception.CarryCapacity,
            "health" => perception.Health,
            "max_health" => perception.MaxHealth,
            "food_fruit" => FoodCode(FoodTypes.Fruit),
            "food_meat" => FoodCode(FoodTypes.Meat),
            "carry_food_type" => FoodCode(perception.CarryFood),
            "nest_fruit" => (int)Math.Min(int.MaxValue,perception.NestStock.GetValueOrDefault(FoodTypes.Fruit)),
            "nest_meat" => (int)Math.Min(int.MaxValue,perception.NestStock.GetValueOrDefault(FoodTypes.Meat)),
            "nearest_supply_id"=>NearestSupply().Site?.Id??0,
            "nearest_supply_x"=>NearestSupply().Site?.Position.X??0,
            "nearest_supply_y"=>NearestSupply().Site?.Position.Y??0,
            "nearest_supply_food"=>FoodCode(NearestSupply().Food),
            "nearest_supply_distance"=>NearestSupply().Site is {} supplySite?Math.Max(Math.Abs(supplySite.Position.X-perception.Position.X),Math.Abs(supplySite.Position.Y-perception.Position.Y)):int.MaxValue,
            "nearest_id" => NearestResource()?.Id??0,
            "nearest_food_type" => FoodCode(NearestResource()?.FoodType??""),
            "group_id" => perception.GroupId,
            "group_task" => perception.Order?.Activity switch { GroupActivities.Collect=>1,GroupActivities.Explore=>2,GroupActivities.Guard=>3,_=>0 },
            "task_collect" => 1,"task_explore" => 2,"task_guard" => 3,
            "group_food_type" => FoodCode(perception.Order?.FoodFilter??""),
            "group_radius" => perception.Order?.RadiusCells??0,
            "job_food_id" => NearestResource(false,true)?.Id??0,
            "job_food_amount" => NearestResource(false,true)?.Amount??0,
            "job_food_x" => NearestResource(false,true)?.Position.X??0,
            "job_food_y" => NearestResource(false,true)?.Position.Y??0,
            "job_food_distance" => NearestResource(false,true) is {} jf ? Math.Max(Math.Abs(jf.Position.X-perception.Position.X),Math.Abs(jf.Position.Y-perception.Position.Y)):int.MaxValue,
            "job_queen_amount" => NearestResource(true,true)?.Amount??0,
            "job_queen_x" => NearestResource(true,true)?.Position.X??0,
            "job_queen_y" => NearestResource(true,true)?.Position.Y??0,
            "job_explore_x" => JobExplore().X,
            "job_explore_y" => JobExplore().Y,
            "group_order_version" => perception.Order?.Version??0,
            "rally_x" => perception.Order?.Rally.X??perception.Nest.X,
            "rally_y" => perception.Order?.Rally.Y??perception.Nest.Y,
            "objective_x" => perception.Order?.Objective.X??perception.Nest.X,
            "objective_y" => perception.Order?.Objective.Y??perception.Nest.Y,
            "retreat_x" => perception.Order?.Retreat.X??perception.Nest.X,
            "retreat_y" => perception.Order?.Retreat.Y??perception.Nest.Y,
            "nearest_ally_id" => NearestAlly(false)?.Id??0,
            "nearest_group_member_id" => NearestAlly(true)?.Id??0,
            "species_soldier" => perception.Species == AntSpeciesIds.Soldier ? 1 : 0,
            "nearest_enemy_health" => NearestEnemy()?.Health ?? 0,
            "nearest_enemy_id" => NearestEnemy()?.Id ?? 0,
            "nearest_enemy_x" => NearestEnemy()?.Position.X ?? 0,
            "nearest_enemy_y" => NearestEnemy()?.Position.Y ?? 0,
            "nearest_enemy_distance" => NearestEnemy() is { } enemy ? Math.Max(Math.Abs(enemy.Position.X-perception.Position.X),Math.Abs(enemy.Position.Y-perception.Position.Y)) : int.MaxValue,
            "species_scout" => perception.Species == AntSpeciesIds.Scout ? 1 : 0,
            "species_hauler" => perception.Species == AntSpeciesIds.Hauler ? 1 : 0,
            "nearest_amount" => NearestResource()?.Amount ?? 0,
            "nearest_x" => NearestResource()?.Position.X ?? 0,
            "nearest_y" => NearestResource()?.Position.Y ?? 0,
            "queen_connected" => perception.QueenConnected ? 1 : 0,
            "queen_count" => perception.QueenResources.IsDefault ? 0 : perception.QueenResources.Length,
            "queen_info_tick" => (int)perception.QueenInfoTick,
            "action_status" => (int)perception.ActionStatus,
            "queen_request_status" => (int)perception.QueenRequestStatus,
            "queen_report_status" => (int)perception.QueenReportStatus,
            "at_nest" => Math.Abs((long)perception.Position.X-perception.Nest.X) <= Point.UnitsPerCell && Math.Abs((long)perception.Position.Y-perception.Nest.Y) <= Point.UnitsPerCell ? 1 : 0,
            "nearest_distance" => NearestResource() is { } food ? (int)Math.Max(Math.Abs((long)food.Position.X-perception.Position.X), Math.Abs((long)food.Position.Y-perception.Position.Y)) : int.MaxValue,
            "queen_amount" => NearestResource(true)?.Amount ?? 0,
            "queen_x" => NearestResource(true)?.Position.X ?? 0,
            "queen_y" => NearestResource(true)?.Position.Y ?? 0,
            "explore_x" => ExploreTarget().X,
            "explore_y" => ExploreTarget().Y,
            "institution_version" => perception.InstitutionVersion,
            _ when InstitutionCatalog.TryBuiltinCode(name, out var builtinCode) => builtinCode,
            _ when variables.TryGetValue(name, out var value) => value,
            _ => throw new DslRuntimeException($"Unknown variable '{name}'.")
        };
    }

    public int GroupValue(string name,string key) {
        Spend();
        var order=perception.Order;
        return name switch {
            "require_param"=>order is not null && order.Parameters.TryGetValue(key,out var parameter)?parameter:Missing("parameter",key),
            "require_group_x"=>order is not null && order.Points.TryGetValue(key,out var pointX)?pointX.X:Missing("point",key),
            "require_group_y"=>order is not null && order.Points.TryGetValue(key,out var pointY)?pointY.Y:Missing("point",key),
            "item_code"=>FoodCode(key),
            "param"=>order?.Parameters.GetValueOrDefault(key)??0,
            "has_param"=>order?.Parameters.ContainsKey(key)==true?1:0,
            "group_x"=>order?.Points.GetValueOrDefault(key).X??0,
            "group_y"=>order?.Points.GetValueOrDefault(key).Y??0,
            "has_point"=>order?.Points.ContainsKey(key)==true?1:0,
            _=>throw new DslRuntimeException("invalid_group_query")
        };
    }
    public void SetVariable(string name, int value) {
        Spend();
        if (!variables.ContainsKey(name)) throw new DslRuntimeException($"Unknown variable '{name}'.");
        variables[name] = value;
    }

    private int Missing(string kind,string key) {
        Action=AntAction.Wait;Target=null;
        throw new DslRuntimeException("missing_group_"+kind+":"+key);
    }
    private int RecipeReady(int station,int code) {
        var building=BuildingAt(station);
        if(building is null || building.Phase!="ready" || building.Recipe.Length>0)return 0;
        foreach(var recipe in perception.Recipes) {
            Spend();if(recipe.Code!=code)continue;
            if(perception.TechnologyLevel<recipe.RequiredNetworkLevel || building.Type!=recipe.BuildingType)return 0;
            foreach(var input in recipe.Ingredients()) {Spend();if(building.Store.Get(input.Key)<input.Value)return 0;}
            foreach(var type in perception.BuildingTypes) {Spend();if(type.Id==building.Type)return building.Output.Total+recipe.OutputCount<=type.Capacity?1:0;}
            return 0;
        }
        return 0;
    }
    public int ReadMemory(int index) {
        Spend();
        if (index is < 0 or >= AntDslCompiler.MemorySlots) throw new DslRuntimeException("memory_index_out_of_range");
        return memory[index];
    }

    public void WriteMemory(int index, int value) {
        Spend();
        if (index is < 0 or >= AntDslCompiler.MemorySlots) throw new DslRuntimeException("memory_index_out_of_range");
        memory[index] = value;
    }

    private SourceView? SourceAt(int id,bool queen=false) {foreach(var s in queen?perception.QueenSources:perception.Sources){Spend();if(s.Id==id)return s;}return null;}
    private BuildingView? BuildingAt(int id,bool queen=false) {foreach(var b in queen?perception.QueenBuildings:perception.Buildings){Spend();if(b.Id==id)return b;}return null;}
    public int Function(string name, int[] values) {
        Spend();
        return name switch {
            "recipe_ready"=>RecipeReady(values[0],values[1]),
            "recipe_unlocked"=>RecipeUnlocked(values[0]),
            "food_value"=>perception.FoodDefinitions.FirstOrDefault(x=>x.Id==FoodName(values[0])) is {Edible:true} item?item.Value:0,
            "source_id_at"=>SourceIndex(values[0],false),"queen_source_id_at"=>SourceIndex(values[0],true),
            "building_id_at"=>BuildingIndex(values[0],false),"queen_building_id_at"=>BuildingIndex(values[0],true),
            "source_x"=>SourceAt(values[0])?.Position.X??0,"source_y"=>SourceAt(values[0])?.Position.Y??0,
            "source_amount"=>SourceAt(values[0])?.Amount??0,"source_type"=>FoodCode(SourceAt(values[0])?.ItemType??""),
            "queen_source_x"=>SourceAt(values[0],true)?.Position.X??0,"queen_source_y"=>SourceAt(values[0],true)?.Position.Y??0,
            "queen_source_amount"=>SourceAt(values[0],true)?.Amount??0,"queen_source_type"=>FoodCode(SourceAt(values[0],true)?.ItemType??""),
            "building_x"=>BuildingAt(values[0])?.Position.X??0,"building_y"=>BuildingAt(values[0])?.Position.Y??0,
            "building_ready"=>BuildingAt(values[0])?.Phase=="ready"?1:0,"building_busy"=>BuildingAt(values[0]) is {Recipe.Length:>0}?1:0,
            "building_can_feed"=>BuildingAt(values[0]) is {Phase:"ready"} feeding && CanFeed(feeding)?1:0,
            "building_phase"=>BuildingAt(values[0])?.Phase switch {"blueprint"=>1,"ready"=>2,"cancelled"=>3,_=>0},
            "building_material"=>FoodCode(BuildingAt(values[0]) is {} site?(site.ConstructionItem.Length>0?site.ConstructionItem:perception.BuildingTypes.FirstOrDefault(d=>d.Id==site.Type)?.MaterialType??""):""),
            "building_cost"=>BuildingAt(values[0]) is {} costSite?perception.BuildingTypes.FirstOrDefault(d=>d.Id==costSite.Type)?.MaterialCost??0:0,
            "building_work"=>BuildingAt(values[0])?.ConstructionWork??0,"recipe_work"=>BuildingAt(values[0])?.RecipeWork??0,
            "holder_stock"=>(int)Math.Min(int.MaxValue,BuildingAt(values[0])?.Store.Get(FoodName(values[1]))??0),
            "holder_output"=>(int)Math.Min(int.MaxValue,BuildingAt(values[0])?.Output.Get(FoodName(values[1]))??0),
            "abs" when values.Length == 1 => Math.Abs(values[0]),
            "min" when values.Length == 2 => Math.Min(values[0], values[1]),
            "max" when values.Length == 2 => Math.Max(values[0], values[1]),
            "queen_x_at" when values.Length == 1 => QueenAt(values[0])?.Position.X ?? 0,
            "queen_y_at" when values.Length == 1 => QueenAt(values[0])?.Position.Y ?? 0,
            "queen_amount_at" when values.Length == 1 => QueenAt(values[0])?.Amount ?? 0,
            "queen_food_type_at" => FoodCode(QueenAt(values[0])?.FoodType??""),
            "claimed_amount" => ClaimedAmount(values[0]),
            "ally_x" => AllyAt(values[0])?.Position.X??0,
            "ally_y" => AllyAt(values[0])?.Position.Y??0,
            "ally_health" => AllyAt(values[0])?.Health??0,
            "institution" when values.Length == 1 => CurrentInstitutionTierCode(values[0]),
            "stock" => (int)Math.Min(int.MaxValue,perception.NestStock.GetValueOrDefault(FoodName(values[0]))),
            _ => throw new DslRuntimeException($"Invalid function or argument count '{name}'.")
        };
    }

    private int RecipeUnlocked(int code) {foreach(var r in perception.Recipes){Spend();if(r.Code==code)return perception.TechnologyLevel>=r.RequiredNetworkLevel?1:0;}return 0;}
    // institution(fieldCode) 返回该字段当前档位的全局整数码，供 DSL 与 Builtin 常量比较；纯读感知投影。
    private int CurrentInstitutionTierCode(int fieldCode) =>
        fieldCode is < 1 or > 5 || perception.InstitutionTierCodes.IsDefault || perception.InstitutionTierCodes.Length != 5
            ? 0 : perception.InstitutionTierCodes[fieldCode - 1];
    private int SourceIndex(int index,bool queen) {Spend();var list=queen?perception.QueenSources:perception.Sources;return index>=0 && index<list.Length?list[index].Id:0;}
    private int BuildingIndex(int index,bool queen) {Spend();var list=queen?perception.QueenBuildings:perception.Buildings;return index>=0 && index<list.Length?list[index].Id:0;}
    public void ActionStatement(string name, int[] values) {
        Spend(2);
        switch (name) {
            case "harvest":Production=new(values[0],Quantity:values[1]);Action=AntAction.Harvest;Target=null;Branch=name;break;
            case "deposit":case "withdraw":case "transfer":Production=new(values[0],FoodName(values[1]),values[2]);Action=name=="deposit"?AntAction.Deposit:name=="withdraw"?AntAction.Withdraw:AntAction.Transfer;Target=null;Branch=name;break;
            case "process":Production=new(values[0],RecipeCode:values[1]);Action=AntAction.Process;Target=null;Branch=name;break;
            case "work":Production=new(values[0]);Action=AntAction.Work;Target=null;Branch=name;break;
            case "pickup_from": PickupFoodId=values[0];Action=AntAction.Pickup;Branch="pickup_from";break;
            case "eat": EatHolderId=-1;EatFood=FoodName(values[0]);Target=null;Action=AntAction.Eat;Branch="eat";break;
            case "eat_from":EatHolderId=values[0];EatFood=FoodName(values[1]);Target=null;Action=AntAction.Eat;Branch="eat_from";break;
            case "follow": FollowAntId=values[0];FollowDistance=values[1];Action=AntAction.Follow;Branch="follow";break;
            case "claim_food": ClaimFoodId=values[0];ClaimQuantity=values[1];ClaimDuration=values[2];Branch="claim_food";break;
            case "release_claim": ReleaseClaim=true;Branch="release_claim";break;
            case "set_target" when values.Length == 2:
            case "move_to" when values.Length == 2:
                var maxX = (perception.Width - 1) * Point.UnitsPerCell;
                var maxY = (perception.Height - 1) * Point.UnitsPerCell;
                Target = new(Math.Clamp(values[0], 0, maxX), Math.Clamp(values[1], 0, maxY));
                Action = AntAction.Move;
                Branch = name;
                break;
            case "attack" when values.Length == 1:
                AttackTargetId = values[0]; Target = null; Action = AntAction.Attack; Branch = "attack"; break;
            case "pickup" when values.Length == 0:
                Action = AntAction.Pickup;
                Branch = "pickup";
                break;
            case "deliver" when values.Length == 0:
                Target = null;
                Action = AntAction.Deliver;
                Branch = "deliver";
                break;
            case "drop" when values.Length == 0:
                Target = null; Action = AntAction.Drop; Branch = "drop"; break;
            case "request_queen_info" when values.Length == 0:
                RequestQueenInfo = true; Branch = "request_queen_info"; break;
            case "report_discovery" when values.Length == 0:
                ReportDiscovery = true; Branch = "report_discovery"; break;
            case "set_institution" when values.Length == 2:
                InstitutionField = InstitutionCatalog.FieldName(values[0]);
                InstitutionTier = InstitutionCatalog.TierName(values[1]);
                Branch = "set_institution"; break;
            case "wait" when values.Length == 0:
                Target = null;
                Action = AntAction.Wait;
                Branch = "wait";
                break;
            default:
                throw new DslRuntimeException($"Invalid action or argument count '{name}'.");
        }
    }

    public void SetBranch(string branch) => Branch = branch;

    public Decision Decision(string branch, string? error = null) => new(branch == "dsl-complete" && Branch != "dsl-idle"
            ? Branch : branch,
        budget.Initial - budget.Remaining, Target, Action, memory.ToImmutable(), error, RequestQueenInfo, ReportDiscovery) { Production=Production,AttackTargetId = AttackTargetId, PickupFoodId=PickupFoodId, EatFood=EatFood,FollowAntId=FollowAntId,FollowDistance=FollowDistance,
            ClaimFoodId=ClaimFoodId,ClaimQuantity=ClaimQuantity,ClaimDuration=ClaimDuration,ReleaseClaim=ReleaseClaim,EatHolderId=EatHolderId,
            InstitutionField=InstitutionField,InstitutionTier=InstitutionTier };

    private int FoodCode(string id) {
        for(int i=0;i<perception.FoodDefinitions.Length;i++) if(perception.FoodDefinitions[i].Id==id) return i+1;
        return 0;
    }
    private string FoodName(int code) => code>0 && code<=perception.FoodDefinitions.Length?perception.FoodDefinitions[code-1].Id:"unknown";
    private bool CanFeed(BuildingView site) {
        foreach(var definition in perception.BuildingTypes){Spend();if(definition.Id==site.Type)return definition.CanFeedAnts;}
        return false;
    }
    private (BuildingView? Site,string Food) NearestSupply() {
        if(supplyScanned)return supply;supplyScanned=true;long best=long.MaxValue;
        foreach(var site in perception.Buildings.OrderBy(b=>b.Id)) {
            Spend();if(site.Phase!="ready" || !CanFeed(site))continue;
            var distance=Math.Max(Math.Abs((long)site.Position.X-perception.Position.X),Math.Abs((long)site.Position.Y-perception.Position.Y));
            if(distance>=best)continue;
            foreach(var food in perception.FoodDefinitions) {
                Spend();if(!food.Edible || food.HealAmount<=0 || site.Store.Get(food.Id)<=0)continue;
                supply=(site,food.Id);best=distance;break;
            }
        }
        return supply;
    }
    private AllyView? AllyAt(int id) {
        foreach(var ally in perception.NearbyAllies) { Spend();if(ally.Id==id) return ally; }return null;
    }
    private AllyView? NearestAlly(bool sameGroup) {
        AllyView? nearest=null;long best=long.MaxValue;
        foreach(var ally in perception.NearbyAllies) {
            Spend(); if(sameGroup && (perception.GroupId==0 || ally.GroupId!=perception.GroupId)) continue;
            var d=Math.Abs((long)ally.Position.X-perception.Position.X)+Math.Abs((long)ally.Position.Y-perception.Position.Y);
            if(d<best) { nearest=ally;best=d; }
        }return nearest;
    }
    private int ClaimedAmount(int foodId) {
        var seen=new HashSet<int>();var sum=0;
        foreach(var claim in perception.NearbyClaims.Concat(perception.QueenClaims)) {
            Spend();if(claim.FoodId==foodId && claim.ExpiresTick>perception.Tick && seen.Add(claim.AntId)) sum=checked(sum+claim.Quantity);
        }return sum;
    }
    private ResourceView? NearestResource(bool queen = false,bool job = false) {
        if(resourceCache.TryGetValue((queen,job),out var cached)) return cached;
        ResourceView? nearest = null;
        long best = long.MaxValue;
        foreach (var resource in queen ? (perception.QueenResources.IsDefault ? ImmutableArray<ResourceView>.Empty : perception.QueenResources) : perception.Nearby) {
            Spend();
            var distance = Math.Abs((long)resource.Position.X - perception.Position.X) +
                Math.Abs((long)resource.Position.Y - perception.Position.Y);
            if (resource.Amount > 0 && (!job || MatchesJob(resource)) && distance < best) { nearest = resource; best = distance; }
        }
        resourceCache[(queen,job)]=nearest;
        return nearest;
    }

    private EnemyView? NearestEnemy() {
        EnemyView? nearest = null; long best = long.MaxValue;
        foreach (var enemy in perception.NearbyEnemies) {
            Spend();
            var distance = Math.Abs((long)enemy.Position.X-perception.Position.X)+Math.Abs((long)enemy.Position.Y-perception.Position.Y);
            if (enemy.Health>0 && distance<best) { nearest=enemy; best=distance; }
        }
        return nearest;
    }
    private bool MatchesJob(ResourceView resource) => perception.Order is {} order &&
        (order.FoodFilter.Length==0 || resource.FoodType==order.FoodFilter) &&
        Math.Max(Math.Abs((long)resource.Position.X-order.Objective.X),Math.Abs((long)resource.Position.Y-order.Objective.Y))<=order.RadiusCells*Point.UnitsPerCell;
    private Point JobExplore() {
        if(perception.Order is not {} order)return perception.Nest;
        var radius=order.RadiusCells;var size=radius*2+1;var epoch=perception.Tick/160;
        var x=order.Objective.X+((perception.AntId*17L+epoch*19)%size-radius)*Point.UnitsPerCell;
        var y=order.Objective.Y+((perception.AntId*29L+epoch*23)%size-radius)*Point.UnitsPerCell;
        return new((int)Math.Clamp(x,0,(perception.Width-1)*Point.UnitsPerCell),(int)Math.Clamp(y,0,(perception.Height-1)*Point.UnitsPerCell));
    }
    private Point ExploreTarget() {
        var epoch = perception.Tick / 160;
        var x = (int)((perception.AntId * 17L + epoch * 19) % perception.Width);
        var y = (int)((perception.AntId * 29L + epoch * 23) % perception.Height);
        return Point.FromCells(x, y);
    }

    private ResourceView? QueenAt(int index) =>
        !perception.QueenResources.IsDefault && index >= 0 && index < perception.QueenResources.Length
        ? perception.QueenResources[index] : null;

}

internal abstract record DslExpression {
    public abstract int Evaluate(DslExecution context);
}
internal sealed record DslNumber(int Value) : DslExpression {
    public override int Evaluate(DslExecution context) { context.Spend(); return Value; }
}
internal sealed record DslGroupValue(string Name,string Key) : DslExpression {
    public override int Evaluate(DslExecution context)=>context.GroupValue(Name,Key);
}
internal sealed record DslVariable(string Name) : DslExpression {
    public override int Evaluate(DslExecution context) => context.Variable(Name);
}
internal sealed record DslMemoryRead(DslExpression Index) : DslExpression {
    public override int Evaluate(DslExecution context) { context.Spend(); return context.ReadMemory(Index.Evaluate(context)); }
}
internal sealed record DslCall(string Name, IReadOnlyList<DslExpression> Arguments) : DslExpression {
    public override int Evaluate(DslExecution context) {
        context.Spend();
        return context.Function(Name, Arguments.Select(x => x.Evaluate(context)).ToArray());
    }
}
internal sealed record DslUnary(string Operator, DslExpression Operand) : DslExpression {
    public override int Evaluate(DslExecution context) {
        context.Spend(); var value = Operand.Evaluate(context);
        return Operator switch { "-" => checked(-value), "!" => value == 0 ? 1 : 0, _ => throw new DslRuntimeException("invalid_unary_operator") };
    }
}
internal sealed record DslBinary(string Operator, DslExpression Left, DslExpression Right) : DslExpression {
    public override int Evaluate(DslExecution context) {
        context.Spend();
        var left = Left.Evaluate(context);
        if (Operator == "&&" && left == 0) return 0;
        if (Operator == "||" && left != 0) return 1;
        var right = Right.Evaluate(context);
        return Operator switch {
            "+" => checked(left + right), "-" => checked(left - right), "*" => checked(left * right),
            "/" => left / right, "%" => left % right,
            "==" => left == right ? 1 : 0, "!=" => left != right ? 1 : 0,
            "<" => left < right ? 1 : 0, "<=" => left <= right ? 1 : 0,
            ">" => left > right ? 1 : 0, ">=" => left >= right ? 1 : 0,
            "&&" => right != 0 ? 1 : 0, "||" => right != 0 ? 1 : 0,
            _ => throw new DslRuntimeException("invalid_binary_operator")
        };
    }
}

internal abstract record DslStatement {
    public abstract void Execute(DslExecution context);
}
internal sealed record DslBlock(IReadOnlyList<DslStatement> Statements) : DslStatement {
    public override void Execute(DslExecution context) { foreach (var statement in Statements) statement.Execute(context); }
}
internal sealed record DslLet(string Name, DslExpression Value) : DslStatement {
    public override void Execute(DslExecution context) { context.Spend(); context.SetVariable(Name, Value.Evaluate(context)); }
}
internal sealed record DslAssign(string Name, DslExpression Value) : DslStatement {
    public override void Execute(DslExecution context) { context.Spend(); context.SetVariable(Name, Value.Evaluate(context)); }
}
internal sealed record DslMemoryWrite(DslExpression Index, DslExpression Value) : DslStatement {
    public override void Execute(DslExecution context) { context.Spend(); context.WriteMemory(Index.Evaluate(context), Value.Evaluate(context)); }
}
internal sealed record DslIf(DslExpression Condition, DslBlock Then, DslBlock? Else) : DslStatement {
    public override void Execute(DslExecution context) {
        context.Spend();
        var branch = Condition.Evaluate(context) != 0;
        context.SetBranch(branch ? "if-true" : "if-false");
        (branch ? Then : Else)?.Execute(context);
    }
}
internal sealed record DslRepeat(int Count, DslBlock Body) : DslStatement {
    public override void Execute(DslExecution context) {
        context.Spend();
        for (var i = 0; i < Count; i++) Body.Execute(context);
    }
}
internal sealed record DslAction(string Name, IReadOnlyList<DslExpression> Arguments) : DslStatement {
    public override void Execute(DslExecution context) {
        context.Spend();
        context.ActionStatement(Name, Arguments.Select(x => x.Evaluate(context)).ToArray());
    }
}

internal enum DslTokenKind { Identifier, Number, String, Symbol, End }
internal readonly record struct DslToken(DslTokenKind Kind, string Text, int Offset);

internal sealed class AntDslParser {
    private static readonly HashSet<string> Builtins = new(StringComparer.Ordinal) {
        "nearest_supply_id","nearest_supply_x","nearest_supply_y","nearest_supply_distance","nearest_supply_food",
        "has_target","target_x","target_y","target_distance","target_reached",
        "food_wood","species_builder","ant_level","experience","experience_to_next","move_percent","work_percent","attack_damage","technology_level","food_balanced","food_royal","food_fiber","food_concentrate","food_meal","species_chef","source_count","building_count","queen_source_count","queen_building_count",
        "group_task","task_collect","task_explore","task_guard","group_food_type","group_radius",
        "job_food_id","job_food_amount","job_food_x","job_food_y","job_food_distance","job_queen_amount","job_queen_x","job_queen_y","job_explore_x","job_explore_y",
        "max_health", "food_fruit", "food_meat", "carry_food_type", "nest_fruit", "nest_meat", "nearest_id", "nearest_food_type", "group_id", "group_order_version", "rally_x", "rally_y", "objective_x", "objective_y", "retreat_x", "retreat_y", "nearest_ally_id", "nearest_group_member_id",
        "health", "nearest_enemy_health", "species_soldier", "nearest_enemy_id", "nearest_enemy_x", "nearest_enemy_y", "nearest_enemy_distance", "true", "false", "ant_id", "tick", "x", "y", "nest_x", "nest_y", "width", "height",
        "carry", "capacity", "species_scout", "species_hauler", "nearest_amount", "nearest_x", "nearest_y",
        "at_nest", "nearest_distance", "action_status", "queen_info_tick", "queen_request_status", "queen_report_status", "explore_x", "explore_y", "queen_connected", "queen_count", "queen_amount", "queen_x", "queen_y"
    };
    private static readonly HashSet<string> ExpressionFunctions = new(StringComparer.Ordinal) { "recipe_ready","recipe_unlocked","food_value","source_id_at", "queen_source_id_at", "building_id_at", "queen_building_id_at", "source_x", "source_y", "source_amount", "source_type", "queen_source_x", "queen_source_y", "queen_source_amount", "queen_source_type", "building_x", "building_y", "building_ready", "building_busy", "building_can_feed", "building_phase", "building_material", "building_cost", "building_work", "recipe_work", "holder_stock", "holder_output", "abs", "min", "max", "queen_x_at", "queen_y_at", "queen_amount_at", "queen_food_type_at", "claimed_amount", "ally_x", "ally_y", "ally_health", "stock", "institution" };
    private static readonly HashSet<string> Actions = new(StringComparer.Ordinal) {
        "harvest","deposit","withdraw","transfer","work","process", "pickup_from", "eat", "eat_from", "follow", "claim_food", "release_claim", "attack", "set_target", "move_to", "pickup", "deliver", "wait", "drop", "request_queen_info", "report_discovery", "set_institution"
    };
    static AntDslParser() {
        Builtins.Add("institution_version");
        foreach (var name in InstitutionCatalog.BuiltinNames) Builtins.Add(name);
    }
    private readonly List<DslToken> tokens;
    private readonly HashSet<string> locals = new(StringComparer.Ordinal);
    private int position;
    private int nodeCount;
    private int expressionDepth;
    private int primaryDepth;
    public IReadOnlyCollection<string> Locals => locals;

    public AntDslParser(string source) => tokens = Lex(source);

    public IReadOnlyList<DslStatement> Parse() {
        var statements = new List<DslStatement>();
        while (Current.Kind != DslTokenKind.End) statements.Add(ParseStatement(0));
        if (statements.Count == 0) Error("Program is empty.");
        return statements;
    }

    private DslStatement ParseStatement(int depth) {
        if (Match("if")) return ParseIf(depth);
        if (Match("repeat")) {
            var number = Expect(DslTokenKind.Number, "repeat requires a fixed integer bound.");
            var count = int.Parse(number.Text, System.Globalization.CultureInfo.InvariantCulture);
            if (count is < 1 or > AntDslCompiler.MaxRepeat) ErrorAt(number, $"repeat bound must be 1 to {AntDslCompiler.MaxRepeat}.");
            var body = ParseBlock(depth + 1);
            return Node(new DslRepeat(count, body));
        }
        if (Match("let")) {
            var name = Expect(DslTokenKind.Identifier, "Expected a variable name.");
            if (Builtins.Contains(name.Text) || locals.Contains(name.Text)) ErrorAt(name, $"Variable '{name.Text}' cannot be declared here.");
            Expect("="); var value = ParseExpression(); Expect(";");
            locals.Add(name.Text);
            return Node(new DslLet(name.Text, value));
        }
        if (Match("memory")) {
            Expect("["); var index = ParseExpression(); Expect("]"); Expect("=");
            var value = ParseExpression(); Expect(";");
            return Node(new DslMemoryWrite(index, value));
        }
        var identifier = Expect(DslTokenKind.Identifier, "Expected a statement.");
        if (Match("=")) {
            if (!locals.Contains(identifier.Text)) ErrorAt(identifier, $"Declare '{identifier.Text}' with let before assigning it.");
            var value = ParseExpression(); Expect(";");
            return Node(new DslAssign(identifier.Text, value));
        }
        if (!Actions.Contains(identifier.Text)) ErrorAt(identifier, $"Unknown action '{identifier.Text}'.");
        if (identifier.Text == "set_institution") {
            Expect("(");
            var literals = new List<DslToken>();
            if (!Is(")")) { do { literals.Add(Expect(DslTokenKind.String, "Expected a quoted institution literal.")); } while (Match(",")); }
            Expect(")");
            if (literals.Count != 2) ErrorAt(identifier, "Action 'set_institution' expects 2 argument(s).");
            var fieldToken = literals[0];
            var tierToken = literals[1];
            if (!InstitutionCatalog.IsField(fieldToken.Text)) ErrorAt(fieldToken, $"Invalid institution field '{fieldToken.Text}'.");
            if (!InstitutionCatalog.IsTier(fieldToken.Text, tierToken.Text)) ErrorAt(tierToken, $"Invalid institution tier '{tierToken.Text}' for '{fieldToken.Text}'.");
            Expect(";");
            return Node(new DslAction(identifier.Text, [
                new DslNumber(InstitutionCatalog.FieldCode(fieldToken.Text)),
                new DslNumber(InstitutionCatalog.TierCode(fieldToken.Text, tierToken.Text))]));
        }
        var arguments = ParseArguments(); Expect(";");
        var expected = identifier.Text switch {
            "harvest" or "process" or "set_target" or "move_to" or "follow" or "eat_from" or "set_institution" => 2,
            "deposit" or "withdraw" or "transfer" or "claim_food" => 3,
            "work" or "eat" or "pickup_from" => 1,
            "release_claim" => 0,
            "attack" => 1,
            "pickup" or "deliver" or "wait" or "drop" or "request_queen_info" or "report_discovery" => 0,
            _ => -1
        };
        if (arguments.Count != expected) ErrorAt(identifier, $"Action '{identifier.Text}' expects {expected} argument(s).");
        return Node(new DslAction(identifier.Text, arguments));
    }

    private DslStatement ParseIf(int depth) {
        Expect("("); var condition = ParseExpression(); Expect(")");
        var then = ParseBlock(depth + 1);
        DslBlock? otherwise = null;
        if (Match("else")) {
            if (Match("if")) otherwise = new([ParseIf(depth + 1)]);
            else otherwise = ParseBlock(depth + 1);
        }
        return Node(new DslIf(condition, then, otherwise));
    }

    private DslBlock ParseBlock(int depth) {
        if (depth > AntDslCompiler.MaxNesting) Error("Maximum block nesting exceeded.");
        Expect("{"); var statements = new List<DslStatement>();
        while (!Is("}") && Current.Kind != DslTokenKind.End) statements.Add(ParseStatement(depth));
        Expect("}");
        return Node(new DslBlock(statements));
    }

    private IReadOnlyList<DslExpression> ParseArguments() {
        Expect("("); var args = new List<DslExpression>();
        if (!Is(")")) {
            do { args.Add(ParseExpression()); } while (Match(","));
        }
        Expect(")"); return args;
    }

    private DslExpression ParseExpression(int minimumPrecedence = 1) {
        expressionDepth++;
        if (expressionDepth > AntDslCompiler.MaxNesting) {
            expressionDepth--;
            Error($"Maximum expression nesting of {AntDslCompiler.MaxNesting} exceeded.");
        }
        try {
            var left = ParsePrimary();
            while (Precedence(Current.Text) is var precedence && precedence >= minimumPrecedence) {
                var op = Advance().Text;
                var right = ParseExpression(precedence + 1);
                left = Node(new DslBinary(op, left, right));
            }
            return left;
        } finally { expressionDepth--; }
    }

    private DslExpression ParsePrimary() {
        primaryDepth++;
        if (primaryDepth > AntDslCompiler.MaxNesting) {
            primaryDepth--;
            Error($"Maximum expression nesting of {AntDslCompiler.MaxNesting} exceeded.");
        }
        try {
            if (Match("!")) return Node(new DslUnary("!", ParsePrimary()));
            if (Match("-")) return Node(new DslUnary("-", ParsePrimary()));
            if (Match("(")) { var inner = ParseExpression(); Expect(")"); return inner; }
            if (Current.Kind == DslTokenKind.Number) {
                var token = Advance();
                if (!int.TryParse(token.Text, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var number)) ErrorAt(token, "Integer literal is out of range.");
                return Node(new DslNumber(number));
            }
            if (Match("memory")) {
                Expect("["); var index = ParseExpression(); Expect("]");
                return Node(new DslMemoryRead(index));
            }
            var identifier = Expect(DslTokenKind.Identifier, "Expected an expression.");
            if (Match("(")) {
                if(identifier.Text is "require_param" or "require_group_x" or "require_group_y" or "item_code" or "param" or "has_param" or "group_x" or "group_y" or "has_point") {
                    var key=Expect(DslTokenKind.String,"Expected a quoted group parameter or point name.");
                    if(key.Text.Length is <1 or >40) ErrorAt(key,"Group key must have 1..40 characters.");
                    Expect(")");return Node(new DslGroupValue(identifier.Text,key.Text));
                }
                if (identifier.Text == "institution") {
                    var literals = new List<DslToken>();
                    if (!Is(")")) { do { literals.Add(Expect(DslTokenKind.String, "Expected a quoted institution field.")); } while (Match(",")); }
                    Expect(")");
                    if (literals.Count != 1) ErrorAt(identifier, "Function 'institution' expects 1 argument(s).");
                    var fieldToken = literals[0];
                    if (!InstitutionCatalog.IsField(fieldToken.Text)) ErrorAt(fieldToken, $"Invalid institution field '{fieldToken.Text}'.");
                    return Node(new DslCall(identifier.Text, [new DslNumber(InstitutionCatalog.FieldCode(fieldToken.Text))]));
                }
                if (!ExpressionFunctions.Contains(identifier.Text)) ErrorAt(identifier, $"Unknown expression function '{identifier.Text}'.");
                var args = new List<DslExpression>();
                if (!Is(")")) { do { args.Add(ParseExpression()); } while (Match(",")); }
                Expect(")");
                var expected = identifier.Text switch { "recipe_unlocked" or "food_value" or "source_id_at" or "queen_source_id_at" or "building_id_at" or "queen_building_id_at" or "source_x" or "source_y" or "source_amount" or "source_type" or "queen_source_x" or "queen_source_y" or "queen_source_amount" or "queen_source_type" or "building_x" or "building_y" or "building_ready" or "building_busy" or "building_can_feed" or "building_phase" or "building_material" or "building_cost" or "building_work" or "recipe_work" or "abs" or "queen_x_at" or "queen_y_at" or "queen_amount_at" or "queen_food_type_at" or "claimed_amount" or "ally_x" or "ally_y" or "ally_health" or "stock" or "institution" => 1, "recipe_ready" or "holder_stock" or "holder_output" or "min" or "max" => 2, _ => -1 };
                if (args.Count != expected) ErrorAt(identifier, $"Function '{identifier.Text}' expects {expected} argument(s).");
                return Node(new DslCall(identifier.Text, args));
            }
            if (!Builtins.Contains(identifier.Text) && !locals.Contains(identifier.Text))
                ErrorAt(identifier, $"Unknown variable '{identifier.Text}'.");
            return Node(new DslVariable(identifier.Text));
        } finally { primaryDepth--; }
    }

    private T Node<T>(T node) {
        nodeCount++;
        if (nodeCount > AntDslCompiler.MaxNodes) Error($"Program exceeds {AntDslCompiler.MaxNodes} syntax nodes.");
        return node;
    }

    private static int Precedence(string op) => op switch {
        "||" => 1, "&&" => 2, "==" or "!=" => 3, "<" or "<=" or ">" or ">=" => 4,
        "+" or "-" => 5, "*" or "/" or "%" => 6, _ => 0
    };

    private static List<DslToken> Lex(string source) {
        var result = new List<DslToken>();
        for (var i = 0; i < source.Length;) {
            if (char.IsWhiteSpace(source[i])) { i++; continue; }
            if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '/') {
                i += 2; while (i < source.Length && source[i] is not '\r' and not '\n') i++; continue;
            }
            var start = i; var ch = source[i];
            if(ch=='"') {
                i++;var begin=i;
                while(i<source.Length && source[i]!='"') {
                    if(!char.IsAsciiLetterOrDigit(source[i]) && source[i]!='_' && source[i]!='.' && source[i]!='-') throw new FormatException("Group keys only accept ASCII letters, digits, underscore, dot and dash.");
                    i++;
                }
                if(i==source.Length)throw new FormatException("Unterminated group key.");
                result.Add(new(DslTokenKind.String,source[begin..i],start));i++;continue;
            }
            if (char.IsAsciiLetter(ch) || ch == '_') {
                i++;
                while (i < source.Length && (char.IsAsciiLetterOrDigit(source[i]) || source[i] == '_')) i++;
                result.Add(new(DslTokenKind.Identifier, source[start..i], start)); continue;
            }
            if (char.IsAsciiDigit(ch)) {
                i++; while (i < source.Length && char.IsAsciiDigit(source[i])) i++;
                result.Add(new(DslTokenKind.Number, source[start..i], start)); continue;
            }
            var pair = i + 1 < source.Length ? source.Substring(i, 2) : "";
            if (pair is "==" or "!=" or "<=" or ">=" or "&&" or "||") {
                result.Add(new(DslTokenKind.Symbol, pair, start)); i += 2; continue;
            }
            if ("(){}[];,=+-*/%<>!".Contains(ch)) {
                result.Add(new(DslTokenKind.Symbol, ch.ToString(), start)); i++; continue;
            }
            throw new FormatException($"Unexpected character at offset {start}.");
        }
        result.Add(new(DslTokenKind.End, "", source.Length));
        return result;
    }

    private DslToken Current => tokens[position];
    private DslToken Advance() => tokens[position++];
    private bool Is(string text) => Current.Text == text;
    private bool Match(string text) { if (!Is(text)) return false; Advance(); return true; }
    private DslToken Expect(string text) { if (!Match(text)) Error($"Expected '{text}'."); return tokens[position - 1]; }
    private DslToken Expect(DslTokenKind kind, string message) {
        if (Current.Kind != kind) Error(message);
        return Advance();
    }
    private void Error(string message) => ErrorAt(Current, message);
    private static void ErrorAt(DslToken token, string message) => throw new FormatException($"{message} (offset {token.Offset}).");
}
