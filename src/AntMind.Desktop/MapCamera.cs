namespace AntMind.Desktop;

// Screen-only doubles never enter simulation state or change perception permissions.
public readonly record struct CameraPoint(double X, double Y);
public sealed class MapCamera {
    public const double NativeScale = 16;
    public const double MaximumScale = 64;
    public double WorldWidth { get; private set; } = 128;
    public double WorldHeight { get; private set; } = 128;
    public double ViewWidth { get; private set; } = 1;
    public double ViewHeight { get; private set; } = 1;
    public double Scale { get; private set; } = NativeScale;
    public CameraPoint Center { get; private set; } = new(64,64);
    public double FitScale => Math.Min(ViewWidth/WorldWidth,ViewHeight/WorldHeight);
    public void Configure(double worldWidth,double worldHeight,double viewWidth,double viewHeight) {
        if(worldWidth<=0 || worldHeight<=0 || viewWidth<=0 || viewHeight<=0) return;
        var changed=worldWidth!=WorldWidth || worldHeight!=WorldHeight;
        WorldWidth=worldWidth;WorldHeight=worldHeight;ViewWidth=viewWidth;ViewHeight=viewHeight;
        if(changed) Center=new(worldWidth/2,worldHeight/2);
        Scale=Math.Clamp(Scale,Math.Min(FitScale,MaximumScale),MaximumScale);ClampCenter();
    }
    public CameraPoint ToScreen(CameraPoint p)=>new((p.X-Center.X)*Scale+ViewWidth/2,(p.Y-Center.Y)*Scale+ViewHeight/2);
    public CameraPoint ToWorld(CameraPoint p)=>new((p.X-ViewWidth/2)/Scale+Center.X,(p.Y-ViewHeight/2)/Scale+Center.Y);
    public void ZoomAt(double factor,CameraPoint anchor) {
        if(!double.IsFinite(factor) || factor<=0) return;
        var before=ToWorld(anchor);
        Scale=Math.Clamp(Scale*factor,Math.Min(FitScale,MaximumScale),MaximumScale);
        var after=ToWorld(anchor);Center=new(Center.X+before.X-after.X,Center.Y+before.Y-after.Y);ClampCenter();
    }
    public void Pan(double screenX,double screenY) {Center=new(Center.X-screenX/Scale,Center.Y-screenY/Scale);ClampCenter();}
    public void Focus(double x,double y) {Center=new(x,y);ClampCenter();}
    public void Fit() {Scale=Math.Min(FitScale,MaximumScale);Center=new(WorldWidth/2,WorldHeight/2);}
    private void ClampCenter() {
        var halfX=ViewWidth/(2*Scale);var halfY=ViewHeight/(2*Scale);
        Center=new(halfX>=WorldWidth/2?WorldWidth/2:Math.Clamp(Center.X,halfX,WorldWidth-halfX),
            halfY>=WorldHeight/2?WorldHeight/2:Math.Clamp(Center.Y,halfY,WorldHeight-halfY));
    }
}

// Layout offsets and dimensions are world units, so camera zoom scales them uniformly.
public static class MapSpriteLayout {
    public static CameraPoint CrowdOffset(int index,int count) {
        if(count<=1)return new(0,0);
        var angle=2*Math.PI*index/count;
        var radius=Math.Min(28,8+count*2)/MapCamera.NativeScale;
        return new(Math.Cos(angle)*radius,Math.Sin(angle)*radius);
    }
    public static double AntSize(double scale)=>scale*1.8;
    public static double QueenSize(double scale)=>scale*3.6;
}
