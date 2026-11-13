# 蚁智内政三件套 · 野怪生态（CreatureKind）分册契约

> 版本 v0.1.0 ｜ 契约冻结 ｜ 2026-11-13

> 本文件只冻结契约、不含实现；实现按 S1/S2/S3 落地。

本分册从属于 [`colony-era-contract-0.1.0.md`](colony-era-contract-0.1.0.md)（下称「总纲」）。总纲 §2 四条不变式、§3 Order 表、§5 版本表、§7 scenario pack 字段表、§8 验收口径对本分册全部生效。落地阶段 **S3**（依赖 S1 的 save5→6 迁移骨架与 S2 的 `NestLayout`）。

## 1 · 定位与硬边界

- `CreatureKind` 是**封闭白名单**，复用现成 `EnemyView` / `EnemySpawnRule`(`core.enemy-spawn`, Order 0) / `EnemyMovementRule`(`core.enemy-move`, Order 21) / `CombatRule`(`core.combat`, Order 25) / `CombatRules.AddFood` 尸体链路，**不另起对象体系、不改 `Pipes.cs`**。`EnemyView` 继续是 `IWorldObject`（`Protocol.cs:108-119`，`Kind=>"enemy"`），进 `world.Objects` / `EntityDatabase`，不新增 `IWorldObject` 派生类型。
- **独立子种子**：`WorldState.CreatureSeed`（`ulong`，总纲 §2.1/§5）是野怪侧唯一随机源。S3 起 `core.enemy-spawn`(0) 必须从 `world.RandomState` **改挂 `CreatureSeed`** —— 现状 `CombatRules.cs:28-47` 里 `var random=world.RandomState; side/offset/food` 三处 `RandomSource.Next(ref random)` 是 S3 必须清掉的债务；主 RNG 此后只服务于资源刷新、`SourceGrowth`、`ProductionFactory`，绝不因野怪种类/数量变化而偏移。
- **迁徙群是纯函数**：周期迁徙群的成群数量与位置是 `f(tick, scenarioSeed)` 的**步数纯函数**，既不读 `RandomState`、也不读 `CreatureSeed`（总纲 §2.1），保证可预测、可写进事件流，专门考「看事件流提前调度」。
- **掉落守恒**：死亡掉落统一走 `AddFood(world, pos, amount, ResourceOrigin.Carcass, carcassFoodType, enemyId)` → `Economy.Created` + `Generated += amount` + `ItemCredit.Split` 的 `CreditLot` 随物转移 + `Properties["source_enemy_id"]`；**一次死亡只调一次 `AddFood`**，靠 `EnemyView.CorpseFoodId`（已有 `int?`，`:113`）非空做幂等标记，防重试/回滚重复生成（总纲 `contract/conservation/no-duplicate-carcass`）。
- **局部感知**：未发现野怪对 player 与 Agent 同等隐藏，**复用 `EnemyView` 既有** `IsDiscovered` / `LastKnownPosition` / `LastKnownHealth` / `LastSeenTick` 四字段投影（不新增 `ObservedCreatures` 字段，与总纲 §2.4 一致）；`inspect_object`(`Mcp/Program.cs:93`) 对未发现野怪返回 `object_not_found`，与不存在 ID 同表现；`Snapshot` 不返回 `world.Enemies` 全量。
- **坐标系**：野怪全在地表 `Point`（每格 1000 整数单位）；堵路虫的 `BlockedCells` 是**地表格索引**，与巢穴分册的地下 `corridor` 不是同一坐标系，不互相换算（巢穴分册 §1）。
- **不直接给分**：野怪存在/刷新/被打都不写 `FirstAdmissionPoints`。收益仍只认 `core.interaction`(Order 30) 首次入巢；尸体肉、蚜虫放牧产粮都是**新食物**，入巢时按 `ItemCredit.Points(credits)` 计一次，不是「打怪得分」（总纲 §2.3）。

## 2 · `CreatureKind` 白名单（逐种冻结）

`CreatureKind` 用 string 字面量存档 / DSL / MCP / `content/world.json` 统一，C# 侧常量类同名 PascalCase。五种**全部可被 `AntAction.Attack` 击杀**（击杀即失去该个体的后续行为收益，这是策略取舍，不设「不可击杀」特例）。

