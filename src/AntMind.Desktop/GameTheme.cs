using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AntMind.Desktop;

internal static class GameTheme {
    public static readonly IBrush Ink = Brush("#493329");
    public static readonly IBrush Muted = Brush("#846C52");
    public static readonly IBrush Paper = Brush("#F4E6CC");
    public static readonly IBrush Card = Brush("#FFEFDA");
    public static readonly IBrush Line = Brush("#B89A70");
    public static readonly IBrush Wood = Brush("#382A25");
    public static readonly IBrush Moss = Brush("#65713D");
    public static readonly IBrush Rust = Brush("#AD5D42");
    public static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));
    public static TextBlock Text(string text = "", double size = 14, IBrush? color = null) => new() {
        Text = text, FontSize = size, Foreground = color ?? Ink, TextWrapping = TextWrapping.Wrap
    };
    public static Button Button(string text, bool primary = false) => new() {
        Content = text, Background = primary ? Moss : Card, Foreground = primary ? Paper : Ink,
        BorderBrush = primary ? Brush("#465127") : Line, BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(2), Padding = new Thickness(14, 10),
        HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center,
        MinHeight = 40
    };
    public static Border Frame(Control child, IBrush? background = null, double padding = 14) => new() {
        Child = child, Background = background ?? Paper, BorderBrush = Line,
        BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(3), Padding = new Thickness(padding)
    };
}
