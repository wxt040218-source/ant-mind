namespace AntMind.Contracts;

// Plans are public colony records. Placement never probes hidden resources or creatures.
public static class BuildingPlacement {
    public static bool IsFree(Point p,Point nest,int width,int height,IEnumerable<BuildingView> buildings) {
        static bool Close(Point a,Point b) {var dx=(long)a.X-b.X;var dy=(long)a.Y-b.Y;return dx*dx+dy*dy<4_000_000;}
        return p.X>=0 && p.Y>=0 && p.X<=(width-1)*1000 && p.Y<=(height-1)*1000 && !Close(p,nest) &&
            !buildings.Any(b=>b.Phase!="cancelled" && Close(p,b.Position));
    }
}
