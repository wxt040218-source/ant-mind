# 蚁智内政三件套 · 总纲契约

> 版本 v0.1.1 ｜ 契约冻结 ｜ 2026-11-13 ｜ v0.1.1 勘误 2026-11-14

> 本文件只冻结契约、不含实现；实现按 S1/S2/S3 落地。

## v0.1.1 勘误（2026-11-14）

- 本次勘误只升总纲与制度分册两份文档的内部版本行；另两份分册仍保持 S0 冻结的 v0.1.0，§0「同版本」按 S0 冻结时点理解，`contractVersion="0.1.0"` 字符串不变。
- 四条不变式、Order 表、版本三跳、白名单、SaveMigration 六步、phase 顺序全部不变；文件名不变。
- 订正 `set_institution` 为编译期合法的只读意图：DSL 写 `Decision` 供审计/预演，不改 `InstitutionState`、不产生全局写入。制度写入只走 `CommandKind.SetInstitution` / `RollbackInstitution` 经 MCP `command` + Runtime actor + `requestId` 幂等。
- 制度分册同步登记 `admission_policy` 次键偏差留 S4；S1.5 不修实现、不改制度分册 §2.2 正文。

## 0 · 本文件的效力与边界

- 本文件是 `dev/colony-era` 分支后续 S1/S2/S3/S4 全部开发的**唯一字段与命名准绳**。三份分册（`institution-contract-0.1.0.md`、`nest-layout-contract-0.1.0.md`、`creature-contract-0.1.0.md`）与本文件同版本、同日期、互相引用；分册不得另立与本表冲突的字段名、Order 或枚举值。
- S0 阶段**零引擎改动**：不写任何 `.cs` / `.json` / `.yml`，不改 `content/world.json`，不动 `Pipes.cs` / `Directory.Build.props`，不碰服务器与上游 `ytzha/ant-mind`。下文所有「将新增 / 将修改」都是**对 S1–S3 的约束描述**，不是本阶段动作。
- 天气层整层不做。多蚁群、第二 AI 巢、配对赛本轮冻结，故 `RivalSeed` 只预留命名、不进存档、不参与任何计算。
- 计分口径不变：收益仍只认 `FirstAdmissionPoints`（新食物首次入巢）。考场目标是让强弱模型在**同一道题**上拉开分，而不是把默认搬运题的分母做大。

## 1 · 三件套定义（冻结）

| 件套 | 契约对象 | 本质 | 是否新增 IWorldRule | 落地阶段 |
|---|---|---|---|---|
| 文明制度 | `InstitutionState` | 全群共享、带版本、可热更新/回滚的**封闭白名单状态**；每步跑规则前映射成参数（阈值/乘数/稳定排序键），是状态不是随机 | 否（Tick 前置纯函数 `InstitutionParameters.Resolve`） | S1 |
| 巢穴规划建设 | `NestLayout` | 地下整数网格，房间带等级、连通判定、施工进度；效果是可验乘数/容量，不搞隐藏随机 | 是，`core.nest-construction`（Order 33） | S2 |
| 野怪生态 | `CreatureKind` 白名单 | 复用现成 `EnemyView` / `EnemySpawnRule` / `EnemyMovementRule` / `CombatRule` / `AddFood` 尸体链路，**不另起对象体系** | 是，`core.creature-ecology`（Order 1） | S3 |

三件套都遵守：新增只读量与动作进 `AntDslParser` 三张白名单；新增命令进 `CommandKind` 与 `command` 工具；新增状态进 `WorldState` 并走 `SaveMigration`；新增断言并入 `tools/AntMind.Checks/Program.cs` 现有单一 `Check(bool,label)` 计数器。**DSL `set_institution` 是只读意图语句**（写 `Decision` 供审计/预演，不改 `InstitutionState`、不产生全局写入）；全局立法只走 `CommandKind.SetInstitution` / `RollbackInstitution` 经 MCP `command` + Runtime actor + `requestId` 幂等，DSL 语句与命令层不得混名。

## 2 · 四条不变式：怎么保证 + 怎么验

四条对三件套**同时生效**，分册只写本件套特有的落点，不重定义不变式本身。

### 2.1 确定性

