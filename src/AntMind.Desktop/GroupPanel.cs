using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using AntMind.Contracts;
namespace AntMind.Desktop;

// Planning belongs to the Agent; this panel observes its groups without prescribing a workflow.
public sealed class GroupPanel : Border {
    private readonly ComboBox groups=new() { HorizontalAlignment=HorizontalAlignment.Stretch };
    private readonly TextBlock detail=GameTheme.Text("暂无小组，请让 Agent 创建并分配成员。",13);
    private readonly TextBlock members=GameTheme.Text("",13);
    private readonly TextBlock parameters=GameTheme.Text("",12,GameTheme.Muted);
    private Snapshot? view;
    private int selectedId;
    private string signature="";
    private bool updating;
    public GroupPanel() {
        Padding=new Thickness(14);Background=GameTheme.Paper;
        var page=new StackPanel { Spacing=14 };
        page.Children.Add(GameTheme.Text("Agent 小组",22));
        page.Children.Add(GameTheme.Text("同类蚂蚁共享代码，小组提供不同的任务数据。",12,GameTheme.Muted));
        page.Children.Add(groups);page.Children.Add(detail);
        page.Children.Add(GameTheme.Text("成员",17));page.Children.Add(members);
        page.Children.Add(GameTheme.Text("Agent 参数与地点",17));page.Children.Add(parameters);
        page.Children.Add(GameTheme.Text("在聊天中让 Agent 创建小组、分配成员、设置参数和地点。任务由蚁种代码解释，更新需显式读取蚁后信息。\n蚁种程序仍在「蚁种与科技」中查看。",12,GameTheme.Muted));
        Child=new ScrollViewer { Content=page,HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        groups.SelectionChanged+=(_,_)=> {
            if(updating || view is null)return;
            selectedId=groups.SelectedIndex>=0 && groups.SelectedIndex<view.Groups.Length?view.Groups[groups.SelectedIndex].Id:0;Render();
        };
    }
    public void Update(Snapshot snapshot) {
        view=snapshot;
        var next=string.Join('|',view.Groups.Select(g=>$"{g.Id}:{g.Name}:{g.Version}"));
        if(signature!=next) {
            signature=next;updating=true;
            groups.ItemsSource=view.Groups.Select(g=>$"{g.Name} · #{g.Id}").ToArray();
            if(!view.Groups.Any(g=>g.Id==selectedId))selectedId=view.Groups.FirstOrDefault()?.Id??0;
            groups.SelectedIndex=view.Groups.ToList().FindIndex(g=>g.Id==selectedId);updating=false;
        }
        Render();
    }
    private void Render() {
        if(view is null)return;
        var group=view.Groups.FirstOrDefault(g=>g.Id==selectedId);
        if(group is null) {detail.Text="暂无小组，请让 Agent 创建并分配成员。";members.Text="";parameters.Text="";return;}
        var food=group.FoodFilter.Length==0?"不限":view.FoodTypes.FirstOrDefault(f=>f.Id==group.FoodFilter)?.Name??group.FoodFilter;
        detail.Text=$"{group.Name} · 任务 v{group.Version}\n{group.Task}\n任务标记 {group.Activity} · 食物筛选 {food}\n作业点 {group.Objective.X/1000},{group.Objective.Y/1000} · 范围 {group.RadiusCells} 格";
        var ants=view.Ants.Where(a=>a.GroupId==group.Id).ToArray();
        members.Text=ants.Length==0?"暂无成员":string.Join("\n",ants.Select(a=>$"{view.Species.Single(s=>s.Id==a.Species).Name} #{a.Id} · 生命 {a.Health}/{a.MaxHealth} · 任务缓存 v{a.ReceivedOrder?.Version??0}"));
        var rows=group.Parameters.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>$"{p.Key} = {p.Value}")
            .Concat(group.Points.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>$"{p.Key} = ({p.Value.X/1000},{p.Value.Y/1000})"));
        parameters.Text=rows.Any()?string.Join("\n",rows):"尚无自定义参数或命名地点。";
    }
}