| `CreatureKind` | 中文 | 行为 | 刷新函数 | 视野 / 速度 / 伤害 | 掉落 | 咬合的规则 |
|---|---|---|---|---|---|---|
| `predator_beetle` | 掠食甲虫（现有） | 直冲蚁后或视野内最近蚂蚁，一根筋冲巢 | `core.enemy-spawn`(0)：`world.Tick % SpawnIntervalTicks == 0` 且 `Enemies.Length < EnemyCap` 时，从 `CreatureSeed` 取 `side/offset/food`，四边生成；`food = CorpseMin + Next % (CorpseMax-CorpseMin+1)` | `EnemyVisionCells=8`、`EnemySpeed=70`、`EnemyDamage=2`、`AttackCooldownTicks=20`、`EnemyHealth=18` | `CarcassFoodType="meat"`，`FoodAmount` 由刷新时确定 | `core.enemy-spawn`(0) / `core.enemy-move`(21) / `core.combat`(25) / `core.local-observation`(5) / `core.discovery`(45) |
| `pantry_moth` | 蛀仓虫 | **专啃粮仓/仓库，不打蚂蚁**：目标为地下 `granary`（`NestLayout`）或地表 `cache`/`workshop` 的 `BuildingView.Store`；啃食是 `FoodStore.Take` 真实扣料 + `Economy.Consumed`，不是凭空扣分；逼分兵守仓 | 同 `predator_beetle` 的周期刷新，但 `side/offset` 用 `CreatureSeed` 的独立流；数量受 `EnemyCap` 约束 | 视野 `0`（不索敌）、速度 `70`、对蚁伤害 `0`、`EnemyHealth=12` | `CarcassFoodType="fiber"`，`FoodAmount ∈ [1,2]` | `core.creature-ecology`(**1**) 结算啃仓 / `core.enemy-move`(21) 目标改粮仓 / `core.combat`(25) 可被击杀 / `core.production-actions`(30) 库存守恒 / `core.local-observation`(5) |
| `road_blocker` | 堵路虫 | **占通道格拖慢运输**：占住地表若干格写 `WorldState.BlockedCells`，经过的 `core.move`(20) 按固定点减速；不主动打蚂蚁、不啃仓；逼规划冗余路径 | 同周期刷新，`CreatureSeed` 独立流；`BlockCells` 由刷新时确定 | 视野 `0`、速度 `0`（不动）、伤害 `0`、`EnemyHealth=24`（耐打，逼绕路而非硬清） | `CarcassFoodType="fiber"`，`FoodAmount ∈ [1,3]` | `core.creature-ecology`(**1**) 写 `BlockedCells` / `core.move`(20) 读减速 / `core.combat`(25) 击杀后清 `BlockedCells` / `core.enemy-move`(21) 不移动 |
| `aphid` | 蚜虫 | **可放牧产额外粮但要护卫**：活体每 `GrazeIntervalTicks` 产 `fruit`，是**新食物生成**（`ResourceOrigin.Natural` + `Economy.Created` + `Generated += qty` + `ItemCredit.Raw(FoodTypes.Fruit,qty)`），入巢计一次分；被击杀则停止产粮但掉肉，属长线投资 | 同周期刷新，`CreatureSeed` 独立流；`HerdId=0` | 视野 `0`、速度 `0`、伤害 `0`、`EnemyHealth=8`（脆，需护卫） | 活体产 `fruit`（`Natural`）；死亡 `CarcassFoodType="meat"`，`FoodAmount=1` | `core.creature-ecology`(**1**) 结算放牧产粮 / `core.enemy-move`(21) 不移动 / `core.combat`(25) 护卫与击杀 / `core.interaction`(30) 入巢计分 / `core.discovery`(45) |
| `migration_swarm` | 周期迁徙群 | **按固定步数成群来**：`predator_beetle` 的成群实例，`HerdId>0` 标记同群；成群直线推进，可预测，逼看事件流提前调度 | **`f(tick, scenarioSeed)` 步数纯函数**：`tick % MigrationIntervalTicks == 0` 时按 `HerdSize` 成群生成，位置由 `tick` 与 `scenarioSeed` 整数哈希决定，**不读任何 RNG**（含 `CreatureSeed`）；不占 `EnemyCap` 的随机刷新名额，单独按 `HerdCap` 限流 | 同 `predator_beetle`（`EnemyVisionCells=8` 等），`EnemyHealth=18` | `CarcassFoodType="meat"`，`FoodAmount` 由 `f(tick,scenarioSeed)` 决定（纯函数） | `core.enemy-spawn`(0) 纯函数分支 / `core.enemy-move`(21) 成群推进 / `core.combat`(25) / `core.queen-communication`(15) 提前告警 / `core.local-observation`(5) |

