# 蚁智内政三件套 · 巢穴规划建设（NestLayout）分册契约

> 版本 v0.1.0 ｜ 契约冻结 ｜ 2026-11-13

> 本文件只冻结契约、不含实现；实现按 S1/S2/S3 落地。

本分册从属于 [`colony-era-contract-0.1.0.md`](colony-era-contract-0.1.0.md)（下称「总纲」）。总纲 §2 四条不变式、§3 Order 表、§5 版本表、§7 scenario pack 字段表、§8 验收口径对本分册全部生效。落地阶段 **S2**（依赖 S1 已打通的 save5→6 迁移骨架）。

## 1 · 定位与硬边界

- `NestLayout` 是**地下整数网格**：每格有房间类型、等级、施工进度、`discovered` 标记，并带「是否连到巢核心」的连通判定。**不连通的房间不给任何加成**（防孤立造房刷效果）。
- 房间效果是**可验乘数 / 容量**，不搞隐藏随机：粮仓提存量上限、育婴降产卵成本、兵营加攻防、工坊管加工。所有乘数用整数或固定点整数（百分比 `basis=100`），不读 `RandomState`、不读 `NestSeed` 做效果抽样（`NestSeed` 仅随存档派生并保留、为后续扩展预留，S2 全程默认不读取，见总纲 §2.1）。
- **不复用 `BuildingView`**：总纲 §9 已冻结「地下 `NestLayout` 是独立网格，不复用 `BuildingView`」。`BuildingView`（`Contracts/Production.cs:35-49`，地表 `cache`/`workshop`）保持 `docs/BUILDINGS_WOOD.md` / `docs/OUTPOSTS.md` 的既定语义不动；`OUTPOSTS.md:44`「建筑生命、占领、被动回血、固定线路、全局派工不在本轮」继续成立。地下状态落在 `WorldState.Nest`（`NestLayout` 结构，总纲 §5），**不进 `world.Objects`**（`EntityDatabase` 的 `IWorldObject` 派生表不新增地下类型，避免 `SavedGames.Validate` 对象遍历与 `inspect_object` 全图泄漏）。
- **坐标系隔离**：地表 `Point` 是每格 1000 整数单位（`Protocol.cs:1` 注释 `Coordinates are fixed-point: 1000 units per map cell. No floating-point world state.`）；地下格用 `(chamberX, chamberY)` **小整数索引**，不换算成 `Point`、不乘 `UnitsPerCell`、不进地表寻路 `PointMath`。二者不得混用。
- **施工复用现有件**：`plan_chamber`/`dig_chamber`/`upgrade_chamber`/`cancel_chamber` 由 `builder` 蚁种执行，进度推进复用 `core.work`(Order 31) 的 `WorkTicksRemaining` / 有效工时语义与 `core.nest-construction`(**Order 33**) 的 phase 结算；木料 `FoodTypes.Wood` 不可食用/价值 0/不计 `FirstAdmissionPoints`（`BUILDINGS_WOOD.md:24`），木材与工时全走 `FoodStore` 单一台账（总纲 §2.2）。
- **建/升/拆是投入不是产出**（总纲 §2.3）：四动作只走 `FoodStore.Take` / `Spend` / `Economy.Consumed`，不得 `Economy.Created`、不得写 `Generated`、不得碰 `FirstAdmissionPoints` / `DevelopmentValue`。

## 2 · 网格结构（冻结）

### 2.1 常量与形状

| 项 | 值 | 说明 |
|---|---|---|
| `GridWidth` / `GridHeight` | `9` / `9` | 固定 81 格，`x,y ∈ [0,8]`，索引稳定、不随存档缩放 |
| `CoreX` / `CoreY` | `4` / `4` | **唯一巢核心**，类型 `core`，`level=1`，`phase=active`，`discovered=true`；迁移与新建都必须唯一 |
| `MaxLevel` | `3` | 房间等级上限，`level ∈ [1,3]`；`core` 恒 `1`，不可升级 |
| `ConstructionTicksPerLevel` | 见 §5 成本表 | 有效工时，`builder` 工作率 2（`BUILDINGS_WOOD.md:26`），每次 work 至多 10 Tick、受击中断沿用 `:20` |
| `NestSeed` | `ulong` | 总纲 §2.1 派生，存档字段；**默认不参与效果抽样**，仅预留给「施工期发现扰动」且 S2 阶段保持关闭（恒不读），写进契约防后续偷加随机 |

