namespace AntMind.Contracts;

// Deterministic geometry in fixed-point units, shared by navigation and DSL queries.
public static class PointMath {
    public static long Distance(Point from,Point to) {
        var dx=Math.Abs((long)to.X-from.X);var dy=Math.Abs((long)to.Y-from.Y);
        var squared=(UInt128)(ulong)dx*(ulong)dx+(UInt128)(ulong)dy*(ulong)dy;
        long low=0,high=dx+dy;
        while(low<high) {
            var middle=low+(high-low)/2;
            if((UInt128)(ulong)middle*(ulong)middle>=squared)high=middle;
            else low=middle+1;
        }
        return low; // Ceiling length: rounding can never grant diagonal speed.
    }
    public static Point Towards(Point from,Point target,int maxDistance) {
        if(maxDistance<0)throw new ArgumentOutOfRangeException(nameof(maxDistance));
        if(maxDistance==0 || from==target)return from;
        var length=Distance(from,target);
        if(length<=maxDistance)return target;
        var dx=(long)target.X-from.X;var dy=(long)target.Y-from.Y;
        var stepX=dx*maxDistance/length;var stepY=dy*maxDistance/length;
        // Sub-unit projections cannot be represented. Make progress along the dominant axis.
        if(stepX==0 && stepY==0) {
            if(Math.Abs(dx)>=Math.Abs(dy))stepX=Math.Sign(dx);
            else stepY=Math.Sign(dy);
        }
        return new(checked((int)(from.X+stepX)),checked((int)(from.Y+stepY)));
    }
}
