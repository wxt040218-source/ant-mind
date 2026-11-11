using System.Collections.Immutable;
using AntMind.Contracts;
using AntMind.Core;
using AntMind.Programs;
namespace AntMind.Runtime;

public sealed partial class GameSession {
    private static bool EquivalentCommands(GameCommand a,GameCommand b) =>
        a with { AntIds=default,GroupConfiguration=null } == b with { AntIds=default,GroupConfiguration=null } &&
        EquivalentSetup(a.GroupConfiguration,b.GroupConfiguration) &&
        (a.AntIds.IsDefault?ImmutableArray<int>.Empty:a.AntIds).SequenceEqual(b.AntIds.IsDefault?ImmutableArray<int>.Empty:b.AntIds);
    private static bool EquivalentSetup(GroupConfiguration? a,GroupConfiguration? b) => a is null?b is null:b is not null &&
        a with {Members=[],Parameters=ImmutableDictionary<string,int>.Empty,Points=ImmutableDictionary<string,Point>.Empty} == b with {Members=[],Parameters=ImmutableDictionary<string,int>.Empty,Points=ImmutableDictionary<string,Point>.Empty} &&
        a.Members.IsDefault==b.Members.IsDefault && (a.Members.IsDefault || a.Members.SequenceEqual(b.Members)) &&
        EquivalentMap(a.Parameters,b.Parameters) && EquivalentMap(a.Points,b.Points);
    private static bool EquivalentMap<T>(ImmutableDictionary<string,T>? a,ImmutableDictionary<string,T>? b)=>a is null?b is null:b is not null && a.OrderBy(x=>x.Key,StringComparer.Ordinal).SequenceEqual(b.OrderBy(x=>x.Key,StringComparer.Ordinal));
    private bool QueueProgram(GameCommand command,out string error) {
        error="invalid_species";
        if(!world.Config.AntTypes.Any(x=>x.Id==command.Species)) return false;
        try { AntDslCompiler.Compile(command.ProgramSource); }
        catch(Exception ex) when(ex is FormatException or ArgumentException) { error=ex.Message;return false; }
        return programCatalog.TryQueueApply(command.Species,command.ProgramSource,command.Reason,out _,out error);
    }
    private static AntView ResetBehavior(AntView ant) => ant with {
        Target=null,Intent=AntAction.Wait,WorkTicksRemaining=0,NextDecisionTick=0,HealUntilTick=0,RequestQueenInfo=false,ReportDiscovery=false,
        ClaimFoodId=0,ReleaseClaim=false,Memory=Enumerable.Repeat(0,AntDslCompiler.MemorySlots).ToImmutableArray(),
        LastDecisionBranch="assignment-changed",LastInstructionSpent=0,LastError=null
    };
    private CommandResult ApplyGroupCommand(GameCommand command) {
        CommandResult Fail(string code)=>new(command.RequestId,false,code,revision);
        if(string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length>256) return Fail("reason_required");
        if(command.Kind==CommandKind.ConfigureGroup)return ConfigureGroup(command);
        var group=command.Kind==CommandKind.CreateGroup?null:world.Groups.FirstOrDefault(g=>g.Id==command.GroupId);
        if(command.Kind!=CommandKind.CreateGroup && command.GroupId!=0 && group is null) return Fail("group_not_found");
        bool PointValid(Point? p)=>p is null || p.Value.X>=0 && p.Value.Y>=0 && p.Value.X<(long)world.Config.Width*Point.UnitsPerCell && p.Value.Y<(long)world.Config.Height*Point.UnitsPerCell;
        if(!PointValid(command.Rally) || !PointValid(command.Objective) || !PointValid(command.Retreat)) return Fail("invalid_group_point");
        if(command.Kind is CommandKind.CreateGroup or CommandKind.UpdateGroup) {
            if(string.IsNullOrWhiteSpace(command.Name) || command.Name.Length>40 || command.Task.Length>256) return Fail("invalid_group_name_or_task");
            if(command.Kind==CommandKind.CreateGroup && world.Groups.Length>=32) return Fail("group_capacity");
            if(command.Kind==CommandKind.UpdateGroup && group is null) return Fail("group_not_found");
            var activity=command.Activity??group?.Activity??GroupActivities.Collect;
            var filter=command.FoodFilter??group?.FoodFilter??"";
            var radius=command.RadiusCells??group?.RadiusCells??8;
            if(string.IsNullOrWhiteSpace(activity) || activity.Length>80) return Fail("invalid_group_activity");
            if(filter.Length>0 && !world.Config.Foods.Any(f=>f.Id==filter)) return Fail("invalid_group_food");
            if(radius is <1 or >32) return Fail("invalid_group_radius");
            var nest=Point.FromCells(world.Config.Width/2,world.Config.Height/2);
            var next=new GroupOrder(group?.Id??world.NextGroupId,command.Name,command.Task,
                command.Rally??group?.Rally??nest,command.Objective??group?.Objective??nest,command.Retreat??group?.Retreat??nest,
                (group?.Version??0)+1) { Activity=activity,FoodFilter=filter,RadiusCells=radius,
                Parameters=group?.Parameters??ImmutableDictionary<string,int>.Empty,Points=group?.Points??ImmutableDictionary<string,Point>.Empty };
            world=world with { Groups=world.Groups.Where(g=>g.Id!=next.Id).Append(next).OrderBy(g=>g.Id).ToImmutableArray(),
                NextGroupId=command.Kind==CommandKind.CreateGroup?checked(world.NextGroupId+1):world.NextGroupId };
        } else if(command.Kind is CommandKind.AssignAnt or CommandKind.AssignAnts) {
            var ids=command.Kind==CommandKind.AssignAnt?[command.AntId]:command.AntIds.IsDefault?ImmutableArray<int>.Empty:command.AntIds;
            if(ids.IsEmpty || ids.Length>100 || ids.Distinct().Count()!=ids.Length) return Fail("invalid_ant_ids");
            if(ids.Any(id=>!world.Ants.Any(a=>a.Id==id))) return Fail("ant_not_found");
            var members=ids.ToHashSet();
            world=world with { Ants=world.Ants.Select(a=>members.Contains(a.Id)?ResetBehavior(a) with { GroupId=command.GroupId,ReceivedOrder=null,QueenClaims=[] }:a).ToImmutableArray(),
                Claims=world.Claims.Where(c=>!members.Contains(c.AntId)).ToImmutableArray() };
        } else if(command.Kind is CommandKind.SetGroupParameter or CommandKind.RemoveGroupParameter or CommandKind.SetGroupPoint or CommandKind.RemoveGroupPoint) {
            if(group is null) return Fail("group_not_found");
            if(string.IsNullOrWhiteSpace(command.Key) || command.Key.Length>40 || command.Key.Any(c=>!char.IsAsciiLetterOrDigit(c) && c!='_' && c!='.' && c!='-')) return Fail("invalid_group_key");
            var next=group;
            if(command.Kind==CommandKind.SetGroupParameter) {
                if(group.Parameters.Count>=64 && !group.Parameters.ContainsKey(command.Key)) return Fail("group_parameter_capacity");
                next=next with { Parameters=next.Parameters.SetItem(command.Key,command.Value) };
            } else if(command.Kind==CommandKind.RemoveGroupParameter) next=next with { Parameters=next.Parameters.Remove(command.Key) };
            else if(command.Kind==CommandKind.SetGroupPoint) {
                if(command.Location is null || !PointValid(command.Location)) return Fail("invalid_group_point");
                if(group.Points.Count>=16 && !group.Points.ContainsKey(command.Key)) return Fail("group_point_capacity");
                next=next with { Points=next.Points.SetItem(command.Key,command.Location.Value) };
            } else next=next with { Points=next.Points.Remove(command.Key) };
            next=next with { Version=checked(group.Version+1) };
            world=world with { Groups=world.Groups.Select(g=>g.Id==next.Id?next:g).ToImmutableArray() };
        } else if(command.Kind==CommandKind.DeleteGroup) {
            if(group is null) return Fail("group_not_found");
            var members=world.Ants.Where(a=>a.GroupId==group.Id).Select(a=>a.Id).ToHashSet();
            world=world with { Groups=world.Groups.Where(g=>g.Id!=group.Id).ToImmutableArray(),
                Ants=world.Ants.Select(a=>a.GroupId==group.Id?ResetBehavior(a) with { GroupId=0,ReceivedOrder=null,QueenClaims=[] }:a).ToImmutableArray(),
                Claims=world.Claims.Where(c=>!members.Contains(c.AntId)).ToImmutableArray() };
        }
        revision++;
        return new(command.RequestId,true,"applied",revision);
    }
    private CommandResult ConfigureGroup(GameCommand c) {
        CommandResult Fail(string code)=>new(c.RequestId,false,code,revision);
        if(c.GroupConfiguration is not {} setup)return Fail("group_configuration_required");
        var old=world.Groups.FirstOrDefault(g=>g.Id==c.GroupId);
        if(c.GroupId<0 || c.GroupId>0 && old is null)return Fail("group_not_found");
        if(old is null && world.Groups.Length>=32)return Fail("group_capacity");
        bool Key(string key)=>key.Length is >0 and <=40 && key.All(ch=>char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-' or '.');
        bool Valid(Point? p)=>p is null || p.Value.X>=0 && p.Value.Y>=0 && p.Value.X<(long)world.Config.Width*1000 && p.Value.Y<(long)world.Config.Height*1000;
        if(string.IsNullOrWhiteSpace(setup.Name) || setup.Name.Length>40 || setup.Task is null || setup.Task.Length>256 || string.IsNullOrWhiteSpace(setup.Activity) || setup.Activity.Length>80 || setup.FoodFilter is null || setup.FoodFilter.Length>0 && !world.Config.Foods.Any(f=>f.Id==setup.FoodFilter) || setup.RadiusCells is <1 or >32)return Fail("invalid_group_configuration");
        if(!Valid(setup.Rally) || !Valid(setup.Objective) || !Valid(setup.Retreat) || setup.Parameters is null || setup.Parameters.Count>64 || setup.Parameters.Keys.Any(k=>!Key(k)) || setup.Points is null || setup.Points.Count>16 || setup.Points.Any(k=>!Key(k.Key) || !Valid(k.Value)))return Fail("invalid_group_fields");
        if(setup.Members.IsDefault || setup.Members.Length>100 || setup.Members.Distinct().Count()!=setup.Members.Length)return Fail("invalid_ant_ids");
        if(setup.Members.Any(id=>!world.Ants.Any(a=>a.Id==id)))return Fail("ant_not_found");
        var nest=Point.FromCells(world.Config.Width/2,world.Config.Height/2);var id=old?.Id??world.NextGroupId;
        var next=new GroupOrder(id,setup.Name,setup.Task,setup.Rally??nest,setup.Objective??nest,setup.Retreat??nest,(old?.Version??0)+1) {Activity=setup.Activity,FoodFilter=setup.FoodFilter,RadiusCells=setup.RadiusCells,Parameters=setup.Parameters,Points=setup.Points};
        var members=setup.Members.ToHashSet();var changed=world.Ants.Where(a=>members.Contains(a.Id)?a.GroupId!=id:a.GroupId==id).Select(a=>a.Id).ToHashSet();
        world=world with {
            Groups=world.Groups.Where(g=>g.Id!=id).Append(next).OrderBy(g=>g.Id).ToImmutableArray(),NextGroupId=old is null?checked(world.NextGroupId+1):world.NextGroupId,
            Ants=world.Ants.Select(a=>changed.Contains(a.Id)?ResetBehavior(a) with {GroupId=members.Contains(a.Id)?id:0,ReceivedOrder=null,QueenClaims=[]}:a).ToImmutableArray(),
            Claims=world.Claims.Where(claim=>!changed.Contains(claim.AntId)).ToImmutableArray()
        };
        revision++;return new(c.RequestId,true,"group_configured",revision,id.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

}
