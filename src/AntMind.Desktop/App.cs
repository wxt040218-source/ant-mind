using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Themes.Fluent;
using AntMind.Contracts;
using Point = AntMind.Contracts.Point;
using AntMind.Core;
using AntMind.Runtime;
using AntMind.Transport;
namespace AntMind.Desktop;
public sealed class App : Application {
    public override void Initialize() {
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
        Styles.Add(new FluentTheme());
    }
    public override void OnFrameworkInitializationCompleted() {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow = new GameWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