`NestLayout` 形状（JSON 字段名 snake_case ↔ C# PascalCase，四份一致）：

| JSON 字段 | C# 属性 | 类型 |
|---|---|---|
| `grid_width` | `GridWidth` | int (=9) |
| `grid_height` | `GridHeight` | int (=9) |
| `cells` | `Cells` | `ImmutableArray<ChamberCell>`（长度 **81**，行优先 `index = y*9 + x`，稳定序） |
| `connected_mask` | `ConnectedMask` | `ImmutableArray<bool>`（长度 81，由 §3 BFS 重算，`[JsonIgnore]` 派生亦可但须可验） |
| `capacity_bonus` | `CapacityBonus` | int（派生，§4 聚合值） |

`ChamberCell` 形状（冻结）：

| JSON 字段 | C# 属性 | 类型 / 取值 |
|---|---|---|
| `x` / `y` | `X` / `Y` | int `[0,8]` |
| `chamber_type` | `ChamberType` | enum：`empty` / `core` / `granary` / `nursery` / `barracks` / `workshop` / `corridor` |
| `level` | `Level` | int `[1,3]`（`empty`/`corridor` 恒 1） |
| `phase` | `Phase` | enum：`planned` / `digging` / `active` / `cancelled` |
| `construction_work` | `ConstructionWork` | int `≥0`，单调不减（防刷分，总纲 §2.3） |
| `work_target` | `WorkTarget` | int `≥0`，本阶段所需有效工时（来自 §5 表） |
| `discovered` | `Discovered` | bool（局部感知，总纲 §2.4） |
| `wood_reserved` | `WoodReserved` | int `≥0`，已 `FoodStore.Take` 但尚未消耗的木料（`cancel` 只退这部分） |

### 2.2 房间类型语义

| `chamber_type` | 中文 | 可升级 | 连通要求 | 效果类别 |
|---|---|---|---|---|
| `empty` | 空 / 未挖 | 否 | — | 无；未 `discovered` 时对 player 与 Agent 一律投影为 `empty` |
| `core` | 巢核心 | 否（恒 level 1） | 自身即连通源 | 唯一 BFS 起点；不算加成 |
| `granary` | 粮仓 | 是 | 必须连到 core | 提 `FoodStore` 存量上限（容量，非乘数） |
| `nursery` | 育婴 | 是 | 必须连到 core | 降 `ProduceEgg` / `HatchEgg` 成本 |
| `barracks` | 兵营 | 是 | 必须连到 core | 加兵蚁攻/防（整数百分比） |
| `workshop` | 工坊 | 是 | 必须连到 core | 管 `process(site,recipe)` 加工槽与产出 |
| `corridor` | 通道 | 否 | 本身是连通介质 | 只用于把房间接回 core，不给加成 |

**注意**：地表已有 `workshop`（厨房，`BUILDINGS_WOOD.md:13`）；地下 `workshop` 是**同名不同对象**，靠「在 `NestLayout.Cells` 里 vs 在 `world.Objects` 的 `BuildingView` 里」区分，DSL/MCP 参数必须带 `chamberX/chamberY` 才走地下分支，禁止按名字自动路由。

## 3 · 连通判定算法（稳定、可复现，冻结）

