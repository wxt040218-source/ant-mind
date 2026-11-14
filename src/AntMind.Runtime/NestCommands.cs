// VERSION=0.1.0
using System.Collections.Immutable;
using AntMind.Contracts;
using AntMind.Core;
namespace AntMind.Runtime;

// 地下巢穴四动作的唯一状态迁移入口。纯整数格索引，不进 Point / world.Objects / EntityDatabase。
// 木料只走 FoodStore.Take / Put，完工才由 Order33 走 Economy.Consumed；此处绝不碰 Created / Admit / 分数。
public static class NestTransitions {
    public sealed record Outcome(WorldState World, string Code);

    private static bool AdjacentConnected(ImmutableArray<bool> mask, int index) {
        var x = index % NestLayout.GridWidth; var y = index / NestLayout.GridWidth;
        return (y > 0 && mask[(y - 1) * NestLayout.GridWidth + x]) ||
               (x < NestLayout.GridWidth - 1 && mask[y * NestLayout.GridWidth + x + 1]) ||
               (y < NestLayout.GridHeight - 1 && mask[(y + 1) * NestLayout.GridWidth + x]) ||
               (x > 0 && mask[y * NestLayout.GridWidth + x - 1]);
    }
    // 已 discovered 的格 index 升序、去重，是 ObservedChambers 的唯一定义。
    public static ImmutableArray<int> RebuildObserved(ImmutableArray<ChamberCell> cells) =>
        Enumerable.Range(0, NestLayout.CellCount).Where(i => cells[i].Discovered).ToImmutableArray();
    private static ImmutableArray<ChamberCell> With(ImmutableArray<ChamberCell> cells, int index, ChamberCell cell) =>
        cells.SetItem(index, cell);

    public static Outcome Apply(WorldState world, int chamberX, int chamberY, string chamberType, CommandKind kind) {
        if (chamberX < 0 || chamberX >= NestLayout.GridWidth || chamberY < 0 || chamberY >= NestLayout.GridHeight)
            return new(world, $"Chamber cell ({chamberX},{chamberY}) is unavailable.");
        var index = chamberY * NestLayout.GridWidth + chamberX;
        var nest = world.Nest;
        if (nest is null || nest.Cells.IsDefault || nest.Cells.Length != NestLayout.CellCount ||
            nest.ConnectedMask.IsDefault || nest.ConnectedMask.Length != NestLayout.CellCount)
            return new(world, "Invalid nest layout.");
        var config = world.Config.NestLayout;
        var cell = nest.Cells[index];
        var store = world.Stock;
        var cells = nest.Cells;
        var next = cell;

        switch (kind) {
            case CommandKind.PlanChamber: {
                if (!ChamberCatalog.IsBuildable(chamberType))
                    return new(world, $"Invalid chamber type '{chamberType}'.");
                // 勘误 v0.1.1：初始仅 core discovered，若死守「plan 必须已 discovered」则全盘无格可规划。
                // 放宽为「已 discovered 或与连通格 4 邻接」，登记蓝图同时把该格标记为已勘察。
                var visible = cell.Discovered || AdjacentConnected(nest.ConnectedMask, index);
                if (cell.ChamberType != "empty" || cell.Phase != "planned" || !visible)
                    return new(world, $"Chamber cell ({chamberX},{chamberY}) is unavailable.");
                next = cell with { ChamberType = chamberType, Phase = "planned", Level = 1,
                    ConstructionWork = 0, WorkTarget = 0, WoodReserved = 0, Discovered = true };
                break;
            }
            case CommandKind.DigChamber: {
                if (cell.Phase != "planned" || !ChamberCatalog.IsBuildable(cell.ChamberType))
                    return new(world, $"Chamber cell ({chamberX},{chamberY}) is not planned.");
                if (!AdjacentConnected(nest.ConnectedMask, index))
                    return new(world, $"Chamber cell ({chamberX},{chamberY}) is unavailable.");
                var (wood, work) = ChamberCatalog.Cost(cell.ChamberType, 1, config);
                if (store.Get(FoodTypes.Wood) < wood) return new(world, "Insufficient wood for chamber.");
                var (taken, credits) = store.Take(FoodTypes.Wood, wood);
                if (!credits.IsDefaultOrEmpty) return new(world, "invalid_wood_credit");
                store = taken;
                next = cell with { Phase = "digging", WorkTarget = work, ConstructionWork = 0, WoodReserved = wood };
                break;
            }
            case CommandKind.UpgradeChamber: {
                if (!nest.ConnectedMask[index]) return new(world, $"Chamber at ({chamberX},{chamberY}) is not connected.");
                if (cell.Phase != "active" || !ChamberCatalog.IsUpgradeable(cell.ChamberType))
                    return new(world, $"Chamber cell ({chamberX},{chamberY}) is unavailable.");
                if (cell.Level >= config.MaxLevel) return new(world, $"Chamber at ({chamberX},{chamberY}) is at max level.");
                var targetLevel = cell.Level + 1;
                var (wood, work) = ChamberCatalog.Cost(cell.ChamberType, targetLevel, config);
                if (store.Get(FoodTypes.Wood) < wood) return new(world, "Insufficient wood for chamber.");
                var (taken, credits) = store.Take(FoodTypes.Wood, wood);
                if (!credits.IsDefaultOrEmpty) return new(world, "invalid_wood_credit");
                store = taken;
                next = cell with { Phase = "digging", Level = targetLevel, WorkTarget = work,
                    ConstructionWork = 0, WoodReserved = wood };
                break;
            }
            case CommandKind.CancelChamber: {
                if (cell.Phase == "active") return new(world, $"Cannot cancel active chamber at ({chamberX},{chamberY}).");
                if (cell.Phase is not ("planned" or "digging"))
                    return new(world, $"Chamber cell ({chamberX},{chamberY}) is unavailable.");
                // wood 的 CreditLot 恒空（value=0 / ItemCredit.Raw 只对 fruit/meat 生成），只退未消耗 WoodReserved。
                if (cell.WoodReserved > 0) store = store.Put(FoodTypes.Wood, cell.WoodReserved, []);
                next = cell with { ChamberType = "empty", Phase = "planned", Level = 1,
                    ConstructionWork = 0, WorkTarget = 0, WoodReserved = 0 };
                break;
            }
            default: return new(world, "invalid_command");
        }

        cells = With(cells, index, next);
        // 纯函数即时重算：plan/dig 结果幂等不变，upgrade 的 active→digging 与 cancel 会即时修正连通。
        var rebuilt = (nest with { Cells = cells }).Recompute(config);
        var updated = world with { Nest = rebuilt, Stock = store, ObservedChambers = RebuildObserved(cells) };
        return new(updated, "applied");
    }
}

public sealed partial class GameSession {
    private CommandResult ApplyNestCommand(GameCommand c) {
        var outcome = NestTransitions.Apply(world, c.ChamberX, c.ChamberY, c.ChamberType, c.Kind);
        if (outcome.Code != "applied") return new(c.RequestId, false, outcome.Code, revision);
        world = outcome.World; revision++;
        return new(c.RequestId, true, "applied", revision);
    }
}
