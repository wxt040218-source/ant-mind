using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
namespace AntMind.Desktop;

public sealed class RolePortrait : Control {
    private readonly string role;
    public RolePortrait(string role,double size) {
        this.role=role;Width=size;Height=size;
        RenderOptions.SetBitmapInterpolationMode(this,BitmapInterpolationMode.None);
    }
    public override void Render(DrawingContext context) {
        base.Render(context);RoleArtwork.Draw(context,role,Bounds.WithX(0).WithY(0));
    }
}