- 输入：`Cells`（81 格，行优先稳定序）。输出：`ConnectedMask`（81 bool）。
- **可通行格**定义：`phase == "active"` 且 `chamber_type ∈ { core, granary, nursery, barracks, workshop, corridor }`。`planned` / `digging` / `cancelled` / `empty` 一律**不可通行**（施工中不提前给连通、不提前给加成）。
- 算法：从唯一 `(CoreX=4, CoreY=4)` 起做 **BFS（4-邻接，不含对角）**；邻居枚举顺序固定为 `上(y-1) → 右(x+1) → 下(y+1) → 左(x-1)`，队列 FIFO，越界跳过。每格只入队一次。结束后 `ConnectedMask[i]=true` 当且仅当该格被 BFS 访问。
- **纯函数**：`ComputeConnectedMask(Cells)` 不读 Tick、不读种子、不读 `WorldState` 其它字段；同输入恒同输出。重算时机**只在** `core.nest-construction`(33) 把某格 `digging → active`、`active → cancelled`、`level` 成功变化时执行；重算结果写回 `ConnectedMask`，不累积、不增量修补。
- **加成门控**：任何房间效果（§4）只在 `ConnectedMask[index] == true` 时计入聚合；孤立房间 `level` 再高也返回 0 贡献。这正是「不连通不给加成」的可验落点。
- Checks 断言 `nest/connectivity/bfs-stable`、`nest/connectivity/isolated-room-zero-bonus`。

## 4 · 效果乘数表（冻结，全整数 / 固定点 basis=100，无随机）

聚合规则：先按 §3 过滤出「`phase=active` 且 `ConnectedMask=true`」的房间，再**按 `index = y*9+x` 升序**累加（稳定序，不依赖遍历哈希顺序）。`level` 线性缩放用 `basis=100` 整数，不引入浮点。

| 房间 | 每级效果（`level=1` 基准） | 聚合出的参数名 | 被谁消费 |
|---|---|---|---|
| `granary` | `CapacityPerLevel = 40` 份/级（对齐 `BUILDINGS_WOOD.md:13` cache 容量 40，但**地下独立计算**） | `NestCapacityBonus = Σ CapacityPerLevel × level` | `FoodStore` 上限校验（`SavedGames.ValidateStore` 与 `core.interaction`(30) 入巢前检查）；**不增加食物，只放宽上限** |
| `nursery` | `EggCostPercent = 100 − 15 × (level−1)` → L1 100 / L2 85 / L3 70 | `EggCostMultiplier = min(取所有 nursery 中最小值)` | `GameSession` 的 `ProduceEgg` / `HatchEgg` 扣费（`TechnologyProgression.Pay` 同一口径），不改 `recruitmentCost=20` 基准定义 |
| `barracks` | `AttackPercent = 100 + 20 × level`（L1 120 / L2 140 / L3 160）、`DefensePercent = 100 + 10 × level` | `SoldierAttackBasis` / `SoldierDefenseBasis` = 取最高一间兵营 × 该值 | `core.combat`(25) 兵蚁 `Attack` 结算与蚁后受击减免；沿用 `AntProgression` 的 `damagePercent [100,125,150]` 不叠乘两次，只取较大者 |
| `workshop` | `ProcessSlotsPerLevel = 1`，`ProcessSpeedPercent = 100 + 25 × (level−1)`（L1 100 / L2 125 / L3 150） | `NestProcessSlots = Σ ProcessSlotsPerLevel × level`，`NestProcessSpeed = max(各间)` | `core.production-actions`(30) `process(site,recipe)` 并发槽与有效进度；混合配方输入仍须原子预留 |
| `corridor` / `core` / `empty` | 恒 0 | — | 仅连通介质 |

- **`nest_capacity`（只读量）= 基准上限 + `NestCapacityBonus`**，基准上限沿用 `WorldConfig` 现有 `groundResourceCap=1200` 之外的巢穴 `Stock` 上限定义；契约冻结「只读量返回整数，不返回浮点、不返回全图」。
- 所有效果**只读**、不写 `Economy.Created`、不生成食物、不改 `FirstAdmissionPoints`。育婴/兵营/工坊改的是「成本 / 战斗 / 加工」，粮仓改的是「容量」，都不直接产分（总纲 §2.3）。
- 制度 `labor_policy`（`institution-contract-0.1.0.md` §2.2）会按 `builder` 权重缩放 `core.nest-construction`(33) 的施工进度；本分册不重定义该参数，只声明消费点。