- **白名单封闭性**：任意其它 `CreatureKind` 值在 `content/world.json` 校验、`SaveMigration` 升级、`EnemyView` 反序列化、`SavedGames.Validate` 四处一律拒绝；错误串 `Invalid creature kind.`。
- **刷新参数**进 `content/world.json` 的新 `creatures` 段（总纲 §5）：`creatureSeedDerivation`（只用总纲 §2.1 常量，不另配）、`spawnIntervalTicks=1200`、`enemyCap=3`（沿用 `CombatConfig`）、`migrationIntervalTicks`、`herdSize`、`herdCap`、`grazeIntervalTicks`、`grazeQuantity`、`blockedCellSpeedBasis=50`（减速到 50%）、每种 `health/damage/speed/vision/carcassFoodType/corpseMin/corpseMax`。全部整数，无浮点、无隐藏随机。
- `CombatConfig`（`CombatRules.cs:5-23`）现有字段保持兼容，`Enabled/SpawnIntervalTicks/EnemyCap/EnemyHealth/EnemyDamage/EnemySpeed/EnemyVisionCells/AttackCooldownTicks/QueenHealth/CorpseMin/CorpseMax` 作为 `predator_beetle` 的默认来源，新种类走 `creatures` 段覆盖；`Validate()` 失败仍抛 `Invalid combat configuration.`。

## 3 · 独立子种子方案与 `AddFood` 改造点

- `CreatureSeed` 由 `Simulation.Create(WorldConfig, ulong seed)` 用总纲 §2.1 的固定常量 `0x9E3779B97F4A7C15UL` 一次性派生；旧档由 `SaveMigration` 第 1 步从 `save.World.RandomState` 确定性补出（总纲 §5），S3 直接读，**不掷新随机数**。
- `core.enemy-spawn`(0) 改造（**不改 Order、不改 Id、不改 `Pipes.cs`**）：把 `var random = world.RandomState;` 换成 `var random = world.CreatureSeed;`，末尾 `world with { ..., CreatureSeed = random }`；`side/offset/food` 三次 `RandomSource.Next(ref random)` 顺序不变，保证同种子同序列。按 `CreatureKind` 分派到各自刷新流（每种取独立偏移，避免互相抢序列）；`migration_swarm` 分支完全跳过 RNG。
- `CombatRules.AddFood` 扩展（`:121-135`）：现状 `if(origin==ResourceOrigin.Carcass) world=Economy.Created(world,foodType,amount);`。S3 改为 **`Carcass` 与 `Natural` 都调 `Economy.Created` 且 `Generated += amount`**（对齐 `HarvestRules.cs:25` 的自然生长语义），`Dropped` 仍不调（转移不算生成）；`credits ?? ItemCredit.Raw(foodType, quantity)` 保持不变 —— 注意 `ItemCredit.IsRawFood` 只认 `fruit` / `meat`（`Production.cs:8-9`），故 `fiber` 尸体 `credits=[]`、不带 `CreditLot`、入巢**不计分**，只作木料/纤维用途，这正是「不重复计分」的一部分。
- **掉落守恒链**（逐字对齐 `CombatRules.cs:121-135`）：`AddFood` 以 `world.Config.ResourceMax` 拆批 → `ItemCredit.Split` → 生成 `ResourceView`（`Credits=part.Taken, CreditTracked=true, FoodType, Origin, IsDiscovered=true, DiscoveredTick, LastObservedAmount, LastObservedTick`）→ `Properties` 加 `source_enemy_id` → `Economy.Created` → `Generated`。`core.combat`(25) 死亡分支写 `enemy with { CorpseFoodId = corpseId }` 并 `EnemiesDefeated+1`；再次遇到同一 `CorpseFoodId != null` 的敌人跳过 `AddFood`。
- **不重复计分**：同一 `CreditLot` 入巢时经 `Economy.Admit` → `ItemCredit.Points(credits)` 计一次后不再携带；蚂蚁携带物用 `CargoCredits` / `ItemCredit.Split(...).Left`（`ColonyRules.cs:27` 既有语义）继续随物转移；被 `pantry_moth` 啃掉的食物走 `Economy.Consumed` 且已从 `FoodStore` 移除，不会再次入巢。Checks 断言 `creature/conservation/no-duplicate-carcass` 与总纲 `contract/conservation/no-duplicate-carcass`。

