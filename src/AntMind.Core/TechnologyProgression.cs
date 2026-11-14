using System.Collections.Immutable;
using AntMind.Contracts;
namespace AntMind.Core;

// Technology is a projection of the existing network level, never a second progression counter.
public static class TechnologyProgression {
    public static string Era(WorldConfig config,int level)=>config.TechnologyEras.FirstOrDefault(x=>x.Level==level)?.Name??$"科技 {level} 级";
    public static ImmutableArray<TechnologySnapshot> Unlocks(WorldState world) {
        var list=ImmutableArray.CreateBuilder<TechnologySnapshot>();
        void Add(string id,string name,int required,string category,string content)=>list.Add(new(id,name,required,world.NetworkLevel>=required) {Category=category,ContentId=content});
        foreach(var t in world.Config.Technologies)Add(t.Id,t.Name,t.RequiredNetworkLevel,"ability",t.Id);
        foreach(var t in world.Config.AntTypes)Add("species."+t.Id,t.Name,t.RequiredNetworkLevel,"species",t.Id);
        if(world.Config.Production.Enabled) {
            foreach(var b in world.Config.Production.Buildings)Add("building."+b.Id,b.Name,b.RequiredNetworkLevel,"building",b.Id);
            foreach(var r in world.Config.Production.Recipes)Add("recipe."+r.Id,r.Name,r.RequiredNetworkLevel,"recipe",r.Id);
        }
        return list.OrderBy(x=>x.RequiredNetworkLevel).ThenBy(x=>x.Id,StringComparer.Ordinal).ToImmutableArray();
    }
    public static WorldState Pay(WorldState world,long cost) {
        if(cost<0 || world.DevelopmentValue-cost<InstitutionMechanics.EffectiveReserve(world))throw new InvalidOperationException("Insufficient development value.");
        var payment=world.Stock.PayValue(cost,world.DevelopmentChange,world.Config.Foods);
        var result=world with {Stock=payment.Store,DevelopmentChange=payment.Change};
        foreach(var item in world.Stock.Amounts) {var consumed=item.Value-result.Stock.Get(item.Key);if(consumed>0)result=Economy.Consumed(result,item.Key,consumed);}
        return result;
    }
}