## 5 · 木料与工时成本表（冻结）

成本全走 `FoodStore`：`FoodTypes.Wood`（`Take` 时 `CreditLot` 随物转移，`BUILDINGS_WOOD.md:24` 明确 wood 价值 0、不计食物储备/首次入巢）。`work_target` 是**有效工时**，`builder` 工作率 2、每次 work 至多 10 Tick（`:20/:26`）。

| 动作 / 房间 | `WoodCost` | `WorkTarget`（有效工时） | 前置 |
|---|---|---|---|
| `plan_chamber` | 0（只登记蓝图，不扣料） | 0 | 目标格 `empty`、`discovered=true`、在 `[0,8]` 内；`chamber_type` 在白名单内 |
| `dig_chamber`（任一房间 L1） | `granary=6` / `nursery=8` / `barracks=10` / `workshop=12` / `corridor=2` | `granary=100` / `nursery=140` / `barracks=160` / `workshop=200` / `corridor=40` | 该格 `phase=planned`；`FoodStore.Take(wood, WoodCost)` 成功；相邻至少一格 `ConnectedMask=true`（保证挖完能连上，防造孤岛） |
| `upgrade_chamber`（L→L+1） | `WoodCost(L+1) = 上表 × level`（即 L2=×2、L3=×3） | `WorkTarget(L+1) = 上表 × level` | `phase=active`、`ConnectedMask=true`、`level < MaxLevel` |
| `cancel_chamber` | **只退 `wood_reserved` 未消耗部分** | 不退已耗工时 | `phase ∈ {planned, digging}`；`active` 不可取消（旧档/地表建筑同样不提供拆除，`:18`） |

- `ConstructionWork` 单调不减；`digging → active` 时**一次性**把 `wood_reserved` 全部转 `Economy.Consumed`（中途不逐 Tick 扣，避免重复扣料），置 `wood_reserved=0`，按 §3 重算 `ConnectedMask`。
- `cancel_chamber` 把 `wood_reserved` 用 `FoodStore.Put` 退回同一 `CreditLot`（`ItemCredit.Merge`），**不**调用 `Economy.Created`、不产任何资源或积分；已沉没的工时/已消耗木料不返还（总纲 `contract/no-farming/cancel-only-refunds-unspent`）。
- 施工中断（受击）沿用 `BUILDINGS_WOOD.md:20`，`ConstructionWork` 保留，不回退。

## 6 · DSL 四动作 + 只读量（白名单位置与精确报错）

`AntDsl.cs` 编译器常量不变（`MaxSourceLength=8192 / MaxNodes=512 / MaxNesting=8 / MaxRepeat=16 / MemorySlots=16`，`:6-11`）。

### 6.1 动作（`Actions`(:511) 追加，参数个数写进 `DslCall` 表(:648)）

| 动作 | 参数 | 精确报错文本 |
|---|---|---|
| `plan_chamber` | 3：`chamberX`(int)、`chamberY`(int)、`chamberType`(string 字面量，§2.2 白名单) | 个数不符 `Action 'plan_chamber' expects 3 argument(s).`；类型非法 `Invalid chamber type '{name}'.`；越界/已占用 `Chamber cell ({x},{y}) is unavailable.` |
| `dig_chamber` | 2：`chamberX`、`chamberY` | 个数不符 `Action 'dig_chamber' expects 2 argument(s).`；非 planned `Chamber cell ({x},{y}) is not planned.`；木料不足 `Insufficient wood for chamber.` |
| `upgrade_chamber` | 2：`chamberX`、`chamberY` | 个数不符 `Action 'upgrade_chamber' expects 2 argument(s).`；满级 `Chamber at ({x},{y}) is at max level.`；未连通 `Chamber at ({x},{y}) is not connected.` |
| `cancel_chamber` | 2：`chamberX`、`chamberY` | 个数不符 `Action 'cancel_chamber' expects 2 argument(s).`；已 active `Cannot cancel active chamber at ({x},{y}).` |

