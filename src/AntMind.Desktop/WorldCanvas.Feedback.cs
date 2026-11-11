using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
namespace AntMind.Desktop;

public sealed partial class WorldCanvas {
    private readonly MapFeedback feedback=new();
    private readonly DispatcherTimer feedbackTimer=new() {Interval=TimeSpan.FromMilliseconds(16)};
    private bool feedbackAttached;
    private double paintTime;
    private static readonly IBrush CargoGlow=GameTheme.Brush("#F5D785");
    private static readonly IBrush CargoShade=GameTheme.Brush("#BD854B");
    private static readonly IBrush LeafGlow=GameTheme.Brush("#C1D589");
    private static readonly IBrush HitGlow=GameTheme.Brush("#F09C77");
    private void InitializeFeedback() {
        feedbackTimer.Tick+=(_,_)=> {feedback.Trim(MapFeedback.Now);InvalidateVisual();if(!feedback.Active)feedbackTimer.Stop();};
        AttachedToVisualTree+=(_,_)=> {feedbackAttached=true;if(feedback.Active)feedbackTimer.Start();};
        DetachedFromVisualTree+=(_,_)=> {feedbackAttached=false;feedbackTimer.Stop();feedback.Clear();};
    }
    private void ObserveFeedback(AntMind.Contracts.Snapshot? next) {
        feedback.Observe(view,next,MapFeedback.Now);
        if(feedbackAttached && feedback.Active)feedbackTimer.Start();
    }
    private Matrix SpriteTransform(string kind,int id,Rect box) {
        var pose=feedback.Pose(kind,id,paintTime);
        return Matrix.CreateTranslation(-box.Center.X,-box.Bottom)*Matrix.CreateScale(pose.ScaleX,pose.ScaleY)*
            Matrix.CreateTranslation(box.Center.X+pose.X*box.Width,box.Bottom+pose.Y*box.Height);
    }
    private void DrawSprite(DrawingContext context,string role,string kind,int id,Rect box,bool? facing=null) {
        using(context.PushTransform(SpriteTransform(kind,id,box)))RoleArtwork.Draw(context,role,box,facing);
    }
    private void DrawFeedback(DrawingContext context) {
        foreach(var pulse in feedback.Accents) {
            var p=pulse.Progress(paintTime);if(p>=1)continue;
            var center=Screen(pulse.Position);var size=camera.Scale;
            var radius=size*(.18+.70*p);var pixel=Math.Max(1,Math.Round(size*.075));
            var color=pulse.Style switch {MapFeedbackKind.Hurt or MapFeedbackKind.Attack=>HitGlow,MapFeedbackKind.Heal or MapFeedbackKind.Gather=>LeafGlow,_=>CargoGlow};
            using var fade=context.PushOpacity(Math.Pow(1-p,1.4)*.9);
            if(pulse.Style==MapFeedbackKind.Deliver && pulse.Origin is {} origin) {
                var start=Screen(origin);var travel=1-Math.Pow(1-p,3);
                for(var i=0;i<Math.Min(3,Math.Max(1,pulse.Amount));i++) {
                    var t=Math.Clamp(travel-i*.10,0,1);
                    var x=start.X+(center.X-start.X)*t;var y=start.Y+(center.Y-start.Y)*t-Math.Sin(t*Math.PI)*size*.8;
                    var bead=new Rect(Math.Round(x-pixel),Math.Round(y-pixel),pixel*2,pixel*2);
                    context.FillRectangle(CargoShade,bead.Inflate(1));context.FillRectangle(CargoGlow,bead);
                }
            }
            // Sparse hard-edged sparks, readable at both overview and close zoom.
            if(pulse.Style==MapFeedbackKind.Work) {
                var x=center.X+size*.30+radius*.35;var y=center.Y-size*.3-radius*.35;
                context.FillRectangle(CargoGlow,new Rect(Math.Round(x),Math.Round(y),pixel*2,pixel));
                context.FillRectangle(CargoGlow,new Rect(Math.Round(x+pixel),Math.Round(y-pixel),pixel,pixel*3));
                continue;
            }
            if(pulse.Style==MapFeedbackKind.Heal) {
                var x=Math.Round(center.X);var y=Math.Round(center.Y-size*.4-p*size*.6);
                context.FillRectangle(LeafGlow,new Rect(x-pixel,y,pixel*3,pixel));
                context.FillRectangle(LeafGlow,new Rect(x,y-pixel,pixel,pixel*3));
                continue;
            }
            var rays=pulse.Style is MapFeedbackKind.Complete or MapFeedbackKind.Spawn?8:4;
            for(var i=0;i<rays;i++) {
                var angle=(i+.5)*Math.PI*2/rays;
                var x=center.X+Math.Cos(angle)*radius;var y=center.Y+Math.Sin(angle)*radius*.65-size*.15;
                context.FillRectangle(color,new Rect(Math.Round(x),Math.Round(y),pixel,pixel));
            }
        }
    }
}
