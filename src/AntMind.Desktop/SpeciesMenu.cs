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
    private readonly Grid tabs = new() { ColumnDefinitions = new("*,*,*,*,*"), ColumnSpacing = 6 };
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
        foreach (var (id, name) in new[] { ("queen", "蚁后"), (AntSpeciesIds.Scout, "侦察蚁"), (AntSpeciesIds.Hauler, "搬运蚁"), (AntSpeciesIds.Soldier, "兵蚁"), (AntSpeciesIds.Chef,"厨师蚁") }) {
            var label = new StackPanel { Spacing = 3, HorizontalAlignment = HorizontalAlignment.Center };
            label.Children.Add(new RolePortrait(id, 43));
            label.Children.Add(GameTheme.Text(name, 13));
            var button = GameTheme.Button(name); button.Content = label; button.Padding = new Thickness(6);
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
                page.Children.Add(Section("蚁群网络", networkInfo, networkUpgrade, networkHint, technologyInfo));
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
        var shortfall = Math.Max(0, cost + view.MinimumReserve - view.Inventory);
        return shortfall > 0 ? $"还差 {shortfall} 食物 · 保留储备 {view.MinimumReserve}" : $"操作后保留 {view.Inventory - cost} 食物 · 最低储备 {view.MinimumReserve}";
    }

    public void Update(Snapshot view) {
        snapshot = view;
        if (selectedRole == "queen") {
            title.Text = "蚁后"; subtitle.Text = $"繁殖与信息中枢\n已生产 {view.EggsProduced} 枚卵";
            attributes.Text = $"蚂蚁卵 {view.Eggs}　·　通信 {view.QueenRangeCells} 格　·　已知资源 {view.QueenResources.Length}";
            description.Text = $"待孵化 {view.Eggs} 枚 · 已孵化 {view.EggsHatched} 只\n生产卵后，到对应职业选择孵化。";
            produce.Content = $"生产 1 枚卵　·　{view.EggProductionCost} 食物";
            produce.IsEnabled = !view.Defeated && !busy && view.Inventory - view.EggProductionCost >= view.MinimumReserve;
            recruitHint.Text = StockHint(view, view.EggProductionCost);
            networkInfo.Text = $"Lv.{view.NetworkLevel} / {view.MaxNetworkLevel}　·　通信 {view.QueenRangeCells} 格";
            networkUpgrade.Content = view.NextNetworkUpgradeCost > 0
                ? $"升级网络　·　{view.NextNetworkUpgradeCost} 食物" : "蚁群网络已满级";
            networkUpgrade.IsEnabled = !view.Defeated && !busy && view.NextNetworkUpgradeCost > 0 && view.Inventory - view.NextNetworkUpgradeCost >= view.MinimumReserve;
            networkHint.Text = view.NextNetworkUpgradeCost > 0
                ? $"下一级通信 {view.QueenRangeCells} → {view.NextNetworkRangeCells} 格\n" + StockHint(view, view.NextNetworkUpgradeCost)
                : "已达到最高网络等级";
            technologyInfo.Text = "科技解锁条件由网络等级决定。\n" + string.Join("\n", view.Technologies.Select(x =>
                $"{(x.Unlocked ? "已开放" : "未开放")} · {x.Name} · Lv.{x.RequiredNetworkLevel}"));
            return;
        }
        var species = view.Species.FirstOrDefault(x => x.Id == selectedRole);
        if (species is null) return;
        title.Text = showingCode ? species.Name + "程序" : species.Name;
        subtitle.Text = $"{species.Count} 只 · 已送回 {species.Delivered}\n" + (selectedRole switch { AntSpeciesIds.Chef=>"建设加工台，制作浓缩食物", AntSpeciesIds.Hauler=>"集运资源，壮大蚁群", AntSpeciesIds.Soldier=>"近战护群，守卫蚁后", _=>"探索地图，发现食物" });
        attributes.Text = $"载量 {species.CarryCapacity}　·　视野 {species.VisionCells} 格　·　大脑 Lv.{view.BrainLevel}\n生命 {species.MaxHealth}　·　攻击 {species.AttackDamage}　·　工作 {species.WorkRate}×";
        if (showingCode) {
            var program = view.Programs.Single(x => x.Species == species.Id && x.GroupId==0);
            description.Text = $"当前 v{program.ActiveVersion}" + (program.PendingVersion is { } next ? $" · 待切换 v{next}" : "") +
                $"\n历史：{string.Join("，", program.History.Select(x => $"v{x.Version} {x.Reason}"))}";
            if (source.Text != program.ActiveSource) source.Text = program.ActiveSource;
            return;
        }
        carryInfo.Text = species.NextCarryUpgradeCost > 0 ? $"{species.CarryCapacity} → {species.NextCarryCapacity} 份　 /　{species.NextCarryUpgradeCost} 食物" : $"最大载量 {species.CarryCapacity} 份";
        carryUpgrade.Content = species.NextCarryUpgradeCost > 0 ? "升级载量" : "载量已满级";
        carryUpgrade.IsEnabled = !view.Defeated && !busy && species.NextCarryUpgradeCost > 0 && view.Inventory - species.NextCarryUpgradeCost >= view.MinimumReserve;
        carryHint.Text = species.NextCarryUpgradeCost > 0 ? StockHint(view, species.NextCarryUpgradeCost) : "已达到最高载量";
        brainInfo.Text = $"Lv.{view.BrainLevel} / 5 · 预算 {view.InstructionBudget} · 间隔 {view.DecisionInterval} Tick";
        upgrade.Content = view.NextUpgradeCost > 0 ? $"升级大脑　·　{view.NextUpgradeCost} 食物" : "大脑已满级";
        upgrade.IsEnabled = !view.Defeated && !busy && view.NextUpgradeCost > 0 && view.Inventory - view.NextUpgradeCost >= view.MinimumReserve;
        brainHint.Text = view.NextUpgradeCost > 0 ? StockHint(view, view.NextUpgradeCost) + "\n同时影响所有职业" : "所有职业共享最高等级";
        recruit.Content = $"孵化{species.Name}　·　1 枚卵";
        recruit.IsEnabled = !view.Defeated && !busy && view.Ants.Length < view.PopulationCap && view.Eggs > 0;
        recruitHint.Text = view.Ants.Length >= view.PopulationCap ? "人口已满，请先提高人口上限" : view.Eggs == 0 ? "需要蚂蚁卵 · 前往蚁后页面生产" : $"可用 {view.Eggs} 枚卵 · 人口 {view.Ants.Length}/{view.PopulationCap}";
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
                CommandKind.UpgradeNetwork => "蚁群网络升级成功，通信范围与科技权限已更新。",
                _ => "操作成功。"
            } : $"操作未完成：{result.Code}";
            ActionReported?.Invoke(feedback.Text ?? "");
        } catch (Exception error) { feedback.Text = $"操作失败：{error.Message}"; ActionReported?.Invoke(feedback.Text); }
        finally { busy = false; if (snapshot is not null) Update(snapshot); }
    }
}