- **怎么保证**：`WorldState` 新增 `ulong CreatureSeed`、`ulong NestSeed`，由 `Simulation.Create(WorldConfig, ulong seed)` 用固定整数常量一次性派生；之后**野怪侧** `core.enemy-spawn`(0) 用 `RandomSource.Next(ref world.CreatureSeed)`，`NestSeed` 随存档派生并保留、**S2 默认不读取**（巢穴效果全是纯确定性整数函数，见 `nest-layout-contract-0.1.0.md` §2.1，预留子种子只为后续不破坏确定性时扩展）。两者都**绝不读写 `world.RandomState`**：S3 起 `core.enemy-spawn`(0) 从主 RNG 改挂 `CreatureSeed`，主 RNG 此后只服务于现有资源刷新、`SourceGrowth`、`ProductionFactory`（现状 `EnemySpawnRule` 用 `world.RandomState` 是 S3 必须清掉的债务，见 `creature-contract-0.1.0.md` §3）。派生式（纯整数，无浮点）：
  ```
  CreatureSeed = RandomSource.Next(ref Mix(seed, 0x9E3779B97F4A7C15UL))
  NestSeed     = RandomSource.Next(ref Mix(seed, 0xC2B2AE3D27D4EB4FUL))
  Mix(a,b)     = a ^ b 后再走一次 RandomSource.Next
  ```
  周期迁徙群例外且更严格：成群数量与位置是 `f(tick, scenarioSeed)` 的**步数纯函数**，连 `CreatureSeed` 都不读，保证可预测、可写进事件流。
  `Simulation` 已用 `OrderBy(x => x.Order).ThenBy(x => x.Id, StringComparer.Ordinal)` 排序，新增规则给未占用的稳定 Order（见 §3），同 Order 靠 Id 次键稳定，不引入遍历顺序依赖。
- **怎么验**：同一 `seed` + 同一输入序列（DSL、MCP 命令、scenario pack）跑两遍，逐 Tick 比对 `WorldState` 的完整序列化摘要（含 `RandomState`、`CreatureSeed`、`NestSeed`、`Stock.Credits`、`FirstAdmissionPoints`、`BlockedCells`、`Nest.ConnectedMask`），要求逐字节一致；再单独断言「只改 CreatureKind 刷新分支不改变主 `RandomState` 轨迹」。Checks 断言：`contract/determinism/independent-subseeds`、`contract/determinism/migration-is-pure-function-of-tick`。

### 2.2 守恒

- **怎么保证**：房间存量、野怪掉落、放牧产粮、制度消耗、施工木料/工时**全部**经 `Contracts/Colony.cs` 的 `FoodStore`（唯一权威台账：`Amounts` + `Credits`）与 `ItemCredit.Split` / `ItemCredit.Merge`，`CreditLot` 随物转移；`BuildingView.Store`、据点 `station.Store`、巢穴 `world.Stock` 是同一本台账的不同持有者，不另起第二份权威库存（沿用 `docs/OUTPOSTS.md:7` 的既定事实）。生成类一律 `Economy.Created`，消费类一律 `Economy.Consumed`，`Generated` / `CreatedItems` / `ConsumedItems` 同步。
- **怎么验**：每一步校验 `Σ(生成) − Σ(消费) = Σ(地上资源 + 各 Store + 携带 + 巢穴 Stock)`，且 `Stock.Credits` 各 `CreditLot.Quantity` 之和等于 `Stock.Amounts`；野怪死亡一次只产生一次 `AddFood`，重试/回滚不重复生成。Checks 断言：`contract/conservation/single-ledger`、`contract/conservation/credit-lot-follows-item`、`contract/conservation/no-duplicate-carcass`。

### 2.3 防刷分

