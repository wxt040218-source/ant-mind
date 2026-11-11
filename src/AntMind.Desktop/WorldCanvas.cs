using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using AntMind.Contracts;
using Point = AntMind.Contracts.Point;
using ScreenPoint = Avalonia.Point;
namespace AntMind.Desktop;

public sealed partial class WorldCanvas : Control {
    private readonly MapCamera camera=new();
    private Snapshot? view;
    private ScreenPoint? pressPosition;
    private ScreenPoint lastPosition;
    private bool dragging;
    private string? placing;
    private Point? preview;
    public string? Placing {get=>placing;set {placing=value;preview=null;InvalidateVisual();}}
    public event Action<string,Point>? BuildRequested;
    public event Action? PlacementCancelled;
    private static readonly IBrush Ground=GameTheme.Brush("#435139");
    private static readonly IBrush AlternateGround=GameTheme.Brush("#46543B");
    private static readonly Pen GridPen=new(GameTheme.Brush("#526047"),1);
    public Snapshot? View {
        get=>view;
        set { var changed=view is null || view.Width!=value?.Width || view.Height!=value?.Height;
            ObserveFeedback(value);view=value; if(changed) {Configure();ViewportChanged?.Invoke();} }
    }
    public int? SelectedAntId {get;set;}
    public string? SelectedObjectKind {get;set;}
    public int SelectedObjectId {get;set;}
    public event Action? ObjectSelected;
    public int? SelectedFoodId {get;set;}
    public double ZoomPercent=>camera.Scale/MapCamera.NativeScale*100;
    public event Action<int>? AntSelected;
    public event Action<int>? FoodSelected;
    public event Action? ViewportChanged;
    public WorldCanvas() {
        InitializeFeedback();
        MinHeight=180;ClipToBounds=true;Focusable=true;
        RenderOptions.SetBitmapInterpolationMode(this,BitmapInterpolationMode.None);
        ToolTip.SetTip(this,"滚轮缩放 · 拖动平移 · 点击蚂蚁或食物查看 · Home 回到蚁后 · F 全图");
        SizeChanged+=(_,_)=> {Configure();Changed();};
        PointerWheelChanged+=(_,e)=> {Configure();var p=e.GetPosition(this);camera.ZoomAt(Math.Pow(1.18,e.Delta.Y),new(p.X,p.Y));Changed();e.Handled=true;};
        PointerPressed+=(_,e)=> {
            var buttons=e.GetCurrentPoint(this).Properties;
            if(!buttons.IsLeftButtonPressed && !buttons.IsMiddleButtonPressed && !buttons.IsRightButtonPressed)return;
            if(placing is not null && buttons.IsRightButtonPressed) {Placing=null;PlacementCancelled?.Invoke();e.Handled=true;return;}
            Focus();pressPosition=e.GetPosition(this);lastPosition=pressPosition.Value;dragging=false;e.Pointer.Capture(this);e.Handled=true;
        };
        PointerMoved+=(_,e)=> {
            var p=e.GetPosition(this);
            if(placing is not null) {preview=MapPoint(p);InvalidateVisual();}
            if(pressPosition is not {} start)return;
            if(Math.Abs(p.X-start.X)+Math.Abs(p.Y-start.Y)>4)dragging=true;
            if(dragging) {camera.Pan(p.X-lastPosition.X,p.Y-lastPosition.Y);Changed();}
            lastPosition=p;
        };
        PointerReleased+=(_,e)=> {
            var pick=pressPosition.HasValue && !dragging;
            pressPosition=null;dragging=false;e.Pointer.Capture(null);
            if(pick && placing is not null)BuildRequested?.Invoke(placing,MapPoint(e.GetPosition(this)));
            else if(pick)SelectAt(e.GetPosition(this));e.Handled=true;
        };
        PointerCaptureLost+=(_,_)=> {pressPosition=null;dragging=false;};
        KeyDown+=(_,e)=> {if(e.Key==Key.Escape && placing is not null){Placing=null;PlacementCancelled?.Invoke();e.Handled=true;}else if(e.Key==Key.Home){FocusQueen();e.Handled=true;}else if(e.Key==Key.F){FitMap();e.Handled=true;}};
    }
    private void Configure() {if(view is {} v)camera.Configure(v.Width,v.Height,Bounds.Width,Bounds.Height);}
    private void Changed() {InvalidateVisual();ViewportChanged?.Invoke();}
    public void ZoomBy(double factor) {Configure();camera.ZoomAt(factor,new(Bounds.Width/2,Bounds.Height/2));Changed();}
    public void FitMap() {Configure();camera.Fit();Changed();}
    public void FocusQueen() {Configure();if(view is {} v)camera.Focus(v.Nest.X/(double)Point.UnitsPerCell,v.Nest.Y/(double)Point.UnitsPerCell);Changed();}
    private Point MapPoint(ScreenPoint p) {var w=camera.ToWorld(new(p.X,p.Y));return Point.FromCells((int)Math.Round(w.X),(int)Math.Round(w.Y));}
    private Rect BuildingBox(Point point) {var p=Screen(point);var size=camera.Scale*2.1;return new(p.X-size/2,p.Y-size/2,size,size);}
    private ScreenPoint Screen(Point p) {var s=camera.ToScreen(new(p.X/(double)Point.UnitsPerCell,p.Y/(double)Point.UnitsPerCell));return new(s.X,s.Y);}
    private Dictionary<int,ScreenPoint> AntCenters() {
        var centers=new Dictionary<int,ScreenPoint>();
        if(view is null)return centers;
        // Fan out only crowded half-cell buckets for readability; physics positions remain exact.
        foreach(var group in view.Ants.GroupBy(a=>(a.Position.X/500,a.Position.Y/500))) {
            var ants=group.OrderBy(a=>a.Id).ToArray();
            for(int i=0;i<ants.Length;i++) {
                var offset=MapSpriteLayout.CrowdOffset(i,ants.Length);
                var projected=camera.ToScreen(new(ants[i].Position.X/(double)Point.UnitsPerCell+offset.X,
                    ants[i].Position.Y/(double)Point.UnitsPerCell+offset.Y));
                var center=new ScreenPoint(projected.X,projected.Y);
                centers[ants[i].Id]=center;
            }
        }return centers;
    }
    private Rect AntBox(ScreenPoint center) {var size=MapSpriteLayout.AntSize(camera.Scale);return new(Math.Round(center.X-size/2),Math.Round(center.Y-size/2),size,size);}
    private Rect FoodBox(ResourceView food) {var p=Screen(food.Position);var size=Math.Clamp(camera.Scale*0.55,6,24);return new(Math.Round(p.X-size/2),Math.Round(p.Y-size/2),size,size);}
    private void SelectAt(ScreenPoint position) {
        if(view is null)return;
        var centers=AntCenters();
        var ant=view.Ants.Where(a=>AntBox(centers[a.Id]).Inflate(2).Contains(position))
            .OrderBy(a=>Math.Abs(centers[a.Id].X-position.X)+Math.Abs(centers[a.Id].Y-position.Y)).ThenBy(a=>a.Id).FirstOrDefault();
        if(ant is not null) {SelectedObjectKind=null;SelectedAntId=ant.Id;SelectedFoodId=null;AntSelected?.Invoke(ant.Id);InvalidateVisual();return;}
        foreach(var obj in view.Buildings.Cast<IWorldObject>().Concat(view.Sources)) {
            var p=Screen(obj.Position);var size=Math.Max(8,camera.Scale);
            if((obj is BuildingView?BuildingBox(obj.Position):new Rect(p.X-size/2,p.Y-size/2,size,size)).Inflate(3).Contains(position)) {SelectedObjectKind=obj.Kind;SelectedObjectId=obj.Id;SelectedAntId=null;SelectedFoodId=null;ObjectSelected?.Invoke();InvalidateVisual();return;}
        }
        var food=view.Resources.Where(f=>f.Amount>0 && FoodBox(f).Inflate(4).Contains(position))
            .OrderBy(f=>Math.Abs(Screen(f.Position).X-position.X)+Math.Abs(Screen(f.Position).Y-position.Y)).ThenBy(f=>f.Id).FirstOrDefault();
        if(food is null)return;
        SelectedObjectKind=null;SelectedFoodId=food.Id;SelectedAntId=null;FoodSelected?.Invoke(food.Id);InvalidateVisual();
    }
    public override void Render(DrawingContext context) {
        base.Render(context);paintTime=MapFeedback.Now;var viewport=Bounds.WithX(0).WithY(0);
        context.FillRectangle(GameTheme.Brush("#303B2A"),viewport);
        if(view is null || viewport.Width<=0 || viewport.Height<=0)return;
        Configure();using var clip=context.PushClip(viewport);
        var origin=camera.ToScreen(new(0,0));
        var worldRect=new Rect(origin.X,origin.Y,view.Width*camera.Scale,view.Height*camera.Scale);
        context.FillRectangle(Ground,worldRect);
        var topLeft=camera.ToWorld(new(0,0));var bottomRight=camera.ToWorld(new(Bounds.Width,Bounds.Height));
        var startX=Math.Max(0,(int)Math.Floor(topLeft.X/8)*8);var startY=Math.Max(0,(int)Math.Floor(topLeft.Y/8)*8);
        for(var x=startX;x<Math.Min(view.Width,bottomRight.X+8);x+=8)
        for(var y=startY;y<Math.Min(view.Height,bottomRight.Y+8);y+=8) {
            var p=camera.ToScreen(new(x,y));
            if((x/8+y/8)%2==0)context.FillRectangle(AlternateGround,new Rect(p.X,p.Y,Math.Min(8,view.Width-x)*camera.Scale,Math.Min(8,view.Height-y)*camera.Scale));
        }
        var grid=camera.Scale>=12?4:8;
        for(var x=Math.Max(0,(int)Math.Floor(topLeft.X/grid)*grid);x<=Math.Min(view.Width,bottomRight.X);x+=grid) {
            var p=camera.ToScreen(new(x,0));context.DrawLine(GridPen,new(p.X,Math.Max(0,worldRect.Top)),new(p.X,Math.Min(Bounds.Height,worldRect.Bottom)));
        }
        for(var y=Math.Max(0,(int)Math.Floor(topLeft.Y/grid)*grid);y<=Math.Min(view.Height,bottomRight.Y);y+=grid) {
            var p=camera.ToScreen(new(0,y));context.DrawLine(GridPen,new(Math.Max(0,worldRect.Left),p.Y),new(Math.Min(Bounds.Width,worldRect.Right),p.Y));
        }
        context.DrawRectangle(new Pen(GameTheme.Brush("#879468"),2),worldRect);
        var nest=Screen(view.Nest);var range=view.QueenRangeCells*camera.Scale;
        context.DrawEllipse(new SolidColorBrush(Color.FromArgb(12,239,209,134)),new Pen(GameTheme.Brush("#BDA675"),1),nest,range,range);
        foreach(var source in view.Sources) {
            var p=Screen(source.Position);var size=camera.Scale*.9;
            var color=source.ItemType is FoodTypes.Wood or FoodTypes.Fiber?"#C6AA70":"#829653";
            using(context.PushTransform(SpriteTransform("source",source.Id,new Rect(p.X-size/2,p.Y-size/2,size,size)))) {
                context.FillRectangle(GameTheme.Brush("#32291F"),new Rect(p.X-size/2-1,p.Y-size/2-1,size+2,size+2));
                context.FillRectangle(GameTheme.Brush(color),new Rect(p.X-size/2,p.Y-size/2,size,size));
                if(source.ItemType==FoodTypes.Fruit && source.Amount>0)context.DrawEllipse(GameTheme.Brush("#D97E51"),null,p,size*.22,size*.22);
            }
        }
        foreach(var b in view.Buildings) {
            var rect=BuildingBox(b.Position);var def=view.BuildingTypes.Single(d=>d.Id==b.Type);
            using(context.PushOpacity(b.Phase=="ready"?1:b.Phase=="cancelled"?.25:.5))DrawSprite(context,"world:"+b.Type,"building",b.Id,rect);
            if(b.Phase=="blueprint") {
                context.DrawRectangle(new Pen(GameTheme.Brush("#D7C390"),1),rect);
                context.FillRectangle(GameTheme.Brush("#312B21"),new Rect(rect.X,rect.Bottom+2,rect.Width,3));
                context.FillRectangle(GameTheme.Brush("#C9D97F"),new Rect(rect.X,rect.Bottom+2,rect.Width*b.ConstructionWork/def.BuildTicks,3));
            }
            if(SelectedObjectKind=="building" && SelectedObjectId==b.Id)context.DrawRectangle(new Pen(Brushes.Yellow,2),rect.Inflate(2));
        }
        if(placing is {} plan && preview is {} point) {
            var rect=BuildingBox(point);var free=BuildingPlacement.IsFree(point,view.Nest,view.Width,view.Height,view.Buildings);
            using(context.PushOpacity(.6))RoleArtwork.Draw(context,"world:"+plan,rect);
            context.DrawRectangle(new Pen(free?Brushes.LightGreen:Brushes.OrangeRed,2),rect);
        }
        foreach(var food in view.Resources.Where(f=>f.Amount>0)) {
            var box=FoodBox(food);if(!box.Inflate(5).Intersects(viewport))continue;
            using(context.PushTransform(SpriteTransform("food",food.Id,box))) {
                context.FillRectangle(GameTheme.Brush("#302B20"),box.Inflate(1));
                context.FillRectangle(food.FoodType==FoodTypes.Meat?GameTheme.Brush("#CF7960"):GameTheme.Brush("#E9B854"),box);
                context.FillRectangle(food.FoodType==FoodTypes.Meat?GameTheme.Paper:GameTheme.Brush("#829853"),new Rect(box.X+box.Width/3,box.Y-2,Math.Max(2,box.Width/3),3));
            }
            if(food.Id==SelectedFoodId)context.DrawRectangle(new Pen(Brushes.Yellow,2),box.Inflate(3));
        }
        var queenSize=MapSpriteLayout.QueenSize(camera.Scale);
        DrawSprite(context,"queen","queen",1,new Rect(nest.X-queenSize/2,nest.Y-queenSize/2,queenSize,queenSize));
        foreach(var enemy in view.Enemies.Where(e=>e.Health>0)) {
            var p=Screen(enemy.Position);var size=camera.Scale*.85;
            DrawSprite(context,"world:beetle","enemy",enemy.Id,new Rect(p.X-size,p.Y-size,size*2,size*2));
            context.FillRectangle(GameTheme.Rust,new Rect(p.X-size,p.Y-size-4,2*size*enemy.Health/enemy.MaxHealth,2));
        }
        if(view.Ants.FirstOrDefault(a=>a.Id==SelectedAntId) is {Target:{} goal} selected) {
            var endpoint=Screen(goal);var guide=new Pen(GameTheme.Brush("#C6D9B0"),1);
            context.DrawLine(guide,Screen(selected.Position),endpoint);
            context.DrawLine(guide,new(endpoint.X-4,endpoint.Y),new(endpoint.X+4,endpoint.Y));
            context.DrawLine(guide,new(endpoint.X,endpoint.Y-4),new(endpoint.X,endpoint.Y+4));
        }
        var centers=AntCenters();
        foreach(var ant in view.Ants.OrderBy(a=>centers[a.Id].Y).ThenBy(a=>a.Id)) {
            var box=AntBox(centers[ant.Id]);if(!box.Inflate(8).Intersects(viewport))continue;
            bool? facing=ant.Target is {} target && target.X!=ant.Position.X?target.X>ant.Position.X:null;
            DrawSprite(context,ant.Species,"ant",ant.Id,box,facing);
            var rankSize=Math.Max(1,camera.Scale*.14);
            for(var rank=1;rank<ant.Level;rank++)context.FillRectangle(ant.Level==3?Brushes.Gold:Brushes.LightSkyBlue,new Rect(box.X+(rank-1)*rankSize*2,box.Bottom+rankSize,rankSize,rankSize));
            if(ant.Carry>0)using(context.PushTransform(SpriteTransform("ant",ant.Id,box)))context.FillRectangle(ant.CarryFood==FoodTypes.Meat?GameTheme.Rust:Brushes.Goldenrod,new Rect(box.Right-4,box.Top+2,4,4));
            if(ant.GroupId>0)context.FillRectangle(GameTheme.Paper,new Rect(box.X+2,box.Bottom-2,3,3));
            if(ant.Health<ant.MaxHealth)context.FillRectangle(GameTheme.Rust,new Rect(box.X,box.Top-3,box.Width*ant.Health/ant.MaxHealth,2));
            if(ant.HealUntilTick>0)context.FillRectangle(Brushes.LightGreen,new Rect(box.X,box.Top-6,box.Width,2));
            if(ant.Id==SelectedAntId)context.DrawRectangle(new Pen(Brushes.Yellow,2),box.Inflate(2));
        }
        DrawFeedback(context);
    }
}
