using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AntMind.Contracts;

namespace AntMind.Desktop;

// All management commands are still serialized by Runtime; navigation is local UI state.
public sealed class SpeciesMenu : Border {
    private readonly Func<CommandKind, string, int, Task<CommandResult>> execute;
    private readonly StackPanel page = new() { Spacing = 14 };
    private readonly Grid tabs = new() { ColumnDefinitions = new("*,*,*,*,*,*"), ColumnSpacing = 6 };
    private readonly Dictionary<string, Button> roleTabs = new(StringComparer.Ordinal);
    private readonly TextBlock title = GameTheme.Text("", 24);
    private readonly TextBlock subtitle = GameTheme.Text("", 13, GameTheme.Muted);
    private readonly TextBlock attributes = GameTheme.Text();
    private readonly TextBlock description = GameTheme.Text("", 13, GameTheme.Muted);
    private readonly TextBlock feedback = GameTheme.Text("", 13, GameTheme.Rust);
    private readonly TextBlock carryInfo = GameTheme.Text("", 19);
    private readonly TextBlock brainInfo = GameTheme.Text("", 15);
    private readonly TextBlock carryHint = GameTheme.Text("", 12, GameTheme.Muted);
    private readonly TextBlock brainHint = GameTheme.Text("", 12, GameTheme.Muted);
    private readonly TextBlock recruitHint = GameTheme.Text("", 12, GameTheme.Muted);
    private readonly TextBox source = new() { IsReadOnly = true, AcceptsReturn = true,
        TextWrapping = TextWrapping.NoWrap, Height = 330, FontFamily = new FontFamily("Consolas"),
        Background = GameTheme.Card, Foreground = GameTheme.Ink, FontSize = 12 };
    private readonly Button upgrade = GameTheme.Button("升级大脑");
    private readonly Button carryUpgrade = GameTheme.Button("升级载量", true);
    private readonly Button recruit = GameTheme.Button("孵化蚂蚁");
    private readonly Button produce = GameTheme.Button("生产蚂蚁卵", true);
    private readonly Button networkUpgrade = GameTheme.Button("升级蚁群网络", true);
    private readonly TextBlock networkInfo = GameTheme.Text("", 17);
    private readonly TextBlock networkHint = GameTheme.Text("", 12, GameTheme.Muted);
    private readonly TextBlock technologyInfo = GameTheme.Text("", 12, GameTheme.Muted);
    private Snapshot? snapshot;
    private string selectedRole = AntSpeciesIds.Hauler;
    private bool showingCode;
    private bool busy;
    public event Action<string>? ActionReported;

    public SpeciesMenu(Func<CommandKind, string, int, Task<CommandResult>> execute) {
        this.execute = execute;
        Padding = new Thickness(14); Background = GameTheme.Paper;
        BorderBrush = GameTheme.Line; BorderThickness = new Thickness(2); CornerRadius = new CornerRadius(3);
        var layout = new Grid { RowDefinitions = new("Auto,*"), RowSpacing = 16 };
        foreach (var (id, name) in new[] { ("queen", "蚁后"), (AntSpeciesIds.Scout, "侦察蚁"), (AntSpeciesIds.Hauler, "搬运蚁"), (AntSpeciesIds.Soldier, "兵蚁"), (AntSpeciesIds.Chef,"厨师蚁"), (AntSpeciesIds.Builder,"建筑蚁") }) {
            var label = new StackPanel { Spacing = 3, HorizontalAlignment = HorizontalAlignment.Center };
            label.Children.Add(new RolePortrait(id, 36));
            var tabName=GameTheme.Text(name,11);tabName.TextWrapping=TextWrapping.NoWrap;label.Children.Add(tabName);
            var button = GameTheme.Button(name); button.Content = label; button.Padding = new Thickness(3);button.Height=72;
            button.Click += (_, _) => { if (busy) return; selectedRole = id; showingCode = false; feedback.Text = ""; BuildPage(); };
            Grid.SetColumn(button, roleTabs.Count); tabs.Children.Add(button); roleTabs.Add(id, button);
        }
        layout.Children.Add(tabs);
        var scroll = new ScrollViewer { Content = page, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); layout.Children.Add(scroll); Child = layout;
        carryUpgrade.Click += async (_, _) => await Act(CommandKind.UpgradeCarry);
        upgrade.Click += async (_, _) => await Act(CommandKind.UpgradeBrain);
        recruit.Click += async (_, _) => await Act(CommandKind.HatchEgg);
        produce.Click += async (_, _) => await Act(CommandKind.ProduceEgg);
        networkUpgrade.Click += async (_, _) => await Act(CommandKind.UpgradeNetwork);
        BuildPage();
    }