## 4 · `EnemyView` 扩展字段（冻结，全部带默认值保持旧构造兼容）

现有主构造 `EnemyView(int Id, Point Position, int Health, int MaxHealth, int FoodAmount)`（`Protocol.cs:108`）**不改签名**；以下全部是 `init` 默认字段，旧档由 SaveMigration 补值（总纲 §5 第 5 步）：

| JSON 字段 | C# 属性 | 类型 / 默认值 | 说明 |
|---|---|---|---|
| `creature_kind` | `CreatureKind` | string = `"predator_beetle"` | §2 白名单 |
| `carcass_food_type` | `CarcassFoodType` | string = `"meat"` | `FoodTypes` 常量；`fiber` 不带 credit、不计分 |
| `block_cells` | `BlockCells` | int = `0` | 堵路虫占地表格数；死亡/移除时清 `BlockedCells` |
| `herd_id` | `HerdId` | int = `0` | `0`=非迁徙群；`>0` 同群共享 |
| `last_graze_tick` | `LastGrazeTick` | long = `-1000000` | 蚜虫上次放牧 Tick，配合 `GrazeIntervalTicks` 纯 Tick 判定（不读 RNG） |
| `target_chamber_x` / `target_chamber_y` | `TargetChamberX` / `TargetChamberY` | int = `-1` | 蛀仓虫目标地下 `granary` 格；`-1`=无；**不进 Snapshot 投影**（局部感知） |

**复用既有字段不新增**：`IsDiscovered` / `LastKnownPosition` / `LastKnownHealth` / `LastSeenTick` / `CorpseFoodId` / `CreatedTick` / `LastAttackTick` / `Properties`（`Protocol.cs:108-119`）。`Name` 按 `CreatureKind` 显示中文名，不再恒为「掠食甲虫」。

## 5 · 现有规则改造点（不改 `Pipes.cs`，Id/Order 逐字沿用总纲 §3）

| 规则 Id | Order | 改造 |
|---|---|---|
| `core.enemy-spawn` | 0 | RNG 改挂 `CreatureSeed`；按 §2 白名单分派刷新；`migration_swarm` 走 `f(tick,scenarioSeed)` 纯函数分支 |
| `core.creature-ecology` | **1** | **新增**（总纲 §3）：结算 `pantry_moth` 啃仓、`aphid` 放牧产粮、`road_blocker` 写/清 `BlockedCells`；在 `core.enemy-spawn`(0) 之后、`core.source-growth`(2) 之前，保证同 Tick `core.move`(20) 能读到减速格 |
| `core.local-observation` | 5 | 按 `IsDiscovered` 投影野怪；未发现返回 `object_not_found`；不投影 `TargetChamberX/Y`、不返回 `Enemies` 全量 |
| `core.queen-communication` | 15 | `migration_swarm` 纯函数可预测 → 提前写告警事件（沿用 `docs/AGENT_EVENTS.md` 既有 `enemy_seen` / `enemy_entered_zone`，不新增事件种类）；与制度分册 `defense_policy` 的 `ThreatSwitchPercent` 同一输入 |
| `core.move` | 20 | 读 `BlockedCells`，被占格按 `blockedCellSpeedBasis=50` 固定点减速；不改 `PointMath` 纯整数几何 |
| `core.enemy-move` | 21 | 按 `CreatureKind` 分派：`predator_beetle`/`migration_swarm` 冲巢或索敌、`pantry_moth` 趋粮仓、`road_blocker`/`aphid` 不移动 |
| `core.combat` | 25 | 各 kind 掉血；死亡统一 `AddFood(Carcass)` + `CorpseFoodId` 幂等；兵蚁伤害取 `SoldierAttackBasis`（巢穴分册 §4）与 `AntProgression.damagePercent [100,125,150]` 的较大者，不叠乘两次 |
| `core.production-actions` | 30 | `pantry_moth` 啃掉的粮已 `Economy.Consumed`，加工槽读到的 `Store` 是扣后真值 |
| `core.interaction` | 30 | 仍是**唯一** `FirstAdmissionPoints` 计分点；`fruit`/`meat` 尸体按 `CreditLot` 计一次，`fiber` 不计 |
| `core.discovery` | 45 | 发现野怪写 `IsDiscovered` / `LastSeenTick` / `LastKnownPosition` / `LastKnownHealth` |
| `core.nest-construction` | 33 | **不改**：地下施工不受地表 `BlockedCells` 影响（坐标系隔离） |

