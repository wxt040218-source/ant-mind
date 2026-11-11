using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using AntMind.Contracts;
namespace AntMind.Desktop;

// Original transparent PNGs are shared by menus and map. Never reload/decode per frame.
internal static class RoleArtwork {
    private sealed record Artwork(Bitmap Bitmap,bool FacesRight);
    private static readonly Dictionary<string,Artwork> Roles=new(StringComparer.Ordinal) {
        ["queen"]=Load("queen",false),[AntSpeciesIds.Scout]=Load("scout",true),
        [AntSpeciesIds.Hauler]=Load("hauler",false),[AntSpeciesIds.Soldier]=Load("soldier",true),
        [AntSpeciesIds.Chef]=Load("chef",true),[AntSpeciesIds.Builder]=Load("builder",true),
        ["world:cache"]=LoadWorld("cache"),["world:workshop"]=LoadWorld("workshop"),["world:beetle"]=LoadWorld("beetle")
    };
    private static Artwork Load(string role,bool facesRight)=>new(new Bitmap(AssetLoader.Open(
        new Uri($"avares://AntMind.Desktop/Assets/Roles/{role}.png"))),facesRight);
    private static Artwork LoadWorld(string name)=>new(new Bitmap(AssetLoader.Open(new Uri($"avares://AntMind.Desktop/Assets/World/{name}.png"))),true);
    public static void Draw(DrawingContext context,string role,Rect box,bool? facesRight=null) {
        if(!Roles.TryGetValue(role,out var art) || box.Width<=0 || box.Height<=0) return;
        var size=art.Bitmap.PixelSize;
        var scale=Math.Min(box.Width/size.Width,box.Height/size.Height);
        var width=size.Width*scale;var height=size.Height*scale;
        var destination=new Rect(Math.Round(box.Center.X-width/2),Math.Round(box.Center.Y-height/2),Math.Round(width),Math.Round(height));
        if(facesRight is {} facing && facing!=art.FacesRight) {
            using var mirror=context.PushTransform(Matrix.CreateScale(-1,1)*Matrix.CreateTranslation(2*box.Center.X,0));
            context.DrawImage(art.Bitmap,new Rect(0,0,size.Width,size.Height),destination);
        } else context.DrawImage(art.Bitmap,new Rect(0,0,size.Width,size.Height),destination);
    }
}
