using System.Collections.Immutable;
using AntMind.Contracts;
using AntMind.Core;
using AntMind.Programs;
using AntMind.Runtime;

internal static class MilestoneCheck {
    public static void Run(Action<bool,string> check) {
        var rule = new MilestoneRule();
        var config = new WorldConfig(InitialResourceNodes:0) { Milestones = [
            new("child","挑战","父目标后开放","delivered",10,["parent"],20),
            new("parent","起步","送回一份","delivered",1)
        ] };
        var world = rule.Apply(Simulation.Create(config,42));
        check(MilestoneRule.Snapshot(world).Single(x=>x.Id=="child").Status=="locked", "challenge remains locked before prerequisites complete");
        var unlocked = rule.Apply(world with { Tick=10, Delivered=1 });
        check(unlocked.MilestoneProgress["parent"].CompletedTick==10 && unlocked.MilestoneProgress["child"].StartedTick==10,
            "prerequisite completion starts the challenge clock regardless of configuration order");
        var done = rule.Apply(unlocked with { Tick=30, Delivered=10 });
        check(done.MilestoneProgress["child"].CompletedTick==30, "challenge can complete on its exact simulation deadline");
        var failed = rule.Apply(unlocked with { Tick=31, Delivered=10 });
        check(failed.MilestoneProgress["child"].Failed && failed.MilestoneProgress["child"].CompletedTick is null,
            "late progress cannot complete an expired challenge");
        check(rule.Apply(failed with { Tick=40, Delivered=100 }).MilestoneProgress["child"].Failed &&
            rule.Apply(done with { Tick=40, Delivered=0 }).MilestoneProgress["child"].CompletedTick==30,
            "completion and failure are latched once without repeat awards");
        check(done.Inventory==world.Inventory && done.Generated==world.Generated && done.RandomState==world.RandomState,
            "milestones do not create food, alter inventory or consume random numbers");
        var foodWorld = world with { Resources = [
            new ResourceView(1,Point.FromCells(1,1),1,0) { IsDiscovered=true, Origin=ResourceOrigin.Dropped },
            new ResourceView(2,Point.FromCells(2,2),1,0),
            new ResourceView(3,Point.FromCells(3,3),1,0) { IsDiscovered=true }
        ] };
        check(MilestoneRule.Current(foodWorld,"discovered_food")==1, "exploration objectives exclude hidden food and dropped-pile farming");
        foreach (var invalid in new[] {
            ImmutableArray.Create(new MilestoneDefinition("a","A","test","delivered",1,["a"])),
            ImmutableArray.Create(new MilestoneDefinition("a","A","test","delivered",1,["missing"])),
            ImmutableArray.Create(new MilestoneDefinition("a","A","test","hidden_food",1))
        }) {
            var rejected=false;
            try { MilestoneRule.Validate(invalid); } catch(ArgumentException) { rejected=true; }
            check(rejected,"invalid or secret milestone dependencies/metrics are rejected");
        }
        check(DefaultProgramSources.Scout==File.ReadAllText("content/programs/scout-explorer.ant") &&
            DefaultProgramSources.Hauler==File.ReadAllText("content/programs/hauler-reliable.ant"),
            "new games embed the verified strategies without requiring manual resubmission");
        VerifyPausedUpgrade(check).GetAwaiter().GetResult();
    }
    private static async Task VerifyPausedUpgrade(Action<bool,string> check) {
        await using var session = new GameSession(new WorldConfig(InitialStock:1000) {
            Milestones=[new("network","网络","test","network_level",2)]
        },42);
        var before=await session.ObserveAsync();
        await session.ExecuteAsync(new(Guid.NewGuid(),before.Revision,CommandKind.UpgradeNetwork,Reason:"milestone check"));
        var after=await session.ObserveAsync();
        check(after.Paused && after.Tick==0 && after.Milestones.Single().Status=="completed" && after.Milestones.Single().CompletedTick==0,
            "paused management upgrades immediately update milestones without advancing ticks");
    }
}