未登记动作仍走 `:558` `Unknown action '{identifier.Text}'.`；白名单封闭，编译期拒绝未知参数形态。

### 6.2 只读量

| 名称 | 形态 | 参数 | 白名单位置 | 报错 / 语义 |
|---|---|---|---|---|
| `chamber_count` | 表达式函数 | 1：`chamberType`（或 `""` 表示全部） | `ExpressionFunctions`(:510) 追加 + :648 的 1 参表 | 个数不符复用 `:649` `Function 'chamber_count' expects 1 argument(s).`；返回**仅 `ConnectedMask=true` 且 `discovered=true`** 的该类型房间数（局部感知） |
| `chamber_connected` | 表达式函数 | 2：`chamberX`、`chamberY` | `ExpressionFunctions`(:510) 追加 + :648 的 2 参表 | `Function 'chamber_connected' expects 2 argument(s).`；未 discovered / 未连通一律返回 `0`（bool→int），不泄漏真值 |
| `chamber_level` | 表达式函数 | 2：`chamberX`、`chamberY` | 同上 | `Function 'chamber_level' expects 2 argument(s).`；未 discovered 返回 `0` |
| `chamber_phase` | 表达式函数 | 2：`chamberX`、`chamberY` | 同上 | `Function 'chamber_phase' expects 2 argument(s).`；未 discovered 返回 `empty` |
| `nest_capacity` | Builtin（无参） | 0 | `Builtins`(:499) 追加 | 未知名走 `:653` `Unknown variable '{identifier.Text}'.`；返回 §4 聚合整数 |

- `GroupValue`(:181-195) 不新增成员；巢穴不是小组数据。
- 五处同步见总纲 §4。

## 7 · `BuildingView` 复用裁决与 MCP / 协议

- **裁决：不复用 `BuildingView`**。理由：`BuildingView` 是 `IWorldObject`，进 `world.Objects` / `EntityDatabase`，会触发 `SavedGames.Validate` 对象遍历、`inspect_object`(:93) 全图可见性、`ObservedBuildings` 投影；地下房间要求「未挖/未发现对 player 与 Agent 同等隐藏」且是独立 9×9 网格，塞进 `IWorldObject` 会破坏总纲 §2.4。故 `WorldState.Nest`（`NestLayout`）是**唯一权威地下结构**，`BuildingView` 一字不改。
- `CommandKind`（`Protocol.cs:137`）**追加到枚举尾部**（总纲 §4 顺序）：`PlanChamber, DigChamber, UpgradeChamber, CancelChamber`（前两个 `SetInstitution/RollbackInstitution` 属 S1）。
- `GameCommand`(:138-155) 追加字段（可空/带默认，旧构造兼容）：
  ```
  int ChamberX = 0,
  int ChamberY = 0,
  string ChamberType = "",
  ```
  `ExpectedWorldId` 沿用已有字段不重复声明。
- `Mcp/Program.cs:107` `Command(...)` 追加可选参数 `int chamberX = 0`、`int chamberY = 0`、`string chamberType = ""`；`Enum.TryParse<CommandKind>` 失败仍抛 `Unknown command kind.`；`requestId`/`expectedRevision`/`expectedWorldId` 幂等语义沿用 `GameSession.ExecuteAsync`，四动作**同样靠 `requestId` 去重**（防止重复 `Take` 木料）。
- **新增只读工具** `inspect_chamber`（`[McpServerTool]`）：参数 `chamberX, chamberY`，返回**仅该格**的 `{ chamberType, level, phase, connected, discovered, constructionWork, workTarget }`；未 discovered 一律返回 `empty` 形状（对齐 `inspect_object`(:93) 的 `object_not_found` 同级隐藏，不泄漏全图）。工具总数 15 → 16（S1 制度）→ **17**（S2）；`get_actions`(:69) `protocolVersion=19`。
- `Snapshot` 只暴露 `nestCapacity` 聚合整数与 `ObservedChambers` 投影，**不返回 `Cells` 全量**（总纲 §2.4）。