- **怎么保证**：建房间、升房间、立制度、改档位、回滚制度**都是投入不是产出**：只走 `FoodStore.Take` / `Spend` / `Economy.Consumed`，**不**调用 `Economy.Created`、**不**触碰 `FirstAdmissionPoints`、**不**写 `Generated`。`core.interaction`（Order 30）继续是唯一首次入巢计分点，且同一 `CreditLot` 只计一次。施工进度 `ConstructionWork` 单调不减；`cancel_chamber` 只把**未消耗**材料退回 `FoodStore`，已消耗的木料与工时沉没，取消动作本身不产生任何资源或积分。制度回滚复制历史快照、只改参数，不改账面。
- **怎么验**：构造「造了拆、拆了造」与「反复切制度档位」的对抗脚本，断言 `FirstAdmissionPoints` 与 `DevelopmentValue` 与非建造基线**完全相同**（只少了施工消耗）；断言任何 `SetInstitution` / `RollbackInstitution` / `CancelChamber` 前后 `CreatedItems` 不变。Checks 断言：`contract/no-farming/build-is-investment`、`contract/no-farming/rollback-does-not-mint`、`contract/no-farming/cancel-only-refunds-unspent`。

### 2.4 局部感知

- **怎么保证**：未挖、未发现的巢室与未发现的野怪，对 player 与 Agent **同等隐藏**，统一由 `core.local-observation`（Order 5）投影：`ObservedChambers` 只含已 discovered 的格子，未发现格一律返回 `empty`；野怪**复用 `EnemyView` 既有** `IsDiscovered` / `LastKnownPosition` / `LastKnownHealth` / `LastSeenTick` 四字段做投影（**不新增 `ObservedCreatures` 字段**，避免与 §5 的 `WorldState` 新增字段表冲突），未发现野怪 `inspect_object` 返回 `object_not_found`（与不存在的 ID 同表现，对齐现有 `Mcp/Program.cs:93`）。禁止把 `world.Nest.Cells`、`world.Enemies` 全量塞进 `Snapshot`。
- **怎么验**：同一局面分别取 `observe` 快照与 `Mcp` 各只读工具，断言二者可见集合相等；把一只野怪标为未发现后，断言 player 与 Agent 都查不到它、且地下房间未 discovered 时 `chamber_connected` / `chamber_level` 返回 0 而非真值。Checks 断言：`contract/visibility/player-equals-agent`、`contract/visibility/undiscovered-chamber-is-empty`、`contract/visibility/undiscovered-creature-not-found`。

## 3 · 规则组合根与 Order 表（冻结，四份一致）

`src/AntMind.Runtime/GameRules.cs:12` 的 `Create(SpeciesProgramCatalog catalog, IEnumerable<IWorldRule>? extra = null)` 是完整组合根。现有实例清单（实读 22 个实例；本文只称「现有组合根规则清单」，不硬断言数量）：`ExplorationExperienceRule`、`AntExperienceRule`、`ActivitySamplingRule`、`SourceGrowthRule`、`ProductionActionRule`、`WorkRule`、`EconomyStatisticsRule`、`EnemySpawnRule`、`LocalObservationRule`、`QueenCommunicationRule`、`ProgramRule(catalog)`、`MovementRule`、`EnemyMovementRule`、`ClaimRule`、`FollowRule`、`CombatRule`、`InteractionRule`、`EatingRule`、`ResourceRefreshRule`、`ResourceDiscoveryRule`、`ClaimCleanupRule`、`MilestoneRule`，随后 `.Concat(extra ?? [])`。

已占用 Order（实读 `grep Order =>`）：`0, 2, 5, 10, 15, 16, 18, 19, 20, 21, 25, 30(两处), 31, 32, 40, 45, 46, 47, 48, 49, 50`。**新增规则只能取未占用的 `1` 与 `33`**，且四份文档一致：

| Id | Order | 类型 | 作用 |
|---|---|---|---|
| `core.creature-ecology` | **1** | 新增 | 结算蚜虫放牧产粮、蛀仓虫啃仓、堵路虫写 `BlockedCells`；在 `core.enemy-spawn`(0) 之后、其余规则之前，保证同 Tick `core.move`(20) 能读到减速格 |
| `core.nest-construction` | **33** | 新增 | 推进 `ConstructionWork`、phase→`active` 时重算 `ConnectedMask`；在 `core.work`(31)/`core.eating`(32) 之后 |

**扩展但不改 Order、不新增 Id 的现有规则**（分册逐条写改造点）：`core.enemy-spawn`(0)、`core.local-observation`(5)、`core.queen-communication`(15)、`core.move`(20)、`core.enemy-move`(21)、`core.combat`(25)、`core.production-actions`(30)、`core.interaction`(30)、`core.work`(31)、`core.eating`(32)、`core.discovery`(45)。

