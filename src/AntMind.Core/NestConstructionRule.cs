// VERSION=0.1.0
using System.Collections.Immutable;
using AntMind.Contracts;
namespace AntMind.Core;

// 唯一新增 IWorldRule：契约只冻结 core.nest-construction(Order33)。
// 推进 digging→active / 消费预留木料 / BFS 重算只在这里发生；DSL 与命令层只写意图和预留。
// 不读 Tick/种子/Random/DateTime/Guid，不写 Created/Admit/FirstAdmissionPoints/DevelopmentValue。
public sealed class NestConstructionRule : IWorldRule {
    public string Id => "core.nest-construction";
    public int Order => 33;

    public WorldState Apply(WorldState w) {
        var nest = w.Nest;
        if (nest is null || nest.Cells.IsDefault || nest.Cells.Length != NestLayout.CellCount) return w;
        var cells = nest.Cells.ToBuilder();
        var occupied = new HashSet<int>(); // 按地下 chamberIndex 的局部 worker slot，不碰地表 BuildingPlacement。
        var completed = false;
        var woodConsumed = 0L;

        foreach (var a0 in w.Ants.Where(a => a.ChamberX >= 0 && a.ChamberAction > 0)) {
            var a = a0;
            var index = a.ChamberY * NestLayout.GridWidth + a.ChamberX;
            var error =
                a.ChamberX < 0 || a.ChamberX >= NestLayout.GridWidth ||
                a.ChamberY < 0 || a.ChamberY >= NestLayout.GridHeight ? "chamber_out_of_range" :
                a.Species != AntSpeciesIds.Builder ? "builder_required" :
                a.LastDamageTick == w.Tick ? "work_interrupted" :
                !occupied.Add(index) ? "worker_slot_busy" :
                cells[index].Phase != "digging" ? "no_chamber_work" : null;
            if (error is not null) {
                a = a with { ChamberX = -1, ChamberY = -1, ChamberTypeCode = 0, ChamberAction = 0,
                    Intent = AntAction.Wait, Target = null, ActionStatus = ActionStatus.Failed, ActionResult = error };
                w = w with { Objects = w.Objects.Put(a) };
                continue;
            }
            var cell = cells[index];
            var workValue = checked(w.Config.AntType(a.Species).WorkRate *
                w.Config.Veterancy.WorkPercent[a.Level - 1] *
                InstitutionMechanics.LaborWeight(w, a.Species) / 100 + a.WorkRateRemainder);
            var rate = workValue / 100;
            a = a with { WorkRateRemainder = workValue % 100 };
            var remainingWork = Math.Max(0, cell.WorkTarget - cell.ConstructionWork);
            w = AntProgression.Record(w, a.Id, Math.Min(rate, remainingWork), "chamber", effectiveWork: true);
            cell = cell with { ConstructionWork = Math.Min(cell.WorkTarget, cell.ConstructionWork + rate) };
            if (cell.ConstructionWork >= cell.WorkTarget) {
                // 完工：WoodReserved 从实物台账出账一次；此时才写 ConsumedItems[wood]，绝不碰 Created/Admit/分数。
                w = Economy.Consumed(w, FoodTypes.Wood, cell.WoodReserved);
                woodConsumed += cell.WoodReserved;
                cell = cell with { Phase = "active", WoodReserved = 0, ConstructionWork = cell.WorkTarget,
                    Discovered = true };
                completed = true;
            }
            cells[index] = cell;
            var ticks = (a.WorkTicksRemaining == 0 ? 10 : a.WorkTicksRemaining) - 1;
            a = a with { WorkTicksRemaining = ticks, ChamberX = -1, ChamberY = -1, ChamberTypeCode = 0,
                ChamberAction = 0, Intent = AntAction.Wait, Target = null,
                ActionStatus = ticks > 0 ? ActionStatus.Running : ActionStatus.Succeeded,
                ActionResult = "chamber_work" };
            w = w with { Objects = w.Objects.Put(a) };
        }
        if (!completed && woodConsumed == 0) {
            // 未完工的 ConstructionWork 仍需写回；不触发 Recompute（BFS 只在 phase/level 变化时重算）。
            var progressed = (nest with { Cells = cells.ToImmutable() });
            return w with { Nest = progressed };
        }
        var rebuilt = (nest with { Cells = cells.ToImmutable() }).Recompute(w.Config.NestLayout);
        var rebuiltCells = rebuilt.Cells;
        var observed = Enumerable.Range(0, NestLayout.CellCount)
            .Where(i => rebuiltCells[i].Discovered).ToImmutableArray();
        return w with { Nest = rebuilt, ObservedChambers = observed };
    }
}
