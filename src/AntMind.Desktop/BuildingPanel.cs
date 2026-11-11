using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using AntMind.Contracts;
namespace AntMind.Desktop;

// UI chooses only a public plan. Agent still assigns all transport and worker programs.
internal sealed class BuildingPanel : ScrollViewer {
    private readonly StackPanel cards=new() {Spacing=12,Margin=new Thickness(14)};
    private readonly TextBlock hint=GameTheme.Text("选择建筑，再点击地图放置蓝图。木材必须运到工地，由建筑蚁施工。",12,GameTheme.Muted);
    private readonly StackPanel sites=new() {Spacing=8};
    private readonly Dictionary<string,Button> buttons=new(StringComparer.Ordinal);
    private readonly Dictionary<string,TextBlock> labels=new(StringComparer.Ordinal);
    private readonly TextBlock stock=GameTheme.Text();
    private string lastSites="";
    public event Action<string?>? PlacementRequested;
    public event Action<int>? CancelRequested;
    public BuildingPanel() {
        Content=cards;cards.Children.Add(GameTheme.Text("建设领地",23));cards.Children.Add(stock);cards.Children.Add(hint);
        var stop=GameTheme.Button("取消放置");stop.Click+=(_,_)=>PlacementRequested?.Invoke(null);cards.Children.Add(stop);
    }
    public void Update(Snapshot s) {
        stock.Text=$"巢内木材 {s.Stock.GetValueOrDefault(FoodTypes.Wood)} · 建筑蚁 {s.Ants.Count(a=>a.Species==AntSpeciesIds.Builder)} 只";
        foreach(var def in s.BuildingTypes) {
            if(!buttons.TryGetValue(def.Id,out var button)) {
                var details=new StackPanel {Spacing=6};details.Children.Add(new RolePortrait("world:"+def.Id,92) {HorizontalAlignment=HorizontalAlignment.Left});
                var label=GameTheme.Text();labels.Add(def.Id,label);details.Children.Add(label);
                button=GameTheme.Button("放置蓝图",true);var id=def.Id;button.Click+=(_,_)=>PlacementRequested?.Invoke(id);
                buttons.Add(def.Id,button);details.Children.Add(button);cards.Children.Insert(cards.Children.Count-1,GameTheme.Frame(details,padding:12));
            }
            var name=s.FoodTypes.FirstOrDefault(x=>x.Id==def.MaterialType)?.Name??def.MaterialType;
            labels[def.Id].Text=$"{def.Name} · 科技 Lv.{def.RequiredNetworkLevel}\n材料 {def.MaterialCost} {name} · 工时 {def.BuildTicks}\n容量 {def.Capacity} · 需要建筑蚁"+(def.CanFeedAnts?"\n前线仓储与食物补给 · 附近蚂蚁可进食回血":"");
            button.IsEnabled=!s.Defeated && s.TechnologyLevel>=def.RequiredNetworkLevel;
            button.Content=s.TechnologyLevel>=def.RequiredNetworkLevel?"在地图放置蓝图":$"科技 Lv.{def.RequiredNetworkLevel} 解锁";
        }
        if(sites.Parent is null)cards.Children.Add(sites);
        var key=s.WorldId+string.Join(";",s.Buildings.Select(b=>$"{b.Id}:{b.Phase}:{b.ConstructionWork}:{string.Join(',',b.Store.Amounts.OrderBy(x=>x.Key))}:{b.Recipe}:{b.RecipeWork}:{b.Output.Total}:{s.Ants.Count(a=>a.HealUntilTick>0 && a.EatHolderId==b.Id)}"));
        if(lastSites==key)return;lastSites=key;sites.Children.Clear();
        foreach(var b in s.Buildings) {
            var def=s.BuildingTypes.Single(d=>d.Id==b.Type);var item=b.ConstructionItem.Length>0?b.ConstructionItem:def.MaterialType;
            var text=GameTheme.Text($"#{b.Id} {def.Name} · {b.Phase switch {"blueprint"=>"建设中","ready"=>"已建成",_=>"已取消"}}\n位置 ({b.Position.X/1000.0:0.#}, {b.Position.Y/1000.0:0.#})\n"+
                (b.Phase=="blueprint"?$"材料 {b.Store.Get(item)}/{def.MaterialCost} · 施工 {b.ConstructionWork}/{def.BuildTicks}":$"输入 {b.Store.Total} · 在制 {b.WorkInProgress.Total} · 输出 {b.Output.Total}"),12);
            var row=new StackPanel {Spacing=6};row.Children.Add(text);
            if(def.CanFeedAnts && b.Phase=="ready") {
                var meals=s.FoodTypes.Where(f=>f.Edible && b.Store.Get(f.Id)>0).Select(f=>$"{f.Name} {b.Store.Get(f.Id)}");
                row.Children.Add(GameTheme.Text($"补给食物：{string.Join(" · ",meals.DefaultIfEmpty("暂无，请安排运输"))}\n进食中 {s.Ants.Count(a=>a.HealUntilTick>0 && a.EatHolderId==b.Id)} 只 · 每次消耗1份，受击中断",12,GameTheme.Muted));
            }
            if(b.Phase=="blueprint" || b.Recipe.Length>0) {var cancel=GameTheme.Button(b.Phase=="blueprint"?"取消工程 · 留下材料":"取消加工 · 退回原料");var id=b.Id;cancel.Click+=(_,_)=>CancelRequested?.Invoke(id);row.Children.Add(cancel);}
            sites.Children.Add(GameTheme.Frame(row,padding:10));
        }
    }
    public void SetPlacement(string? type)=>hint.Text=type is null?"选择建筑，再点击地图放置蓝图。Agent 负责物流与建筑蚁分工。":"放置中：左键确认 · 右键或 Esc 退出 · 仍可拖动和缩放地图";
}