制度**不新增 IWorldRule、不占 Order**：`GameSession.Advance` 在跑 rules 前调用纯函数 `InstitutionParameters.Resolve(InstitutionState)` 得到本 Tick 参数快照，各消费规则在自身 `Apply` 开头读取。`InstitutionState` 本身是 `WorldState` 上的**持久存档字段**（字段名 `Institution`，见 §5，随 save5→6 迁移并补默认值），但每 Tick 派生的 `InstitutionParameters` 只是 `[JsonIgnore]` 的只读执行上下文、不落盘、不改变任何持久状态、不引入新随机源；因此制度无规则 Id、无 Order、不参与排序，也不属于 §2.1 的子种子体系。

## 4 · 五处同步清单（每个新动作/状态都必须全过）

| # | 位置 | 具体要求 |
|---|---|---|
| 1 | 契约 | 本总纲 + 对应分册 + `docs/README.md` 索引（若项目索引存在）同步字段、枚举、Order |
| 2 | 内容 | `content/world.json`：`schemaVersion` 15→16，新增 `creatures` / `nestLayout` / `institutions` 默认段；**S0 不改此文件** |
| 3 | DSL | `src/AntMind.Programs/AntDsl.cs`：`Builtins`(:499)、`ExpressionFunctions`(:510)、`Actions`(:511) 三张白名单 + `DslCall` 参数个数表(:648) + 精确报错文本；`MaxSourceLength=8192 / MaxNodes=512 / MaxNesting=8 / MaxRepeat=16 / MemorySlots=16` 不变 |
| 4 | MCP / 协议 | `Protocol.cs` `CommandKind`(:137) 追加 6 个成员（**追加到枚举尾部，不打乱旧值**）、`GameCommand`(:138) 追加制度字段（`InstitutionField`/`InstitutionTier`/`TargetInstitutionVersion`）与地下格字段（`ChamberX`/`ChamberY`/`ChamberType`）、`Mcp/Program.cs` 的 `command`(:107) 参数 + 新增只读工具 `inspect_institution` / `inspect_chamber`、`get_actions`(:69) 输出 `protocolVersion=19` |
| 5 | 文档 + Checks | `docs/` 三件套分册与 `docs/BUILDINGS_WOOD.md` / `docs/OUTPOSTS.md` 交叉引用；`tools/AntMind.Checks/Program.cs` 在现有 `Check(bool,label)` 计数器内加断言，输出仍是单一 `All {checks} architectural checks passed.`（当前 460 项），**不另起计数器** |

新增 `CommandKind` 成员（顺序冻结）：`SetInstitution, RollbackInstitution, PlanChamber, DigChamber, UpgradeChamber, CancelChamber`。**S3 野怪生态不新增命令面**：不新增 `CommandKind` / `GameCommand` 写字段 / `command` 写参数 / 只读工具；MCP 工具总数在 S2 后固定为 17，野怪信息只经既有 `inspect_object` / `observe` / `observe_summary` 与事件投影暴露（逐字见 `creature-contract-0.1.0.md` §6）。

## 5 · 版本与迁移策略（冻结，四份一致）

| 项 | 现值 | 目标值 | 理由 |
|---|---|---|---|
| `content/world.json` `schemaVersion` | 15 | **16** | 新增 `creatures`（白名单与刷新参数）、`nestLayout`（网格尺寸/成本/效果表）、`institutions`（默认档位）、可选 `scenarios` 段；旧字段一律不改语义 |
| `SavedGames.Format`（`Runtime/SavedGames.cs:11`） | 5 | **6** | `WorldState` 新增 `CreatureSeed` / `NestSeed` / `Nest` / `Institution` / `BlockedCells`(`ImmutableArray<int>` 地表格索引) / `ObservedChambers`(`ImmutableArray<int>` 地下格索引集合)，`EnemyView` 新增 `CreatureKind` 等字段 |
| `Protocol.Version`（`Contracts/Protocol.cs:226`） | 18 | **19** | `CommandKind` 与 wire 的 `GameCommand` 形状变化；`SavedGames.Validate` 硬校验 `save.Protocol == Protocol.Version`，故**必须与 Format 同步升**，否则旧档全部报 `Unsupported save format.` |
| `tools/AntMind.Checks` 计数 | 460 | 460 + N | N 由实现统计；只加 `Check(...)` 调用 |

