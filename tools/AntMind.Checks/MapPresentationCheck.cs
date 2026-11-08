using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using AntMind.Desktop;
using AntMind.Runtime;

internal static class MapPresentationCheck {
    public static void Run(Action<bool,string> check) {
        var camera=new MapCamera();camera.Configure(128,128,800,600);
        var center=camera.ToScreen(new(64,64));
        check(center==new CameraPoint(400,300),"map camera initially centers the larger world at native readable scale");
        var anchor=new CameraPoint(570,410);var before=camera.ToWorld(anchor);
        camera.ZoomAt(2,anchor);var after=camera.ToWorld(anchor);
        check(Math.Abs(before.X-after.X)<1e-9 && Math.Abs(before.Y-after.Y)<1e-9,"zoom keeps the world point under the cursor stationary away from edges");
        camera.Pan(110,-60);
        var world=new CameraPoint(70.25,65.75);var roundTrip=camera.ToWorld(camera.ToScreen(world));
        check(Math.Abs(world.X-roundTrip.X)<1e-9 && Math.Abs(world.Y-roundTrip.Y)<1e-9,"selection transform remains reversible after zoom and drag");
        camera.ZoomAt(1e9,anchor);check(camera.Scale==MapCamera.MaximumScale,"camera caps excessive zoom in");
        camera.ZoomAt(1e-9,anchor);check(Math.Abs(camera.Scale-camera.FitScale)<1e-9,"camera zoom out stops at full-map fit");
        camera.Fit();var corner=camera.ToScreen(new(0,0));var end=camera.ToScreen(new(128,128));
        check(corner.X>=-1e-9 && corner.Y>=-1e-9 && end.X<=800+1e-9 && end.Y<=600+1e-9,"full-map action keeps the entire rectangular world inside the viewport");
        camera.ZoomAt(4,new(400,300));camera.Pan(1e9,-1e9);
        var visible=camera.ToWorld(new(0,0));var lower=camera.ToWorld(new(800,600));
        check(visible.X>=-1e-9 && visible.Y>=-1e-9 && lower.X<=128+1e-9 && lower.Y<=128+1e-9,"pan is clamped to map boundaries");
        camera.Focus(64,64);check(camera.Center==new CameraPoint(64,64),"queen focus returns to the actual world nest");
        var scale=camera.Scale;camera.Configure(128,128,500,300);
        check(camera.Scale==scale && camera.ToScreen(camera.Center)==new CameraPoint(250,150),"window resize preserves zoom while updating screen center");
        camera.Configure(128,128,0,0);check(double.IsFinite(camera.Scale),"transient zero-sized layout does not corrupt camera state");
        var offset=MapSpriteLayout.CrowdOffset(0,10);
        var other=MapSpriteLayout.CrowdOffset(1,10);
        camera.Configure(128,128,800,600);camera.Focus(64,64);
        var screenA=camera.ToScreen(new(64+offset.X,64+offset.Y));
        var screenB=camera.ToScreen(new(64+other.X,64+other.Y));
        var distance=Math.Sqrt(Math.Pow(screenA.X-screenB.X,2)+Math.Pow(screenA.Y-screenB.Y,2));
        var initialScale=camera.Scale;
        camera.ZoomAt(2,new(400,300));
        screenA=camera.ToScreen(new(64+offset.X,64+offset.Y));screenB=camera.ToScreen(new(64+other.X,64+other.Y));
        var zoomDistance=Math.Sqrt(Math.Pow(screenA.X-screenB.X,2)+Math.Pow(screenA.Y-screenB.Y,2));
        check(Math.Abs(zoomDistance-distance*2)<1e-9,"crowded ant spacing doubles with map zoom instead of remaining fixed in screen pixels");
        check(Math.Abs(MapSpriteLayout.AntSize(initialScale*2)/zoomDistance-MapSpriteLayout.AntSize(initialScale)/distance)<1e-9,
            "ant size and inter-ant spacing maintain the same ratio during zoom");
        check(MapSpriteLayout.AntSize(2)==3.6 && MapSpriteLayout.QueenSize(2)==7.2,
            "zooming out has no fixed-pixel sprite floor that squeezes apparent spacing");
        var config=ContentLoader.LoadDefault();check(config.Width==128 && config.Height==128,"official world expands from 64 squared to 128 squared cells");
        var directory="src/AntMind.Desktop/Assets/Roles";
        using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"manifest.json")));
        foreach(var role in new[]{"queen","hauler","scout","soldier"}) {
            var data=File.ReadAllBytes(Path.Combine(directory,role+".png"));
            var hash=Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
            check(data.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}) && data[25]==6 &&
                BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(16,4))>0 && hash==manifest.RootElement.GetProperty(role).GetProperty("sha256").GetString(),
                "original transparent RGBA role asset is present and unmodified: "+role);
        }
    }
}
