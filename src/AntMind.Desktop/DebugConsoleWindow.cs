using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using AntMind.Contracts;
using AntMind.Transport;
namespace AntMind.Desktop;

public sealed class DebugConsoleWindow : Window {
    private readonly CommandConsole console;
    private readonly CancellationTokenSource lifetime;
    private readonly TextBox output=new() {IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.NoWrap,FontFamily=new FontFamily("Consolas"),FontSize=13};
    private readonly TextBox input=new() {PlaceholderText="help · status · ants · ant 1 · pause · step 100 · execute …",FontFamily=new FontFamily("Consolas"),FontSize=14};
    private readonly Button run=GameTheme.Button("执行");
    private bool busy;
    public DebugConsoleWindow(Func<WireRequest,CancellationToken,Task<WireResponse>> send,CancellationToken shutdown) {
        console=new(send);lifetime=CancellationTokenSource.CreateLinkedTokenSource(shutdown);
        Title="Ant Mind · 调试控制台";Width=1050;Height=660;MinWidth=700;MinHeight=400;Background=GameTheme.Wood;
        var panel=new Grid {RowDefinitions=new("Auto,*,Auto"),RowSpacing=10,Margin=new Thickness(14)};
        panel.Children.Add(GameTheme.Text("本地调试 · 复用游戏命令与发现权限 · F12 打开",14,GameTheme.Paper));
        output.Background=GameTheme.Brush("#29251F");output.Foreground=GameTheme.Paper;
        Grid.SetRow(output,1);panel.Children.Add(output);
        var row=new Grid {ColumnDefinitions=new("*,Auto"),ColumnSpacing=8};row.Children.Add(input);Grid.SetColumn(run,1);row.Children.Add(run);Grid.SetRow(row,2);panel.Children.Add(row);
        Content=panel;output.Text=CommandConsole.Help+"\n";
        run.Click+=async (_,_)=>await Execute();
        input.KeyDown+=async (_,e)=>{if(e.Key==Key.Enter){e.Handled=true;await Execute();}};
        Opened+=(_,_)=>input.Focus();Closed+=(_,_)=>{lifetime.Cancel();lifetime.Dispose();};
    }
    private async Task Execute() {
        if(busy || string.IsNullOrWhiteSpace(input.Text))return;
        var line=input.Text;input.Text="";busy=true;run.IsEnabled=false;input.IsEnabled=false;
        try {
            var result=await console.ExecuteAsync(line,lifetime.Token);
            var text=output.Text+"\n> "+line+"\n"+result+"\n";
            output.Text=text.Length>60000?text[^60000..]:text;output.CaretIndex=output.Text.Length;
        } catch(OperationCanceledException) { }
        finally {busy=false;run.IsEnabled=true;input.IsEnabled=true;input.Focus();}
    }
}