**SaveMigration 升级链**（`Runtime/SaveMigration.cs:10` 的 `Upgrade` 逐级追加一级，不改写原档字节 —— 沿用文件头注释 `v1 files remain untouched; load upgrades an immutable candidate before any live mutation.`）：

```
Format=5 / Protocol=18  →  Format=6 / Protocol=19
  1. 补 CreatureSeed / NestSeed：由 save.World.RandomState 与 §2.1 固定常量确定性派生（不掷新随机数）
  2. 补 Nest：9×9，中心 (4,4) 唯一 `core`（phase=active / discovered=true），其余 80 格 `chamber_type=empty` 且 `discovered=false`，`ConnectedMask` 仅 core 为 true，`capacity_bonus=0`（逐字见 `nest-layout-contract-0.1.0.md` §8）
  3. 补 Institution：Version=1，`reserve_policy="balanced"` / `ration_policy="weakest_first"` / `labor_policy="balanced"` / `defense_policy="measured"` / `admission_policy="fresh_first"`（即 `institution-contract-0.1.0.md` §2.2 默认值），Reason="migrated-from-save5"，AppliedTick=save.World.Tick，History 含该初始版本
  4. 补 BlockedCells=[]，ObservedChambers=[core 格索引 40]
  5. 现有 EnemyView 一律补 `creature_kind="predator_beetle"` / `carcass_food_type="meat"` / `block_cells=0` / `herd_id=0` / `last_graze_tick=-1000000` / `target_chamber_x=-1` / `target_chamber_y=-1`（扩展字段 7 个标量 / 6 行表格，逐字见 `creature-contract-0.1.0.md` §4）
  6. ObservedBuildings / 旧工地 ConstructionItem=fiber / cache.CanFeedAnts 等既有迁移结果保持不动
```

`NormalizeLegacyJson`(:49) 继续只做已知旧字段改名，不新增分支。兼容矩阵：`wire18 / content15 / save5`（含已知 wire12–17、save1–4 旧档）→ 内存升级到 `wire19 / content16 / save6`；原档不落盘改写。`SavedGames.Validate`(:35) 增补新字段的合法性检查（子种子非 0、Nest 唯一 core、Institution 档位在白名单内、BlockedCells 索引在网格内）。

## 6 · phase 落地顺序（S0..S4）

| Phase | 内容 | 完成判据 |
|---|---|---|
| **S0** | 冻结本总纲 + 三份分册，一笔 `docs(contracts):` 提交并推送 `origin dev/colony-era` | 4 份文档齐、字段/Order/命名自查一致、`git status --porcelain` 干净 |
| **S1** | 文明制度：`InstitutionState`、参数纯函数、6 个 `CommandKind` 中的 2 个、DSL 只读量 `institution(...)` 与只读意图语句 `set_institution`、MCP 只读工具、save5→6 迁移骨架 | 制度热更新/回滚幂等可验；旧档加载通过；五处同步全过 |
| **S2** | 巢穴：`NestLayout`、`core.nest-construction`(33)、4 个 DSL 动作与只读量、连通 BFS、成本/效果表、最重的旧档补默认空布局 | 不连通不给加成可验；造拆不刷分可验；连通算法同输入同输出 |
| **S3** | 野怪：`CreatureKind` 白名单、`core.creature-ecology`(1)、独立 `CreatureSeed`、5 种行为、掉落守恒、局部感知 | 步数纯函数迁徙可预测；掉落只走 `AddFood` 一次；子种子不污染主 RNG |
| **S4** | 锚点标定题 + scenario pack + 评测口径落地 | 三件套各一套「烂/中/优」同题可分辨；写对率 L3 主指标出数；能力结论跨管道独立复核 |

顺序理由：制度是纯参数层、存档结构最小（仅一个 `WorldState.Institution` 持久字段，见 §3/§5），最轻先落并顺带打通 save5→6 迁移骨架；巢穴带最重的默认布局迁移，放在骨架之后；野怪复用现成 `EnemyView`/`CombatRule` 链路、改动面最小，放最后；标定必须等三件套都能跑才做。

