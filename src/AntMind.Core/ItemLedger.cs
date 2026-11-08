using System.Collections.Immutable;
using AntMind.Contracts;
namespace AntMind.Core;

// A read-only ledger over the canonical holders. It never maintains a second inventory.
public static class ItemLedger {
    public static ImmutableDictionary<string,long> Held(WorldState w) {
        var sums=new Dictionary<string,long>(StringComparer.Ordinal);
        void Add(string type,long n)=>sums[type]=checked(sums.GetValueOrDefault(type)+n);
        void Store(FoodStore s) {foreach(var item in s.Amounts)Add(item.Key,item.Value);}
        Store(w.Stock);
        foreach(var a in w.Ants)Add(a.CarryFood,a.Carry);
        foreach(var f in w.Resources)Add(f.FoodType,f.Amount);
        foreach(var source in w.Objects.All<SourceView>())Add(source.ItemType,source.Amount);
        foreach(var b in w.Objects.All<BuildingView>()){Store(b.Store);Store(b.Output);Store(b.WorkInProgress);}
        return sums.ToImmutableDictionary(StringComparer.Ordinal);
    }
    public static ImmutableDictionary<string,long> Imbalance(WorldState w) {
        var held=Held(w);
        return w.Config.Foods.Select(x=>x.Id).ToImmutableDictionary(id=>id,id=>
            checked(w.CreatedItems.GetValueOrDefault(id)-w.ConsumedItems.GetValueOrDefault(id)-held.GetValueOrDefault(id)),StringComparer.Ordinal);
    }
}
