using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using AntMind.Contracts;

namespace AntMind.Desktop;

public sealed class MilestonePanel : ScrollViewer {
    private readonly StackPanel list = new() { Spacing = 8, Margin = new Thickness(4, 8) };
    private string signature = "";
    public MilestonePanel() => Content = list;
    public void Update(Snapshot view) {
        var next = string.Join(";", view.Milestones.Select(x=>$"{x.Id}:{x.Current}:{x.Status}:{x.CompletedTick}:" +
            (x.DeadlineTick is { } deadline && x.Status=="active" ? Math.Max(0,deadline-view.Tick)/20 : 0)));
        if (next == signature) return;
        signature = next; list.Children.Clear();
        foreach (var goal in view.Milestones) {
            var row = new Grid { ColumnDefinitions = new("150,*,100"), ColumnSpacing = 14 };
            row.Children.Add(GameTheme.Text(goal.Title, 14, goal.Status=="completed" ? GameTheme.Moss : GameTheme.Ink));
            var detail = new StackPanel { Spacing = 3 };
            var text = goal.Description;
            if (goal.Status=="locked") text += " · 完成前置目标后开启";
            if (goal.Status=="completed") text += $" · 完成于 {goal.CompletedTick / 20.0:0.0} 秒";
            if (goal.DeadlineTick is { } deadline && goal.Status=="active") text += $" · 剩余 {Math.Max(0,deadline-view.Tick)/20} 秒";
            detail.Children.Add(GameTheme.Text(text, 12, GameTheme.Muted));
            detail.Children.Add(new ProgressBar { Minimum=0, Maximum=goal.Target, Value=goal.Status=="completed" ? goal.Target : goal.Current,
                Height=5, Foreground=GameTheme.Moss, Background=GameTheme.Brush("#DBC8AA") });
            Grid.SetColumn(detail,1); row.Children.Add(detail);
            var status = GameTheme.Text(goal.Status switch { "completed"=>"✓ 已达成", "failed"=>"挑战超时", "locked"=>"待解锁", _=>$"{goal.Current} / {goal.Target}" },12);
            status.HorizontalAlignment=HorizontalAlignment.Right; Grid.SetColumn(status,2); row.Children.Add(status);
            list.Children.Add(row);
        }
    }
}