## 8 · 存档 + SaveMigration（本轮最重）

- `WorldState` 新增 `NestLayout Nest`、`ImmutableArray<int> ObservedChambers`（**已 discovered 的地下格索引集合，按 `index=y*9+x` 升序、去重、元素域 `[0,80]`**；与 §2.1 的 `ConnectedMask`（`ImmutableArray<bool>` 长度 81）形状不同，不得混用；总纲 §5）。`schemaVersion 15→16`、`SavedGames.Format 5→6`、`Protocol.Version 18→19` 必须与 S1 同一跳，不另开第二级。`SavedGame` 记录形状不变（`SavedGames.cs:9-14`），`SavedGames.MaxBytes=32*1024*1024` / `JsonSerializerOptions` 的 `UnmappedMemberHandling=Disallow` 不变 —— 故**旧档必须靠 `Upgrade` 内存补字段**，不能靠反序列化默认值蒙混。
- **旧档怎么补默认空布局**（逐级链末端追加一级 `Format=5/Protocol=18 → Format=6/Protocol=19`，与总纲 §5 第 2/4 步逐字一致）：
  ```
  Nest = {
    grid_width = 9, grid_height = 9,
    cells = [ 81 个 ChamberCell，行优先 index=y*9+x ]，
      其中每个格子：
        x = index % 9, y = index / 9,
        chamber_type = (index == 4*9+4) ? "core" : "empty",
        level = 1,
        phase = (index == 4*9+4) ? "active" : "planned",
           // empty 格 phase 用 "planned" 只是占位，语义由 chamber_type=empty 决定，不给加成
        construction_work = 0, work_target = 0,
        discovered = (index == 4*9+4),   // 仅 core 已发现
        wood_reserved = 0,
    connected_mask = [ 81 个 bool，仅 index==40 为 true ]，
    capacity_bonus = 0
  }
  ObservedChambers = [ 仅 core 格 ]
  ```
  **严格不变式**：`core` 唯一、`discovered` 初始只有 core、其余 80 格 `chamber_type=empty` 且 `discovered=false`、`capacity_bonus=0`（旧档不凭空得粮仓加成）、`ConnectedMask` 只 core 为 true。不掷随机数、不读写 `RandomState`、不补任何资源（总纲 §2.1/§2.2/§2.3）。
- **原档不落盘改写**：沿用 `SaveMigration.cs:8` 注释 `v1 files remain untouched; load upgrades an immutable candidate before any live mutation.`，`SavedGames.Read`(:25) 先 `NormalizeLegacyJson` → 严格反序列化 → `Upgrade` → `Validate` 的链路不变；`NormalizeLegacyJson`(:49) **不**为 `Nest` 新增分支。
- `SavedGames.Validate`(:35) 增补：`Nest.Cells.Length == 81`、唯一 `core` 在 `(4,4)` 且 `phase=active`/`discovered=true`、`ConnectedMask` 与 `ComputeConnectedMask(Cells)` 逐位相等（防存档里手填假连通）、每格 `chamber_type` 在 §2.2 白名单内、`level ∈ [1,3]`、`construction_work ≥ 0`、`wood_reserved ≥ 0`、`ObservedChambers` 索引在 `[0,80]`；失败沿用字段级异常，不混用 `Unsupported save format.` 标签。
- 兼容矩阵：`wire18/content15/save5`（含 wire12–17、save1–4、旧工地 `ConstructionItem=fiber`、`cache.CanFeedAnts`）→ 内存升级 `wire19/content16/save6`；地表 `BuildingView` / `ObservedBuildings` 一律保持原样。

## 9 · Checks 断言（并入现有单一计数器）

`tools/AntMind.Checks/Program.cs:9` 的 `Check(bool,label)` 与 `:502` 的单一 `All {checks} architectural checks passed.` 不变。本分册新增 label：