    private void BuildPage() {
        foreach (var (id, button) in roleTabs) {
            button.Background = id == selectedRole ? GameTheme.Brush("#E4C397") : GameTheme.Card;
            button.BorderBrush = id == selectedRole ? GameTheme.Rust : GameTheme.Line;
            button.BorderThickness = new Thickness(id == selectedRole ? 2 : 1);
        }
        foreach (var old in page.Children.ToArray()) ReleaseLayout(old);
        page.Children.Clear();
        if (showingCode) {
            var back = GameTheme.Button("‹ 返回升级面板");
            back.Click += (_, _) => { showingCode = false; BuildPage(); };
            page.Children.Add(back); page.Children.Add(title); page.Children.Add(description); page.Children.Add(source);
            page.Children.Add(GameTheme.Text("只读程序 · 通过 Agent 提交修改与回滚", 12, GameTheme.Muted));
        } else {
            var hero = new Grid { ColumnDefinitions = new("112,*"), ColumnSpacing = 10 };
            hero.Children.Add(new RolePortrait(selectedRole, 108));
            var labels = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            labels.Children.Add(title); labels.Children.Add(subtitle); Grid.SetColumn(labels, 1); hero.Children.Add(labels);
            page.Children.Add(hero);
            page.Children.Add(GameTheme.Frame(attributes, GameTheme.Card, 10));
            if (selectedRole == "queen") {
                page.Children.Add(Section("蚂蚁卵生产", description, produce, recruitHint));
                page.Children.Add(Section("虫群网络 · 科技等级", networkInfo, networkUpgrade, networkHint, technologyInfo));
            } else {
                if (selectedRole == AntSpeciesIds.Hauler) page.Children.Add(Section("载量升级", carryInfo, carryUpgrade, carryHint));
                page.Children.Add(Section("共享大脑", brainInfo, upgrade, brainHint));
                page.Children.Add(recruit); page.Children.Add(recruitHint);
                var code = GameTheme.Button("〈 / 〉  查看程序　›");
                code.Click += (_, _) => { showingCode = true; BuildPage(); }; page.Children.Add(code);
            }
        }
        page.Children.Add(feedback);
        if (snapshot is not null) Update(snapshot);
    }

    private static void ReleaseLayout(Control control) {
        if (control is Border border && border.Child is { } child) {
            border.Child = null; ReleaseLayout(child);
        } else if (control is Panel panel) {
            foreach (var item in panel.Children.ToArray()) ReleaseLayout(item);
            panel.Children.Clear();
        }
    }

    private static Border Section(string heading, params Control[] contents) {
        var stack = new StackPanel { Spacing = 9 };
        stack.Children.Add(GameTheme.Text(heading, 15, GameTheme.Moss));
        foreach (var child in contents) stack.Children.Add(child);
        return GameTheme.Frame(stack, GameTheme.Card, 12);
    }

    private static string StockHint(Snapshot view, int cost) {
        var shortfall = Math.Max(0, cost + view.MinimumReserve - view.DevelopmentValue);
        return shortfall > 0 ? $"还差 {shortfall} 价值点 · 保留储备 {view.MinimumReserve}" : $"操作后保留 {view.DevelopmentValue - cost} 价值点 · 最低储备 {view.MinimumReserve}";
    }

