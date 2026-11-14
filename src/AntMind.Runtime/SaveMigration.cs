using System.Collections.Immutable;
using AntMind.Contracts;
using AntMind.Core;
using AntMind.Programs;
using System.Text.Json.Nodes;
namespace AntMind.Runtime;

// v1 files remain untouched; load upgrades an immutable candidate before any live mutation.
internal static class SaveMigration {
    public static SavedGame Upgrade(SavedGame save) {
        var legacy=save.Protocol is >=12 and <=16;
        if(save.Format==1 && save.Protocol==12)save=UpgradeV1(save);
        if(save.Format==2 && save.Protocol==13) {
            var cells=save.World.Ants.Select(a=>AntProgression.Cell(save.World.Config,a.Position)).ToImmutableHashSet();
            save=save with {Format=3,Protocol=14,World=save.World with {ExploredCells=cells,PendingExperience=[]}};
        }
        if(save.Format==3 && save.Protocol==14) {
            var world=save.World;var config=world.Config;
            var builder=new AntTypeConfig(AntSpeciesIds.Builder,"建筑蚁",0,110,70,2,4) {MaxHealth=20,WorkRate=2};
            if(!config.AntTypes.Any(a=>a.Id==builder.Id))config=config with {AntTypes=config.AntTypes.Add(builder)};
            if(config.Production.Enabled && !config.Foods.Any(f=>f.Id==FoodTypes.Wood))config=config with {Foods=config.Foods.Add(new(FoodTypes.Wood,"木材",0) {Edible=false,Value=0})};
            var objects=world.Objects;
            foreach(var site in objects.All<BuildingView>())objects=objects.Put(site with {ConstructionItem=site.ConstructionItem.Length>0?site.ConstructionItem:FoodTypes.Fiber});
            var observed=world.ObservedBuildings.ToImmutableDictionary(x=>x.Key,x=>x.Value with {ConstructionItem=x.Value.ConstructionItem.Length>0?x.Value.ConstructionItem:FoodTypes.Fiber});
            world=world with {Config=config,Objects=objects,ObservedBuildings=observed};
            if(config.Production.Enabled)world=ProductionFactory.AddWoodSources(world);
            var programs=save.Programs;
            if(!programs.Any(p=>p.Species==builder.Id))programs=programs.Add(new(builder.Id,0,null,[new(0,"built-in builder added by migration",DefaultProgramSources.Builder)]));
            save=save with {Format=4,Protocol=15,World=world,Programs=programs};
        }
        // 固定写 5/18：不能用 SavedGames.Format/Protocol.Version 常量，否则升 6/19 后旧档会跳过下一级。
        if(save.Format==4 && save.Protocol is >=15 and <=17) {
            var config=save.World.Config;
            var buildings=config.Production.Buildings.Select(b=>b.Id=="cache"?b with {CanFeedAnts=true,Name=b.Name=="中转站"?"中转据点":b.Name}:b).ToImmutableArray();
            save=save with {Format=5,Protocol=18,World=save.World with {Config=config with {Production=config.Production with {Buildings=buildings}}}};
        }
        // save5/wire18 → save6/wire19：六步补值，全部在内存候选上完成，不回写原档。
        if(save.Format==5 && save.Protocol==18)save=UpgradeV5(save);
        if(legacy && save.Format==SavedGames.Format && save.Protocol==Protocol.Version) {
            // Upgrade only the exact built-in old goals. User-defined delivered metrics retain their meaning.
            var definitions=save.World.Config.Milestones.Select(d=> {
                var standard=MilestoneRule.Defaults.FirstOrDefault(n=>n.Id==d.Id && n.Metric=="first_admission_points");
                var description=d.Id=="deliver-50"?"累计送回 50 份食物":"解锁后 10 分钟内累计总交付达到 200";
                return standard is not null && d.Metric=="delivered" && d.Title==standard.Title && d.Description==description && d.Target==standard.Target && d.TimeLimitTicks==standard.TimeLimitTicks && d.Prerequisites.SequenceEqual(standard.Prerequisites)?standard:d;
            }).ToImmutableArray();
            var world=save.World with {Config=save.World.Config with {Milestones=definitions}};
            save=save with {World=new MilestoneRule().Apply(world)};
        }
        return save;
    }
    private static SavedGame UpgradeV5(SavedGame save) {
        var world=save.World;
        var tick=world.Tick;
        // ① 独立子种子：由旧档 RandomState 确定性派生，绝不读写主 RandomState。
        var (creatureSeed,nestSeed)=Simulation.DeriveSubseeds(world.RandomState);
        // ②③④ 地下巢穴默认布局、制度默认档位、BlockedCells/ObservedChambers。
        var nest=NestLayout.Default;
        var institution=InstitutionState.Initial(tick);
        // ⑤ EnemyView 七个标量字段补默认值（反序列化已有默认，仍显式写一次便于核查）。
        var objects=world.Objects;
        foreach(var enemy in objects.All<EnemyView>())
            objects=objects.Put(enemy with {
                CreatureKind="predator_beetle", CarcassFoodType="meat", BlockCells=0, HerdId=0,
                LastGrazeTick=-1000000, TargetChamberX=-1, TargetChamberY=-1 });
        // ⑥ ObservedBuildings / 旧工地 ConstructionItem=fiber / cache.CanFeedAnts 保持不动。
        var config=world.Config with {
            Institutions=new InstitutionConfig(), Creatures=new CreatureConfig(), NestLayout=new NestLayoutConfig() };
        world=world with {
            Config=config, Objects=objects,
            CreatureSeed=creatureSeed, NestSeed=nestSeed, Nest=nest, Institution=institution,
            BlockedCells=[], ObservedChambers=[NestLayout.CoreIndex] };
        return save with {Format=SavedGames.Format,Protocol=Protocol.Version,World=world};
    }
    // Rename the old serialized field before strict DTO deserialization, only for known legacy versions.
    public static string NormalizeLegacyJson(string json) {
        var node=JsonNode.Parse(json) as JsonObject??throw new InvalidDataException("Invalid save JSON.");
        var format=node["Format"]?.GetValue<int>();var protocol=node["Protocol"]?.GetValue<int>();
        if((format==1 && protocol==12) || (format==2 && protocol==13) || (format==3 && protocol==14)) {
            if(node["World"]?["Config"]?["Production"]?["Buildings"] is JsonArray buildings)
                foreach(var building in buildings.OfType<JsonObject>())if(building.Remove("FiberCost",out var cost))building["MaterialCost"]=cost;
        }
        return node.ToJsonString();
    }
    private static SavedGame UpgradeV1(SavedGame save) {
        if(save.Format!=1 || save.Protocol!=12)return save;
        var config=save.World.Config;
        if(config.Production.Enabled) {
            var values=new Dictionary<string,int>(StringComparer.Ordinal) {
                [FoodTypes.Fruit]=1,[FoodTypes.Meat]=1,[FoodTypes.Fiber]=0,[FoodTypes.Concentrate]=3,[FoodTypes.Meal]=4
            };
            var foods=config.Foods.Select(f=>values.TryGetValue(f.Id,out var value)?f with {Value=value}:f).ToImmutableArray();
            if(!foods.Any(f=>f.Id==FoodTypes.Balanced))foods=foods.Add(new(FoodTypes.Balanced,"均衡营养餐",24) {Value=10});
            if(!foods.Any(f=>f.Id==FoodTypes.Royal))foods=foods.Add(new(FoodTypes.Royal,"蚁王盛宴",60) {Value=28});
            var buildings=config.Production.Buildings.Select(b=>b.Id=="workshop"?b with {Name="厨房",RequiredNetworkLevel=2}:b).ToImmutableArray();
            var recipes=config.Production.Recipes.Select(r=>r.Id=="concentrate"?r with {RequiredNetworkLevel=2}:r.Id=="meal"?r with {RequiredNetworkLevel=3}:r).ToImmutableArray();
            foreach(var recipe in new ProductionConfig().Recipes.Where(r=>r.Id is "balanced" or "royal")) {
                if(recipes.Any(r=>r.Id==recipe.Id))continue;
                if(recipes.Any(r=>r.Code==recipe.Code))throw new InvalidDataException("Legacy custom recipe code conflicts with new recipes.");
                recipes=recipes.Add(recipe);
            }
            config=config with {Foods=foods,AntTypes=config.AntTypes.Select(a=>a.Id==AntSpeciesIds.Chef?a with {RequiredNetworkLevel=2}:a).ToImmutableArray(),
                Production=config.Production with {Buildings=buildings,Recipes=recipes}};
        }
        return save with {Format=2,Protocol=13,World=save.World with {Config=config}};
    }
}