## 7 · scenario pack 字段表（冻结）

scenario pack 是评测输入的唯一描述格式，`scenarioId` 稳定不带日期。

| 字段 | 类型 | 说明 |
|---|---|---|
| `scenarioId` | string | 稳定主题 id，如 `nest-granary-rush` |
| `contractVersion` | string | 固定 `"0.1.0"`，与四份文档头部版本行一致 |
| `seed` | ulong | 主种子；派生 `CreatureSeed` / `NestSeed`（§2.1） |
| `ticks` | int | 步数上限（现有基线 300 / 1200 两档均保留，便于对照历史「300 步全场 0」） |
| `slot` | string | 存档槽 / 运行位标识 |
| `promptSha256` | string | 发给外部 AI 的题面原文 SHA-256，保证同题对照可复核 |
| `strategyId` | string | 参考策略 id（`poor` / `medium` / `good` 三档必齐，见 §8） |
| `axes` | string[] | 能力轴：`nest` / `institution` / `creature` / `combo` |
| `layers` | string[] | 消融层：`baseline` / `+nest` / `+institution` / `+creature` / `all` |
| `grading` | object | `primary: "L3"`（写对率主指标）、`openEnded: "L5"`（开放题单列，不并入 L3）、`crossPipelineRecheck: true`（能力结论须跨管道独立复核） |

硬约束：**对比测试必须同题对照** —— 比较中/英、强/弱、A/B 时必须用同一份 `promptSha256` + 同一 `seed` + 同一 `ticks`，只换 `strategyId`；用难题比易题等于没测。能力类结论落进文档前必须留存原始输入与完整方法，并经独立复核。

## 8 · 「什么算完成」验收口径

1. **契约完成（S0）**：本总纲 + 三份分册落 `docs/contracts/`，文件名带版本、不带日期；内部字段/Order/枚举/命名四份一致；一笔语义化提交推送 `origin/dev/colony-era`，工作树干净，远端回读一致。
2. **实现完成（S1/S2/S3）**：五处同步清单全过；解决方案构建通过；`tools/AntMind.Checks` 单一计数器全绿；`All {checks} architectural checks passed.`；旧档迁移矩阵逐档加载通过且原档字节未变。
3. **不变式完成**：§2 四条各有「怎么保证」的代码落点与「怎么验」的 Checks 断言，逐 Tick 确定性比对通过。
4. **考场完成（S4）**：三件套各一套锚点标定题，同题下「烂 / 中 / 优」三档 `FirstAdmissionPoints` 与写对率 L3 **显著可分**；拉不开分即该题作废重出（判据见三份分册 §锚点标定题）；官方默认策略在 1200 步的得分仍作为基线单独记录，避免「模型写的持平或 0」的旧问题重演。
5. **锚点题作废判据（全局）**：若 `poor`、`medium`、`good` 在同一 `scenarioId` 上的主指标差值低于该题预设的最小可分阈值，或三档得分全部相同（含同为 0），该题**直接作废**，不得靠调分母、改计分口径或换题目难度来「救」；L5 开放题单列，不参与 L3 结论；任何能力结论未经跨管道独立复核不得写入文档。

## 9 · 与其它文档的关系

- `docs/BUILDINGS_WOOD.md`、`docs/OUTPOSTS.md`：地表建筑与中转据点的既定事实与边界（`OUTPOSTS.md:44` 明写「建筑生命、占领、被动回血、固定线路、全局派工不在本轮」）。地下 `NestLayout` 是**独立网格**，不复用 `BuildingView`，不改上述边界。
- `PROJECT_STATUS.md`：上游 v0.19 长进度记录，`wire18/content15/save5`、`Checks 460 项通过` 等事实以本契约 §5 的目标值为准做阶段切换；S0 不强改该文件，阶段变化由 lead 决定是否追加一行进度事实。
- 三份分册：`institution-contract-0.1.0.md`（S1）、`nest-layout-contract-0.1.0.md`（S2）、`creature-contract-0.1.0.md`（S3）。冲突时以本总纲 §3 Order 表、§5 版本表、§2 不变式为准。