    public void Update(Snapshot view) {
        snapshot = view;
        if (selectedRole == "queen") {
            title.Text = "蚁后"; subtitle.Text = $"繁殖与信息中枢\n已生产 {view.EggsProduced} 枚卵";
            attributes.Text = $"蚂蚁卵 {view.Eggs}　·　通信 {view.QueenRangeCells} 格　·　已知资源 {view.QueenResources.Length}";
            description.Text = $"待孵化 {view.Eggs} 枚 · 已孵化 {view.EggsHatched} 只\n生产卵后，到对应职业选择孵化。";
            produce.Content = $"生产 1 枚卵　·　{view.EggProductionCost} 价值点";
            produce.IsEnabled = !view.Defeated && !busy && view.DevelopmentValue - view.EggProductionCost >= view.MinimumReserve;
            recruitHint.Text = StockHint(view, view.EggProductionCost);
            networkInfo.Text = $"Lv.{view.TechnologyLevel} / {view.MaxNetworkLevel} · {view.TechnologyEra}\n通信 {view.QueenRangeCells} 格 · 发展储备 {view.DevelopmentValue} 点";
            networkUpgrade.Content = view.NextNetworkUpgradeCost > 0
                ? $"升级网络　·　{view.NextNetworkUpgradeCost} 价值点" : "蚁群网络已满级";
            networkUpgrade.IsEnabled = !view.Defeated && !busy && view.NextNetworkUpgradeCost > 0 && view.DevelopmentValue - view.NextNetworkUpgradeCost >= view.MinimumReserve;
            networkHint.Text = view.NextNetworkUpgradeCost > 0
                ? $"下一级通信 {view.QueenRangeCells} → {view.NextNetworkRangeCells} 格\n" + StockHint(view, view.NextNetworkUpgradeCost)
                : "已达到最高网络等级";
            technologyInfo.Text = "科技等级与虫群网络同步，升级后开放新职业、建筑和配方。\n\n" + string.Join("\n\n",view.TechnologyEras.Select(e=>$"Lv.{e.Level} {e.Name}\n"+string.Join("、",view.Technologies.Where(x=>x.RequiredNetworkLevel==e.Level).Select(x=>$"{(x.Unlocked?"✓":"锁定")} {x.Name}"))));
            return;
        }
        var species = view.Species.FirstOrDefault(x => x.Id == selectedRole);
        if (species is null) return;
        title.Text = showingCode ? species.Name + "程序" : species.Name;
        subtitle.Text = $"{species.Count} 只 · 近 {(view.Economy?.WindowTicks??0)/20.0:0.#} 秒新食物 {view.Economy?.PointsBySpecies.GetValueOrDefault(species.Id)??0} 点\n" + (selectedRole switch { AntSpeciesIds.Builder=>"运送材料，专职建设领地", AntSpeciesIds.Chef=>"驻守厨房，加工高价值食物", AntSpeciesIds.Hauler=>"集运资源，壮大蚁群", AntSpeciesIds.Soldier=>"近战护群，守卫蚁后", _=>"探索地图，发现食物" });
        subtitle.Text+="\n"+string.Join(" · ",view.Ants.Where(a=>a.Species==species.Id).GroupBy(a=>a.Level).OrderBy(g=>g.Key).Select(g=>$"Lv.{g.Key} {g.Count()}只"));
        attributes.Text = $"基础载量 {species.CarryCapacity}　·　视野 {species.VisionCells} 格　·　大脑 Lv.{view.BrainLevel}\n生命 {species.MaxHealth}　·　攻击 {species.AttackDamage}　·　工作 {species.WorkRate}×";
        if (showingCode) {
            var program = view.Programs.Single(x => x.Species == species.Id && x.GroupId==0);
            description.Text = $"当前 v{program.ActiveVersion}" + (program.PendingVersion is { } next ? $" · 待切换 v{next}" : "") +
                $"\n历史：{string.Join("，", program.History.Select(x => $"v{x.Version} {x.Reason}"))}";
            if (source.Text != program.ActiveSource) source.Text = program.ActiveSource;
            return;
        }
        carryInfo.Text = species.NextCarryUpgradeCost > 0 ? $"{species.CarryCapacity} → {species.NextCarryCapacity} 份　 /　{species.NextCarryUpgradeCost} 价值点" : $"最大载量 {species.CarryCapacity} 份";
        carryUpgrade.Content = species.NextCarryUpgradeCost > 0 ? "升级载量" : "载量已满级";
        carryUpgrade.IsEnabled = !view.Defeated && !busy && species.NextCarryUpgradeCost > 0 && view.DevelopmentValue - species.NextCarryUpgradeCost >= view.MinimumReserve;
        carryHint.Text = species.NextCarryUpgradeCost > 0 ? StockHint(view, species.NextCarryUpgradeCost) : "已达到最高载量";
        brainInfo.Text = $"Lv.{view.BrainLevel} / 5 · 预算 {view.InstructionBudget} · 间隔 {view.DecisionInterval} Tick";
        upgrade.Content = view.NextUpgradeCost > 0 ? $"升级大脑　·　{view.NextUpgradeCost} 价值点" : "大脑已满级";
        upgrade.IsEnabled = !view.Defeated && !busy && view.NextUpgradeCost > 0 && view.DevelopmentValue - view.NextUpgradeCost >= view.MinimumReserve;
        brainHint.Text = view.NextUpgradeCost > 0 ? StockHint(view, view.NextUpgradeCost) + "\n同时影响所有职业" : "所有职业共享最高等级";
        recruit.Content = $"孵化{species.Name}　·　1 枚卵";
        recruit.IsEnabled = !view.Defeated && !busy && species.Unlocked && view.Ants.Length < view.PopulationCap && view.Eggs > 0;
        recruitHint.Text = !species.Unlocked?$"需虫群网络 Lv.{species.RequiredNetworkLevel} 解锁该职业":view.Ants.Length >= view.PopulationCap ? "人口已满，请先提高人口上限" : view.Eggs == 0 ? "需要蚂蚁卵 · 前往蚁后页面生产" : $"可用 {view.Eggs} 枚卵 · 人口 {view.Ants.Length}/{view.PopulationCap}";
    }

    private async Task Act(CommandKind kind, int value = 0) {
        if (busy) return;
        busy = true;
        if (snapshot is not null) Update(snapshot);
        try {
            var result = await execute(kind, selectedRole == "queen" ? AntSpeciesIds.Scout : selectedRole, value);
            feedback.Text = result.Accepted ? kind switch {
                CommandKind.UpgradeCarry => "载量升级成功，同类现有与新生蚂蚁共同生效。",
                CommandKind.UpgradeBrain => "大脑升级成功，所有职业共同生效。",
                CommandKind.ProduceEgg => "已生产 1 枚卵，可到职业页面选择孵化。",
                CommandKind.HatchEgg => "孵化成功，蚁群加入一名新成员。",
                CommandKind.UpgradeNetwork => "蚁群网络升级成功，通信范围、职业、建筑和配方已更新。",
                _ => "操作成功。"
            } : $"操作未完成：{result.Code}";
            ActionReported?.Invoke(feedback.Text ?? "");
        } catch (Exception error) { feedback.Text = $"操作失败：{error.Message}"; ActionReported?.Invoke(feedback.Text); }
        finally { busy = false; if (snapshot is not null) Update(snapshot); }
    }
}