```
nest/grid/81-cells-stable-order
nest/grid/single-core-at-4-4
nest/connectivity/bfs-stable
nest/connectivity/isolated-room-zero-bonus
nest/connectivity/no-diagonal
nest/effect/integer-basis-only
nest/effect/capacity-only-widen
nest/cost/wood-not-food
nest/cost/construction-work-monotonic
nest/cost/cancel-refunds-unspent-only
nest/dsl/whitelist-closed
nest/dsl/undiscovered-returns-zero
nest/visibility/observed-chambers-only
nest/migration/save5-empty-layout
nest/migration/single-core-default
nest/migration/connected-mask-matches-bfs
nest/migration/original-bytes-untouched
nest/order/core-nest-construction-33
```

复用总纲断言：`contract/no-farming/build-is-investment`、`contract/no-farming/cancel-only-refunds-unspent`、`contract/conservation/single-ledger`、`contract/conservation/credit-lot-follows-item`、`contract/visibility/undiscovered-chamber-is-empty`、`contract/visibility/player-equals-agent`、`contract/determinism/independent-subseeds`（`NestSeed` 派生但 S2 恒不读）。

## 10 · 锚点标定题（烂 / 中 / 优 三档齐）

题名 `nest-connectivity-plan`，`scenarioId` 固定不带日期，`contractVersion="0.1.0"`，`axes=["nest"]`，`layers=["baseline","+nest","all"]`，`ticks` 同跑 300 / 1200 两档，`seed` / `promptSha256` / `slot` 三档**完全相同**，只换 `strategyId`（同题对照，总纲 §7）。

| 档 | `strategyId` | 参考策略 | 预期 |
|---|---|---|---|
| 烂 | `poor` | 只 `plan_chamber` + `dig_chamber` 乱挖孤立格，从不铺 `corridor`；或反复 `cancel_chamber` 试图刷回木料 | 房间全不连通 → `nest_capacity` 恒 0、加成全 0，木料/工时白耗，L3 与得分垫底 |
| 中 | `medium` | 从 core 用 `corridor` 接出一条线，建 1 间 `granary` 并读到 `chamber_connected=1` | 拿到基础容量加成，但不升级、不组合 `nursery`/`workshop`，中段 |
| 优 | `good` | 读 `chamber_connected` / `chamber_level` / `nest_capacity` 与事件流，铺冗余 `corridor` 后按阶段建 `granary`→`workshop`→`nursery` 并 `upgrade_chamber`，全程不造孤岛、不无效取消 | 三档中 L3 写对率与 `FirstAdmissionPoints` 最高（收益仍来自首次入巢，房间只放大吞吐） |

**怎么判一道题能不能用（作废判据，逐字沿用总纲 §8.5）**：三档在同一 `scenarioId` 上的 L3 写对率差值低于该题预设最小可分阈值，或三档 `FirstAdmissionPoints` 全部相同（含同为 0）→ **该题直接作废**，不得靠调分母、改计分口径或换题目难度来「救」。L5 开放题单列，不并入 L3；能力结论须 `grading.crossPipelineRecheck=true` 跨管道独立复核后才可写入文档，并留存原始输入与完整方法（总纲 §7）。

## 11 · 与其它分册的边界

- `institution-contract-0.1.0.md`：`labor_policy` 的 `builder` 权重在此消费（§4 末）；`admission_policy` 与 `nest_capacity` 无关，前者改顺序、后者改上限。
- `creature-contract-0.1.0.md`：蛀仓虫「专啃粮仓/仓库」的**地下粮仓目标**由本分册 `granary` 格提供，但巢穴不定义 `CreatureKind`、不读 `CreatureSeed`；堵路虫的 `BlockedCells` 是地表通道格，与地下 `corridor` 不是同一坐标系（§1 坐标系隔离）。
- 冲突裁决顺序：总纲 §2 不变式 > 总纲 §3 Order 表 > 总纲 §5 版本表 > 本分册。