制度 `InstitutionParameters`（S1）与 `NestLayout`（S2）的结构**不因本分册改动**；本分册只消费 `SoldierAttackBasis` / `granary` 目标 / `ThreatSwitchPercent`，不重定义。

## 6 · 局部感知与存档字段

- 可见性：`core.local-observation`(5) 是唯一投影口；`Snapshot` 只含已发现野怪的 `LastKnown*` 摘要，`inspect_object`(:93) 对未发现返回 `object_not_found`；`observe` / `observe_summary` 与 Agent DSL 的 `nearest_enemy_*`（`AntDsl.cs:499` 既有 Builtin）取同一可见集合，断言总纲 `contract/visibility/player-equals-agent`、`contract/visibility/undiscovered-creature-not-found`。
- **S3 不新增命令面**：野怪生态不新增 `CommandKind` 成员、不给 `GameCommand` 加写字段、不给 `Mcp/Program.cs:107` 的 `command` 加写参数、不新增只读工具；MCP 工具总数保持 S2 后的 **17**（`inspect_institution` / `inspect_chamber` 已分别在 S1/S2 增加）。野怪信息只经既有 `inspect_object` / `observe` / `observe_summary` 与事件投影（`enemy_seen` / `enemy_entered_zone` / `enemy_defeated`）暴露；总纲 §4 追加的六个 `CommandKind` 成员全部来自 S1 制度与 S2 巢穴，S3 零新增。
- 存档新增（全部属总纲 §5 同一跳 `schemaVersion 15→16` / `SavedGames.Format 5→6` / `Protocol.Version 18→19`，**S3 不另开第二级**）：
  - `WorldState.CreatureSeed`（`ulong`，§5 第 1 步派生）
  - `WorldState.BlockedCells`（`ImmutableArray<int>` 地表格索引，§5 第 4 步默认 `[]`）
  - `EnemyView` 的 §4 扩展字段（7 个标量 / 6 行表格，§5 第 5 步默认 `predator_beetle` / `meat` / `0` / `0` / `-1000000` / `-1,-1`）
- `SavedGames.Validate`(:35) 增补：`CreatureKind` 在 §2 白名单内、`CarcassFoodType` 在 `FoodTypes` 内、`BlockCells ≥ 0`、`HerdId ≥ 0`、`BlockedCells` 索引在地表 `[0, width*height)`、`CorpseFoodId` 非空时对应资源确实存在（防半截掉落）；失败走字段级异常，不混用 `Unsupported save format.`。
- `NormalizeLegacyJson`(:49) 不为野怪新增分支；`SaveMigration.Upgrade` 只在总纲 §5 那一级里补默认值，**原档不落盘改写**（`SaveMigration.cs:8` 注释不变）。兼容矩阵 `wire18/content15/save5`（含 wire12–17、save1–4）→ `wire19/content16/save6`。

## 7 · Checks 断言（并入现有单一计数器）

`tools/AntMind.Checks/Program.cs:9` 的 `Check(bool,label)` 与 `:502` 单一 `All {checks} architectural checks passed.` 不变。本分册新增 label：

