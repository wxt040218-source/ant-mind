using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AntMind.Contracts;
using AntMind.Runtime;
using AntMind.Transport;

namespace AntMind.Desktop;

public sealed class GameWindow : Window {
    private readonly GameSession session = new(ContentLoader.LoadDefault(), 42);
    private readonly CancellationTokenSource shutdown = new();
    private readonly TextBlock queenHealth = GameTheme.Text("蚁后 100/100", 11, GameTheme.Paper);
    private readonly TextBlock food = GameTheme.Text("—", 20, GameTheme.Paper);
    private readonly TextBlock population = GameTheme.Text("—", 20, GameTheme.Paper);
    private readonly TextBlock eggs = GameTheme.Text("—", 20, GameTheme.Paper);
    private readonly TextBlock brain = GameTheme.Text("—", 20, GameTheme.Paper);
    private readonly TextBlock agentLabel = GameTheme.Text("Agent 未连接", 12, GameTheme.Paper);
    private readonly Avalonia.Controls.Shapes.Ellipse agentLamp = new() { Width = 9, Height = 9,
        Fill = GameTheme.Brush("#928779"), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock territoryTitle = GameTheme.Text("蚁群领地",18,GameTheme.Paper);
    private readonly TextBlock zoomLabel = GameTheme.Text("100%",12,GameTheme.Paper);
    private readonly TextBlock stats = GameTheme.Text("正在读取蚁群…", 12, GameTheme.Paper);
    private readonly TextBlock selectedAnt = GameTheme.Text("点击地图上的蚂蚁或食物，查看独立对象记录。", 13);
    private readonly TextBlock decisions = GameTheme.Text("选择蚂蚁后，查看最近的决策记录。", 12);
    private readonly TextBlock productionInfo=GameTheme.Text("尚无生产设施",13);
    private readonly TextBlock operations = GameTheme.Text("等待操作。", 12);
    private readonly MilestonePanel milestonePanel = new();
    private readonly HashSet<string> completedMilestones = new(StringComparer.Ordinal);
    private readonly List<string> operationLog = [];
    private readonly Dictionary<string, int> observedVersions = new(StringComparer.Ordinal);
    private readonly Dictionary<(CommandKind Kind, int Value), Button> timeButtons = [];
    private readonly SpeciesMenu speciesMenu;
    private readonly GroupPanel groupPanel;
    private readonly WorldCanvas map = new();
    private readonly BuildingPanel buildingPanel=new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Task pipe;
    private bool refreshing;
    private DebugConsoleWindow? debugConsole;

    public GameWindow() {
        Title = "Ant Mind · 蚁群领地"; Width = 1400; Height = 930; MinWidth = 1100; MinHeight = 740;
        Background = GameTheme.Wood; Foreground = GameTheme.Ink;
        FontFamily = new FontFamily("Microsoft YaHei UI");
        speciesMenu = new SpeciesMenu(ExecuteMenuCommand);
        speciesMenu.ActionReported += Log;
        groupPanel=new GroupPanel();
        buildingPanel.PlacementRequested+=type=>{map.Placing=type;buildingPanel.SetPlacement(type);map.Focus();};
        map.PlacementCancelled+=()=>buildingPanel.SetPlacement(null);
        map.BuildRequested+=async (type,point)=>{
            var state=await session.ObserveAsync(true);
            var result=await session.ExecuteAsync(new(Guid.NewGuid(),state.Revision,CommandKind.PlanBuild,Reason:"desktop blueprint placement") {ExpectedWorldId=state.WorldId,Key=type,Location=point});
            Log(result.Accepted?"蓝图已放置，等待木材与建筑蚁施工":"放置失败："+result.Code);
            if(result.Accepted){map.Placing=null;buildingPanel.SetPlacement(null);}
        };
        buildingPanel.CancelRequested+=async id=>{
            var state=await session.ObserveAsync(true);var r=await session.ExecuteAsync(new(Guid.NewGuid(),state.Revision,CommandKind.CancelBuild,Value:id,Reason:"desktop cancel construction") {ExpectedWorldId=state.WorldId});
            Log(r.Accepted?"工程已取消，材料保留在原地":"取消失败："+r.Code);
        };
        var shell = new Grid { RowDefinitions = new("78,*,138"), RowSpacing = 12, Margin = new Thickness(14) };
        var header = new Grid { ColumnDefinitions = new("220,*"), ColumnSpacing = 20 };
        var brand = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        brand.Children.Add(GameTheme.Text("ANT MIND", 28, GameTheme.Paper));
        brand.Children.Add(queenHealth);
        header.Children.Add(brand);
        var resources = new Grid { ColumnDefinitions = new("*,*,*,*"), ColumnSpacing = 10 };
        var index = 0;
        foreach (var (label, value) in new[] { ("食物储备", food), ("蚁群人口", population), ("蚂蚁卵", eggs), ("大脑 / 科技", brain) }) {
            var stack = new StackPanel { Spacing = 2 };
            stack.Children.Add(GameTheme.Text(label, 11, GameTheme.Brush("#CEB991"))); stack.Children.Add(value);
            var frame = GameTheme.Frame(stack, GameTheme.Brush("#47352C"), 10);
            Grid.SetColumn(frame, index++); resources.Children.Add(frame);
        }
        Grid.SetColumn(resources, 1); header.Children.Add(resources); shell.Children.Add(header);
        var body = new Grid { ColumnDefinitions = new("*,390"), ColumnSpacing = 12 };
        var territory = new Grid { RowDefinitions = new("Auto,Auto,*,Auto"), RowSpacing = 10 };
        var mapHeader = new Grid { ColumnDefinitions = new("*,Auto") };
        mapHeader.Children.Add(territoryTitle);
        stats.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(stats, 1); mapHeader.Children.Add(stats);
        territory.Children.Add(mapHeader);
        var mapFrame = GameTheme.Frame(map, GameTheme.Brush("#435139"), 0);
        var cameraBar = new Grid { ColumnDefinitions=new("Auto,*"),ColumnSpacing=12 };
        var cameraButtons = new StackPanel { Orientation=Orientation.Horizontal,Spacing=5,VerticalAlignment=VerticalAlignment.Center };
        foreach(var (label,action) in new (string,Action)[] { ("−",()=>map.ZoomBy(1/1.25)),("+",()=>map.ZoomBy(1.25)),("全图",map.FitMap),("蚁后",map.FocusQueen) }) {
            var button=GameTheme.Button(label);button.Padding=new Thickness(9,4);button.MinHeight=28;button.FontSize=12;
            button.Click+=(_,_)=>action();cameraButtons.Children.Add(button);
        }
        zoomLabel.VerticalAlignment=VerticalAlignment.Center;cameraButtons.Children.Add(zoomLabel);
        foreach(var (label,kind) in new[] {("存档",CommandKind.SaveGame),("继续存档",CommandKind.LoadGame)}) {
            var b=GameTheme.Button(label);b.Padding=new Thickness(8,4);b.MinHeight=28;b.FontSize=12;
            b.Click+=async (_,_)=>{var r=await ExecuteMenuCommand(kind,AntSpeciesIds.Scout,0);Log(r.Accepted?r.Code:"存档操作失败："+r.Code);};cameraButtons.Children.Add(b);
        }
        cameraBar.Children.Add(cameraButtons);
        var cameraHint=GameTheme.Text("滚轮缩放 · 拖动地图 · 点击查看",11,GameTheme.Brush("#CEB991"));
        cameraHint.VerticalAlignment=VerticalAlignment.Center;cameraHint.HorizontalAlignment=HorizontalAlignment.Right;
        Grid.SetColumn(cameraHint,1);cameraBar.Children.Add(cameraHint);Grid.SetRow(cameraBar,1);territory.Children.Add(cameraBar);
        map.ViewportChanged+=()=>zoomLabel.Text=$"{map.ZoomPercent:0}%";
        Grid.SetRow(mapFrame, 2); territory.Children.Add(mapFrame);
        var toolbar = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
        var time = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        foreach (var (text, kind, value) in new[] { ("Ⅱ 暂停", CommandKind.Pause, 0), ("▶ 运行", CommandKind.Resume, 0),
            ("单步", CommandKind.Step, 0), ("1×", CommandKind.Speed, 1), ("2×", CommandKind.Speed, 2), ("5×", CommandKind.Speed, 5) }) {
            var button = GameTheme.Button(text); button.Padding = new Thickness(10, 7); button.MinHeight = 34;
            button.Click += async (_, _) => await ExecuteControl(kind, value);
            timeButtons.Add((kind, value), button); time.Children.Add(button);
        }
        var consoleButton=GameTheme.Button("控制台 F12");consoleButton.Padding=new Thickness(8,7);consoleButton.MinHeight=34;consoleButton.FontSize=12;
        consoleButton.Click+=(_,_)=>OpenConsole();time.Children.Add(consoleButton);
        KeyDown+=(_,e)=>{if(e.Key==Avalonia.Input.Key.F12){OpenConsole();e.Handled=true;}};
        toolbar.Children.Add(time);
        var connection = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        connection.Children.Add(agentLamp); connection.Children.Add(agentLabel);
        Grid.SetColumn(connection, 1); toolbar.Children.Add(connection); Grid.SetRow(toolbar, 3); territory.Children.Add(toolbar);
        body.Children.Add(GameTheme.Frame(territory, GameTheme.Brush("#41392C"), 12));
        var management=new TabControl { Background=GameTheme.Paper,Foreground=GameTheme.Ink,FontSize=14 };
        management.Items.Add(new TabItem { Header=GameTheme.Text("蚁种与科技",14),Content=speciesMenu });
        management.Items.Add(new TabItem { Header=GameTheme.Text("建筑",13),Content=buildingPanel });
        management.Items.Add(new TabItem { Header=GameTheme.Text("小组与任务",13),Content=groupPanel });
        management.Items.Add(new TabItem {Header=GameTheme.Text("生产与收益",14),Content=new ScrollViewer {Content=productionInfo,Margin=new Thickness(16)}});
        Grid.SetColumn(management, 1); body.Children.Add(management); Grid.SetRow(body, 1); shell.Children.Add(body);
        var journal = new TabControl { Foreground = GameTheme.Ink, Background = GameTheme.Paper };
        foreach (var (label, content) in new[] { ("个体观察", selectedAnt), ("决策记录", decisions), ("操作记录", operations) })
            journal.Items.Add(new TabItem { Header = label, Content = new ScrollViewer { Content = content, Margin = new Thickness(4, 8) } });
        journal.Items.Add(new TabItem { Header = "挑战目标", Content = milestonePanel });
        journal.SelectedIndex = 3;
        var footer = GameTheme.Frame(journal, padding: 10); Grid.SetRow(footer, 2); shell.Children.Add(footer);
        Content = shell;
        map.AntSelected += id => { map.SelectedAntId = id; UpdateSelectedAnt(); map.InvalidateVisual(); };
        map.ObjectSelected+=()=>{UpdateSelectedAnt();map.InvalidateVisual();};
        map.FoodSelected += _ => { UpdateSelectedAnt(); map.InvalidateVisual(); };
        pipe = new PipeServer(Environment.GetEnvironmentVariable("ANTMIND_PIPE") ?? "antmind-v1",
            (r, ct) => Gateway.DispatchAsync(session, r, ct)).RunAsync(shutdown.Token);
        timer.Tick += async (_, _) => {
            if (refreshing) return;
            refreshing = true;
            try {
                var snapshot = await session.ObserveAsync(true);
                queenHealth.Text = $"蚁后 {snapshot.Queen?.Health}/{snapshot.Queen?.MaxHealth} · 击败 {snapshot.EnemiesDefeated}";
                queenHealth.Foreground = snapshot.Queen is { Health:<30 } ? GameTheme.Rust : GameTheme.Paper;
                food.Text = $"{snapshot.Inventory} 份 · {snapshot.DevelopmentValue} 点";
                productionInfo.Text=ProductionText(snapshot);
                ToolTip.SetTip(food,$"浓缩 {snapshot.Stock.GetValueOrDefault(FoodTypes.Concentrate)} · 蛋白餐 {snapshot.Stock.GetValueOrDefault(FoodTypes.Meal)} · 木材 {snapshot.Stock.GetValueOrDefault(FoodTypes.Wood)} · 食物价值 {snapshot.StoredFoodValue} · 支付余额 {snapshot.DevelopmentChange} · 总量 {snapshot.Inventory} · 已食用 {snapshot.FoodEaten} · 已恢复生命 {snapshot.HealthRestored}"); population.Text = $"{snapshot.Ants.Length} / {snapshot.PopulationCap}";
                eggs.Text = snapshot.Eggs.ToString(); brain.Text = $"Lv.{snapshot.BrainLevel} / 科技 {snapshot.TechnologyLevel}";
                stats.Text = $"新食物 {snapshot.Economy?.PointsPerMinute:0.0}/分 · T {snapshot.Tick} · {snapshot.Speed}× · {(snapshot.Defeated ? "蚁后倒下 · 本局结束" : snapshot.Paused ? "已暂停" : "运行中")}";
                territoryTitle.Text=$"蚁群领地  /  {snapshot.Width} × {snapshot.Height}";
                map.View = snapshot; map.InvalidateVisual(); speciesMenu.Update(snapshot); groupPanel.Update(snapshot);buildingPanel.Update(snapshot);
                agentLamp.Fill = snapshot.AgentConnected ? GameTheme.Brush("#A6C45A") : GameTheme.Brush("#928779");
                agentLabel.Text = snapshot.AgentConnected ? $"Agent 已连接 · {snapshot.ConnectedAgents.Length}" : "Agent 未连接";
                ToolTip.SetTip(connection, snapshot.AgentConnected ? string.Join("、", snapshot.ConnectedAgents) : "等待 MCP 连接；断开后最迟 6 秒恢复灰色");
                foreach (var (key, button) in timeButtons) {
                    var active = key.Kind == CommandKind.Pause && snapshot.Paused || key.Kind == CommandKind.Resume && !snapshot.Paused ||
                        key.Kind == CommandKind.Speed && key.Value == snapshot.Speed;
                    button.Background = active ? GameTheme.Rust : GameTheme.Card;
                    button.Foreground = active ? GameTheme.Paper : GameTheme.Ink;
                    button.IsEnabled = !snapshot.Defeated && (key.Kind != CommandKind.Step || snapshot.Paused);
                }
                foreach (var program in snapshot.Programs.Where(p=>p.GroupId==0)) {
                    if (observedVersions.TryGetValue(program.Species, out var old) && old != program.ActiveVersion)
                        Log($"{snapshot.Species.Single(x => x.Id == program.Species).Name}程序已更新为 v{program.ActiveVersion}");
                    observedVersions[program.Species] = program.ActiveVersion;
                }
                milestonePanel.Update(snapshot);
                foreach (var milestone in snapshot.Milestones.Where(x=>x.Status=="completed"))
                    if (completedMilestones.Add(milestone.Id)) Log($"里程碑达成：{milestone.Title} · T{milestone.CompletedTick}");
                UpdateSelectedAnt();
            } finally { refreshing = false; }
        };
        timer.Start();
        Closed += async (_, _) => { debugConsole?.Close();timer.Stop(); shutdown.Cancel(); await pipe; await session.DisposeAsync(); shutdown.Dispose(); };
    }

    private void OpenConsole() {
        if(debugConsole is not null){debugConsole.Activate();return;}
        debugConsole=new DebugConsoleWindow((r,ct)=>Gateway.DispatchAsync(session,r,ct),shutdown.Token);
        debugConsole.Closed+=(_,_)=>debugConsole=null;debugConsole.Show(this);
    }
    private static string ItemName(string type)=>type switch {FoodTypes.Fruit=>"水果",FoodTypes.Meat=>"肉",FoodTypes.Fiber=>"纤维",FoodTypes.Wood=>"木材",FoodTypes.Concentrate=>"水果浓缩",FoodTypes.Meal=>"蛋白餐",FoodTypes.Balanced=>"均衡营养餐",FoodTypes.Royal=>"蚁王盛宴",_=>type};
    private static string BuildingName(string type)=>type switch {"cache"=>"中转据点","workshop"=>"厨房",_=>type};
    private static string PhaseName(string phase)=>phase switch {"blueprint"=>"施工中","ready"=>"已建成","cancelled"=>"已取消",_=>phase};
    private static string Items(IEnumerable<KeyValuePair<string,long>> items)=>string.Join(" · ",items.Where(x=>x.Value>0).OrderBy(x=>x.Key,StringComparer.Ordinal).Select(x=>$"{ItemName(x.Key)} {x.Value}"));
    private static string RecipeText(RecipeDefinition r,Snapshot s)=>$"{(s.TechnologyLevel>=r.RequiredNetworkLevel?"已解锁":"未解锁")} · Lv.{r.RequiredNetworkLevel} {r.Name}\n"+string.Join(" + ",r.Ingredients().OrderBy(x=>x.Key,StringComparer.Ordinal).Select(i=>$"{i.Value} {ItemName(i.Key)}"))+$" → {r.OutputCount} {ItemName(r.Output)}\n价值 {r.Ingredients().Sum(i=>(long)s.FoodTypes.Single(f=>f.Id==i.Key).Value*i.Value)} → {s.FoodTypes.Single(f=>f.Id==r.Output).Value*r.OutputCount} · 回血 {s.FoodTypes.Single(f=>f.Id==r.Output).HealAmount} · 工时 {r.WorkTicks}";
    private static string ProductionText(Snapshot s)=>$"科技 Lv.{s.TechnologyLevel} · {s.TechnologyEra}\n发展储备 {s.DevelopmentValue} 价值点（余额 {s.DevelopmentChange}）\n新食物累计 {s.FirstAdmissionPoints} · 加工增值 {s.ProcessingValueAdded}\n近 {s.Economy?.WindowTicks/20.0:0.0} 秒：新食物 {s.Economy?.PointsPerMinute:0.0}/分 · 加工增值 {s.Economy?.ValueAddedPerMinute:0.0}/分\n\n库存：{Items(s.Stock)}\n\n可见源头 {s.Sources.Length}\n"+string.Join("\n",s.Sources.Select(x=>$"#{x.Id} {ItemName(x.ItemType)} {x.Amount}/{x.Capacity} · T{x.LastObservedTick}"))+"\n\n设施\n"+string.Join("\n",s.Buildings.Select(b=>$"#{b.Id} {BuildingName(b.Type)} · {PhaseName(b.Phase)} · {(s.TechnologyLevel<s.BuildingTypes.Single(d=>d.Id==b.Type).RequiredNetworkLevel?"科技未达标":"科技已开放")} · 施工 {b.ConstructionWork}\n输入 {Items(b.Store.Amounts)} / 输出 {Items(b.Output.Amounts)}\n{b.Recipe} · 工作 {b.RecipeWork}"))+"\n\n配方\n"+string.Join("\n",s.Recipes.Select(r=>RecipeText(r,s)))+"\n\nAgent 规划地点和物流；蚂蚁程序显式采集、施工、加工与转运。";

    private void Log(string message) {
        operationLog.Insert(0, $"T{map.View?.Tick ?? 0}  {message}");
        if (operationLog.Count > 50) operationLog.RemoveAt(operationLog.Count - 1);
        operations.Text = string.Join("\n", operationLog);
    }
    private async Task ExecuteControl(CommandKind kind, int value) {
        var result = await ExecuteMenuCommand(kind, AntSpeciesIds.Scout, value);
        Log(result.Accepted ? kind switch {
            CommandKind.Pause => "世界已暂停", CommandKind.Resume => "世界继续运行", CommandKind.Step => "推进一个模拟步",
            CommandKind.Speed => $"时间速度调整为 {value}×",
            _ => "操作完成"
        } : $"操作未完成：{result.Code}");
    }
    private async Task<CommandResult> ExecuteMenuCommand(CommandKind kind, string species, int value) {
        var state = await session.ObserveAsync(true);
        return await session.ExecuteAsync(new(Guid.NewGuid(), state.Revision, kind, Value: value, Reason: "desktop menu", Species: species) {ExpectedWorldId=state.WorldId,Key=kind is CommandKind.SaveGame or CommandKind.LoadGame?"quick":""});
    }
    private void UpdateSelectedAnt() {
        if (map.View is not { } view) return;
        if(map.SelectedObjectKind=="source") {var src=view.Sources.FirstOrDefault(x=>x.Id==map.SelectedObjectId);selectedAnt.Text=src is null?"未知源头":$"资源源头 #{src.Id} · {ItemName(src.ItemType)} · {src.Amount}/{src.Capacity}\n生长周期 {src.GrowthTicks} Tick（0 表示不再生） · 最后观察 T{src.LastObservedTick}";return;}
        if(map.SelectedObjectKind=="building") {var b=view.Buildings.FirstOrDefault(x=>x.Id==map.SelectedObjectId);selectedAnt.Text=b is null?"未知设施":$"{BuildingName(b.Type)} #{b.Id} · {PhaseName(b.Phase)} · 建造 {b.ConstructionWork}/{view.BuildingTypes.Single(d=>d.Id==b.Type).BuildTicks} · 配方 {b.Recipe} / {b.RecipeWork}\n建材 {(b.ConstructionItem.Length>0?ItemName(b.ConstructionItem):ItemName(view.BuildingTypes.Single(d=>d.Id==b.Type).MaterialType))} · 输入 {Items(b.Store.Amounts)} · 输出 {Items(b.Output.Amounts)} · 在制 {Items(b.WorkInProgress.Amounts)} · 最后观察 T{b.LastObservedTick}";return;}
        if (map.SelectedFoodId is { } foodId) {
            var foodRecord = view.Resources.FirstOrDefault(x=>x.Id==foodId);
            if (foodRecord is null) { selectedAnt.Text = "无法读取该对象"; return; }
            selectedAnt.Text = $"{(view.FoodTypes.FirstOrDefault(f=>f.Id==foodRecord.FoodType)?.Name??foodRecord.FoodType)} #{foodRecord.Id}　·　当前 {foodRecord.Amount} / 最大容量 {foodRecord.MaxCapacity}　·　已发现\n" +
                $"位置 {foodRecord.Position}　·　首次发现 T{foodRecord.DiscoveredTick}　·　最近观察 T{foodRecord.LastSeenTick}";
            decisions.Text = "食物记录保留最大容量、当前观察数量、发现状态与独立扩展属性。\n视野外数量可能过期，再次观察时更新。";
            return;
        }
        if (map.SelectedAntId is not { } id) return;
        var ant = view.Ants.FirstOrDefault(x => x.Id == id);
        if (ant is null) { selectedAnt.Text = "所选蚂蚁已不存在"; return; }
        var definition = view.Species.Single(x => x.Id == ant.Species);
        var memory = ant.Memory.IsDefaultOrEmpty ? "空" : string.Join(", ", ant.Memory.Select((value, index) => (value, index)).Where(x => x.value != 0).Select(x => $"{x.index}:{x.value}"));
        var next=ant.Level<3?$"{ant.Experience}/{view.AntLevels[ant.Level].RequiredExperience}":"已满级";
        selectedAnt.Text = $"{definition.Name} #{ant.Id}　·　Lv.{ant.Level} · 经验 {next} · 有效工作 {ant.CompletedWork}　·　生命 {ant.Health}/{ant.MaxHealth}　·　携带 {ant.Carry}/{definition.CarryCapacity} {ant.CarryFood}　·　程序 v{ant.ProgramVersion}　·　蚁后{(ant.QueenConnected ? "范围内" : "范围外")}\n" +
            $"最近经验 {ant.LastExperienceWork} @T{ant.LastExperienceTick} · 小组 {view.Groups.FirstOrDefault(g=>g.Id==ant.GroupId)?.Name??"未分组"} · 任务缓存 v{ant.ReceivedOrder?.Version??0} · {(ant.HealUntilTick>0?$"进食中，还需 {Math.Max(0,ant.HealUntilTick-view.Tick)} Tick":"")}\n" +
            $"目标 {ant.Target?.ToString() ?? "无"}　·　动作 {ant.ActionStatus}/{ant.ActionResult}　·　指令 {ant.LastInstructionSpent}　·　分支 {ant.LastDecisionBranch}\n" +
            $"信息读取 {ant.QueenRequestStatus} / 上报 {ant.QueenReportStatus}　·　缓存 T{ant.QueenInfoTick}　·　记忆 {memory}　·　错误 {ant.LastError ?? "无"}";
        decisions.Text = ant.RecentDecisions.IsDefaultOrEmpty ? "暂无决策记录" : string.Join("\n", ant.RecentDecisions.Reverse()
            .Select(x => $"T{x.Tick}  v{x.ProgramVersion}  分支 {x.Branch}  指令 {x.Spent}  {x.Error ?? ""}"));
    }
}