```
creature/whitelist/closed-five-kinds
creature/subseed/enemy-spawn-uses-creatureseed
creature/subseed/main-randomstate-untouched
creature/subseed/migration-is-pure-function-of-tick
creature/order/core-creature-ecology-1
creature/spawn/carcass-once-only
creature/conservation/no-duplicate-carcass
creature/conservation/natural-also-created
creature/conservation/fiber-no-credit-no-points
creature/pantry/store-take-is-real
creature/roadblock/blocked-cells-cleared-on-death
creature/aphid/graze-tick-pure
creature/visibility/undiscovered-not-found
creature/visibility/target-chamber-not-in-snapshot
creature/migration/save5-enemy-defaults
creature/migration/original-bytes-untouched
```

复用总纲断言：`contract/determinism/independent-subseeds`、`contract/determinism/migration-is-pure-function-of-tick`、`contract/conservation/single-ledger`、`contract/conservation/credit-lot-follows-item`、`contract/no-farming/build-is-investment`、`contract/visibility/player-equals-agent`。

## 8 · 锚点标定题（烂 / 中 / 优 三档齐）

题名 `creature-event-scheduling`，`scenarioId` 固定不带日期，`contractVersion="0.1.0"`，`axes=["creature"]`，`layers=["baseline","+creature","all"]`，`ticks` 同跑 300 / 1200 两档（对照历史「300 步全场 0」「1200 步官方默认 8 分」），`seed` / `promptSha256` / `slot` 三档**完全相同**，只换 `strategyId`（同题对照，总纲 §7）。`migration_swarm` 的 `f(tick, scenarioSeed)` 使事件流可预测，是本题「能不能拉开分」的核心。

| 档 | `strategyId` | 参考策略 | 预期 |
|---|---|---|---|
| 烂 | `poor` | 无视事件流、不读 `nearest_enemy_*`，蚂蚁全程搬运；迁徙群来时无分兵守巢，蚜虫被顺手打死、粮仓被蛀空 | 断粮/丢仓/无长线收益，L3 写对率与得分垫底（含 300 步 0 分风险） |
| 中 | `medium` | 只读 `nearest_enemy_*` 做被动反击，迁徙群来了才临时调兵；不护卫蚜虫、不绕开 `BlockedCells` | 能守住巢，但运输被拖慢、放牧收益为 0，中段 |
| 优 | `good` | 读事件流按 `migrationIntervalTicks` **提前**调度（配合 `defense_policy`），分兵守 `granary` 防蛀仓、给 `aphid` 配护卫吃长线 `fruit`、用冗余路径绕 `BlockedCells`，并在 `CorpseFoodId` 出现后不重复打同一尸体 | 三档中 L3 写对率与 `FirstAdmissionPoints` 最高（收益仍来自首次入巢，野怪只改变吞吐与风险） |

**怎么判一道题能不能用（作废判据，逐字沿用总纲 §8.5）**：三档在同一 `scenarioId` 上的 L3 写对率差值低于该题预设最小可分阈值，或三档 `FirstAdmissionPoints` 全部相同（含同为 0）→ **该题直接作废**，不得靠调分母、改计分口径或换题目难度来「救」。L5 开放题单列，不并入 L3；能力结论须 `grading.crossPipelineRecheck=true` 跨管道独立复核后才可写入文档，并留存原始输入与完整方法（总纲 §7）。

## 9 · 与其它分册的边界

- `nest-layout-contract-0.1.0.md`：`pantry_moth` 的地下目标来自 `NestLayout` 的 `granary` 格（§2.2），但野怪不写 `Nest.Cells`、不读 `NestSeed`；`BlockedCells` 是地表格，与地下 `corridor` 坐标系隔离；兵营 `SoldierAttackBasis` 在此被 `core.combat`(25) 消费。
- `institution-contract-0.1.0.md`：`defense_policy` 的 `ThreatSwitchPercent` 用同一敌情可见投影，制度不定义 `CreatureKind`、不改 `EnemyView`；`admission_policy` 改入巢顺序，不改 `CreditLot` 只计一次。
- 冲突裁决顺序：总纲 §2 不变式 > 总纲 §3 Order 表 > 总纲 §5 版本表 > 本分册。
