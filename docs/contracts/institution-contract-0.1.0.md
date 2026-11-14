# 蚁智内政三件套 · 文明制度（Institution）分册契约

> 版本 v0.1.2 ｜ 契约冻结 ｜ 2026-11-13 ｜ v0.1.1 勘误 2026-11-14 ｜ v0.1.2 S2 反转登记 2026-11-14

> 本文件只冻结契约、不含实现；实现按 S1/S2/S3 落地。

## v0.1.1 勘误（2026-11-14）

- 本次勘误只订正 `set_institution` 的语义与锚点题 `institution-reserve-crunch` 的区分变量；四条不变式、Order 表、版本三跳、字段/档位白名单、SaveMigration 六步全部不变。文件名与 `contractVersion="0.1.0"` 保持 S0 冻结标识不变。
- `set_institution` 是**编译期合法的只读意图语句**：DSL 编译通过后把字段/档位写入 `Decision` 供审计/预演，不写 `WorldState.InstitutionState`、不产生全局写入、不改变制度版本。制度写入唯一入口是 `CommandKind.SetInstitution` / `RollbackInstitution` 经 MCP `command` → Runtime actor → `requestId` 幂等落盘。
- 锚点题三档的区分变量改由上述 MCP command 的下发次数、阶段组合与 `RollbackInstitution` 重放构成；DSL `set_institution` 只可作为题面里的意图记录，不作为制度生效与否的判据。
- 登记实现偏差（本轮只登记不改）：`admission_policy` 冻结口径（§2.2）要求 `fresh_first` 按 `CreditLot.CreatedTick`、`bulk_first` 按 `Quantity`、`near_first` 按距巢、`type_first` 按 `Value` 排序且次键恒为 `ResourceId` 升序；但本库 `CreditLot` 仅有 `(Quantity, PointsPerUnit)`（`src/AntMind.Contracts/Production.cs:6`），无 `CreatedTick` / `ResourceId`，当前实现改以 `AntId` 作稳定次键。受数据结构限制，仅排序键不同，物品总量与账本守恒、`FirstAdmissionPoints` 判定不受影响；留 S4 标定前订正实现或契约，S1.5 不修实现、不改 §2.2 正文。
- 登记 S1.5 新增 Check label：`institution/chamber-commands-rejected-until-s2`、`institution/chamber-commands-filtered-from-get-actions`、`contract/determinism/default-institution-baseline-equivalence`。

## v0.1.2 S2 反转登记（2026-11-14）

- S1.5 的两条负向 Check label 已在 S2 撤销：`institution/chamber-commands-rejected-until-s2` 替换为 `institution/chamber-commands-accepted-after-s2`（只验证 `PlanChamber` 不再早拒），`institution/chamber-commands-filtered-from-get-actions` 替换为 `nest/command/four-kinds-get-actions`（验证 `get_actions` 暴露四个 Chamber kind）。
- 四个 Chamber kind 的完整真接受由 `tools/AntMind.Checks/NestCheck.cs` 的 `nest/command/four-kinds-real-accepted` 坐实：通过合法 save6 注入 active 房间与木料，依次下发 `PlanChamber` / `DigChamber` / `UpgradeChamber` / `CancelChamber`，四条命令均 `Accepted=true` 且 `Code="applied"`。
- `contract/determinism/default-institution-baseline-equivalence` 保持有效；制度分册的字段/档位白名单、四条不变式、Order 表、版本三跳、SaveMigration 六步全部不变。

本分册从属于 [`colony-era-contract-0.1.0.md`](colony-era-contract-0.1.0.md)（下称「总纲」）。总纲 §2 四条不变式、§3 Order 表、§5 版本表、§7 scenario pack 字段表、§8 验收口径对本分册全部生效，本分册不重定义、不冲突。落地阶段 **S1**。

## 1 · 定位与硬边界

- `InstitutionState` 是**全群共享、带版本、可热更新/回滚的封闭白名单状态**。它是一张「国策表」，不是随机源、不是奖励表、不是第二条计分通道。
- **制度不直接给分**：任何只读 `set_institution` 意图记录、`CommandKind.SetInstitution` / `RollbackInstitution` 命令都不得调用 `Economy.Created`、不得写 `Generated`、不得触碰 `FirstAdmissionPoints` 与 `DevelopmentValue`。收益仍只认 `core.interaction`(Order 30) 的首次入巢（总纲 §2.3）。
- **制度是状态不是随机**：`GameSession.Advance` 在跑本 Tick 的 rules 之前调用纯函数 `InstitutionParameters.Resolve(InstitutionState)`，把白名单档位映射成整数阈值/乘数/稳定排序键，得到本 Tick 只读快照；各消费规则在自身 `Apply` 开头读取快照。快照 `[JsonIgnore]`、不落盘、不写回任何持久状态、不读 `world.RandomState`、不读 `CreatureSeed`/`NestSeed`。
- **制度不新增 IWorldRule、不占 Order**：总纲 §3 已把未占用 Order `1` 留给 `core.creature-ecology`、`33` 留给 `core.nest-construction`；制度没有规则 Id、没有 Order、不参与 `OrderBy(x => x.Order).ThenBy(x => x.Id, StringComparer.Ordinal)` 排序。`InstitutionState` 本身是 `WorldState.Institution` 上的**持久存档字段**（总纲 §5，随 save5→6 迁移补默认值），但每 Tick 派生的 `InstitutionParameters` 只是只读执行上下文，二者不得混名。
- **沿用项目既定事实**：每蚁种全群共享代码、小组仅为任务数据（`docs/AGENT_START_HERE.md` / `PROJECT_STATUS.md`），故 `labor_policy` 只改参数（工种权重），**不改 `SpeciesProgramCatalog`、不替换程序、不给某小组单独发一份制度**。`ConfigureGroup` 原子替换语义（`Mcp/Program.cs:47`）保持不变。

## 2 · 字段表（冻结：字段名 / 类型 / 枚举值 / 默认值 / 取值范围）

### 2.1 JSON 字段名 ↔ C# 属性名（四份文档与代码必须逐字一致）

| JSON 字段名（snake_case，`content/world.json` / 存档 / DSL / MCP 统一用此名） | C# 属性（PascalCase） | 类型 |
|---|---|---|
| `reserve_policy` | `ReservePolicy` | enum(string) |
| `ration_policy` | `RationPolicy` | enum(string) |
| `labor_policy` | `LaborPolicy` | enum(string) |
| `defense_policy` | `DefensePolicy` | enum(string) |
| `admission_policy` | `AdmissionPolicy` | enum(string) |
| `version` | `Version` | int |
| `applied_tick` | `AppliedTick` | long |
| `reason` | `Reason` | string |
| `history` | `History` | `ImmutableArray<InstitutionRevision>` |

`InstitutionRevision` 形状（冻结）：`{ version:int, applied_tick:long, reason:string, fields: { 上述五字段快照 } }`。`History` 最多 **32** 条（`MaxHistory=32`），超出时丢弃最旧一条；`Version` 严格单调递增，不复用、不回退。

### 2.2 五个法度字段

| 字段 | 枚举值（封闭白名单，任意其它值一律拒绝） | 默认值 | 取值范围 / 映射成的参数（全整数，无浮点） |
|---|---|---|---|
| `reserve_policy` | `strict` / `balanced` / `loose` | `balanced` | `MinimumReserve = clamp(FixedReserve, 0, InitialStock)`；固定整数表 `strict=60`、`balanced=40`（= 现 `content/world.json:12` 的 `minimumReserve=40`）、`loose=20`，**全整数无 `round`/浮点**。结果域 `[0, InitialStock]` |
| `ration_policy` | `queen_first` / `weakest_first` / `worker_first` / `equal` | `weakest_first` | 进食资格稳定排序键：主键按档位定义，次键恒为 `AntId` 升序；不产生新食物、不改 `HealAmount` |
| `labor_policy` | `balanced` / `haul_first` / `build_first` / `guard_first` | `balanced` | 工种权重表（整数百分比，五种 `scout/hauler/soldier/chef/builder`）：`balanced` 全 100；`haul_first` hauler=125/builder=80；`build_first` builder=125/hauler=80；`guard_first` soldier=125/chef=110/scout=80；其余 100。作用于有效工作/施工进度，不改 `workUnitsPerExperience=10` |
| `defense_policy` | `early` / `measured` / `late` / `never` | `measured` | `ThreatSwitchPercent` ∈ `[0,100]`：`early=25`、`measured=50`、`late=75`、`never=101`（哨兵值，关闭自动转防御，手动 `Attack` 仍有效） |
| `admission_policy` | `fresh_first` / `bulk_first` / `near_first` / `type_first` | `fresh_first` | `core.interaction`(30) 入巢处理顺序的稳定排序键：`fresh_first` 按 `CreditLot.CreatedTick` 升序、`bulk_first` 按 `Quantity` 降序、`near_first` 按到巢切比雪夫距离升序、`type_first` 按 `FoodDefinition.Value` 降序；次键恒为 `ResourceId` 升序。**只改顺序，不改 `FirstAdmissionPoints` 判定本身** |

- `Version`：`int`，初值 `1`，取值范围 `[1, int.MaxValue]`，每次成功应用 `+1`。
- `AppliedTick`：`long`，取值范围 `[0, world.Tick]`，取应用那一刻的 `world.Tick`。
- `Reason`：`string`，长度 `[1,128]`，UTF-8；迁移初值固定 `migrated-from-save5`。
- 五个字段**必须全部在白名单内**；`InstitutionState.Validate()` 失败统一抛 `Invalid institution state.`，与 `WorldConfig.Validate()` 的 `Unsupported world configuration.` 风格一致但不混用。

## 3 · 版本化 / 热更新 / 回滚 / requestId 幂等

- **热更新**：`SetInstitution` 一次只改一个字段（`field` + `tier`），或一次提交五个字段的原子替换（契约允许两种，但同一 `requestId` 内只能有一种形态）。成功条件：①`field` 在 §2.1 五字段内 ②`tier` 在该字段 §2.2 枚举内 ③`expectedRevision` 等于当前 `revision` ④`expectedWorldId` 等于当前 `WorldId`。成功即 `Version+1`、写 `AppliedTick`/`Reason`、把新快照压入 `History`，**在 Tick 边界生效**（与 `ApplyProgram` / `RollbackProgram` 同一激活点，见 `GameSession.cs:76-164`）。
- **回滚**：`RollbackInstitution` 带 `TargetInstitutionVersion`（`int`，必须存在于 `History` 或为当前 `Version` 的前序版本）。回滚**复制目标历史快照的五个字段**，再 `Version+1`、`Reason` 记为 `rollback-to-{target}`；不删除历史、不重排历史、不回拨 `Version`。回滚是「发布一个新版本」，不是「把时间倒回去」。
- **幂等**：复用程序提交那套入口 —— `GameSession.ExecuteAsync` 以 `GameCommand.RequestId`（`Guid`）去重，同一 `requestId` 重放返回首次结果且不重复 `+1`、不重复压 `History`；`expectedRevision` 防并发；`expectedWorldId` 防错槽。这与 `Mcp/Program.cs:69` `get_actions` 输出的 workflow 文本一致：`observe; command with observed revision and new UUID requestId; identical retries reuse UUID`。
- **精确错误串**（S1 实现固定，不得改写）：
  ```
  unknown_institution_field
  invalid_institution_tier
  institution_version_not_found
  institution_rollback_conflict
  institution_reason_too_long
  duplicate_request_id
  ```
  另沿用现有 `ExpectedRevision` / `ExpectedWorldId` 校验失败串，不另起别名。
- **防刷分**：热更新与回滚都只改 `Institution` 结构，禁止 `FoodStore.Add` / `Economy.Created` / `FirstAdmissionPoints` 写入；断言落总纲 `contract/no-farming/rollback-does-not-mint`。

## 4 · 每字段被哪条规则以什么 Order 消费（冻结映射表）

制度**不新增规则**，下表全是「扩展现有规则的读取逻辑」，Id 与 Order 逐字沿用总纲 §3；`InstitutionParameters` 由 `GameSession.Advance` 在 Tick 前置注入，各规则只读。

| 字段 | 消费规则 Id | Order | 消费方式（读哪个参数） | 禁止事项 |
|---|---|---|---|---|
| `reserve_policy` | `core.eating` | **32** | 巢穴取食分支读 `MinimumReserve`（现 `ColonyRules.cs:5-39` 已受 `world.Config.MinimumReserve` 约束，改为读本 Tick 快照） | 不改 `FoodStore.Take` 的 `CreditLot` 拆分语义 |
| `reserve_policy` | `core.claim-cleanup` | **46** | 低储时缩短认领超时回收窗口（整数 Tick 阈值） | 不回收已入巢 `CreditLot` |
| `reserve_policy` | 命令层 `GameSession`（非 IWorldRule，不占 Order） | — | `TechnologyProgression.Pay` / `MinimumReserve` 校验读同一参数，保证「制度档位」与「升级扣费」同一口径 | 不绕过 `Pay` 的原子预留 |
| `ration_policy` | `core.eating` | **32** | 用 `EligibleAnts` 稳定排序键（主键档位、次键 `AntId` 升序）决定本 Tick 谁先吃 | 不新增食物、不改 `HealAmount`、不改 `EatingTicks=40` |
| `ration_policy` | `core.claim` | **16** | 认领冲突时按同一排序键裁决 | 不越过 `LocalObservationRule` 投影 |
| `labor_policy` | `core.work` | **31** | 工种权重乘 `WorkTicksRemaining` 的有效进度（整数，固定点余数继续存档，沿用 `docs/ANT_LEVELS_CONSOLE.md`） | 不改 `SpeciesProgramCatalog`、不给小组发私有制度 |
| `labor_policy` | `core.production-actions` | **30** | `process(site,recipe)` 并发槽按 `chef` 权重缩放 | 混合配方输入仍须原子预留 |
| `labor_policy` | `core.nest-construction` | **33**（S2 新增） | 施工进度按 `builder` 权重缩放；S1 阶段该规则尚不存在，参数留空不报错 | 不提前依赖 S2 结构 |
| `defense_policy` | `core.queen-communication` | **15** | `ThreatSwitchPercent` 决定 `enemy_seen` 是否升级为告警事件（`docs/AGENT_EVENTS.md` 既有事件种类） | 不新增事件种类、不写全图敌情 |
| `defense_policy` | `core.combat` | **25** | 威胁占比 ≥ 阈值时把 ` guard` 任务权重前置（读快照，不改 `AntAction.Attack` 手动语义） | 不自动替 Agent 改 DSL、不改 `EnemyDamage=2` |
| `defense_policy` | `core.follow` | **19** | 转防御时 `FollowRule` 的跟随目标优先级读同一阈值 | 不越过 `core.local-observation`(5) |
| `admission_policy` | `core.interaction` | **30** | 入巢 `CreditLot` 处理顺序稳定排序键（§2.2），次键 `ResourceId` 升序 | **不得**改变 `FirstAdmissionPoints` 每个 `CreditLot` 只计一次的判定 |
| `admission_policy` | `core.claim` | **16** | `claim_food` 认领优先级与入巢顺序同键 | 不重复计分、不重复 `Economy.Created` |

- 其余现有规则（`core.enemy-spawn` 0 / `core.source-growth` 2 / `core.local-observation` 5 / `runtime.program` 10 / `core.activity-sampling` 18 / `core.move` 20 / `core.enemy-move` 21 / `core.resource-refresh` 40 / `core.discovery` 45 / `core.exploration-experience` 47 / `core.ant-experience` 48 / `core.economy-statistics` 49 / `core.milestones` 50）**不消费制度参数**，S1 不得顺手改它们。
- `core.local-observation`(5) 仍负责把 `Institution` 的可见投影写进 `Snapshot`：仅返回 `Version` 与五字段档位名，**不返回 `History` 全文、不返回其它巢穴/野怪数据**（总纲 §2.4）。

## 5 · DSL 签名与白名单位置

`src/AntMind.Programs/AntDsl.cs` 的编译器常量不变：`MaxSourceLength=8192 / MaxNodes=512 / MaxNesting=8 / MaxRepeat=16 / MemorySlots=16`（`:6-11`）。

| 名称 | 形态 | 参数 | 白名单位置 | 精确报错文本 |
|---|---|---|---|---|
| `set_institution` | DSL 只读意图语句（编译期合法，写 `Decision`，不改 `InstitutionState`、不产生全局写入） | 2：`field`(string 字面量，§2.1 五字段之一)、`tier`(string 字面量，该字段 §2.2 枚举之一) | `Actions`(:511) **追加**；参数个数写进 `DslCall` 表(:648) 的 2 参分支 | 参数个数不符 `Action 'set_institution' expects 2 argument(s).`；字段非法 `Invalid institution field '{name}'.`；档位非法 `Invalid institution tier '{tier}' for '{field}'.` |
| `institution` | 表达式函数（只读量） | 1：`field` | `ExpressionFunctions`(:510) **追加**；参数个数写进 `DslGroupValue`/`DslCall` 表(:648) 的 1 参分支 | 参数个数不符复用 `:649` 现模板 `Function 'institution' expects 1 argument(s).`；字段非法 `Invalid institution field '{name}'.`；未知名走 `:644` `Unknown expression function '{identifier.Text}'.` |
| `institution_version` | Builtin（只读量，无参） | 0 | `Builtins`(:499) **追加** | 未知名走 `:653` `Unknown variable '{identifier.Text}'.` |

- 返回类型：`institution(field)` 返回档位名字符串（DSL 内以枚举常量比较，不返回自由文本）；`institution_version` 返回 `int`。
- 动作合法性仍走 `:558` `Unknown action '{identifier.Text}'.` 的同一套查表机制；白名单是**封闭**的，未登记的字段/档位一律在编译期拒绝，不进运行时。**编译通过不表示制度生效**：`set_institution` 只写 `Decision` 只读意图，不改 `InstitutionState`、不产生全局写入；全局制度写入唯一入口是 `CommandKind.SetInstitution` / `RollbackInstitution` 经 MCP `command` + Runtime actor + `requestId` 幂等，DSL 语句与命令层不得混名。
- `GroupValue`(:181-195) 不新增成员；制度不是小组数据。
- 五处同步见总纲 §4：DSL 改完必须同步 `content/world.json` 的 `institutions` 默认段、`Protocol.cs` `CommandKind`、`Mcp/Program.cs`、Checks。

## 6 · MCP 工具与协议签名

- `src/AntMind.Contracts/Protocol.cs:137` `CommandKind` **追加到枚举尾部**（不打乱旧值，顺序与总纲 §4 一致）：`SetInstitution, RollbackInstitution`（其余 4 个属 S2，见巢穴分册）。
- `GameCommand`(:138-155) 追加字段（全部可空/带默认，保持旧构造兼容）：
  ```
  string InstitutionField = "",
  string InstitutionTier = "",
  int TargetInstitutionVersion = 0,
  string? ExpectedWorldId = null   // 已有字段，沿用不重复声明
  ```
- `src/AntMind.Mcp/Program.cs:107` `Command(...)` 追加可选参数 `string institutionField = ""`、`string institutionTier = ""`、`int targetInstitutionVersion = 0`；`Enum.TryParse<CommandKind>(kind,false,out var parsed)` 失败仍抛 `Unknown command kind.`；`Guid.Parse(requestId)`、`expectedRevision`、`expectedWorldId` 语义不变。
- **新增只读工具** `inspect_institution`（`[McpServerTool]`，与现有 15 个工具同文件）：无写参数，返回 `{ version, appliedTick, reason, fields{五字段}, historyCount }`；**不返回 `History` 全文、不返回全图**，与 §4 末段一致。工具总数由 15 → 16（S1），`get_actions`(:69) 的 `protocolVersion` 输出随总纲 §5 升到 `19`。
- 幂等/错误串见 §3，`configure_group`(:47) 与 `PreviewProgram` 系列不因本分册改动。

## 7 · 存档字段与迁移

- `WorldState` 新增 `InstitutionState Institution`（总纲 §5，字段名 `Institution`），随 `SavedGames.Format 5→6` / `Protocol.Version 18→19` 一起升。`SavedGame` 记录本身形状不变（仍是 `SavedGame(int Format,int Protocol,WorldState World,ImmutableArray<SavedProgram> Programs,int Speed=1)`，`SavedGames.cs:9-14`）。
- `SaveMigration.Upgrade` 追加一级 `Format=5/Protocol=18 → Format=6/Protocol=19`（总纲 §5 第 3 步，逐字一致）：
  ```
  Institution = {
    version = 1,
    applied_tick = save.World.Tick,
    reason = "migrated-from-save5",
    reserve_policy = "balanced", ration_policy = "weakest_first",
    labor_policy = "balanced", defense_policy = "measured",
    admission_policy = "fresh_first",
    history = [ 该初始版本自身的 InstitutionRevision ]
  }
  ```
  **不掷新随机数、不读写 `RandomState`、不补任何资源**（总纲 §2.1/§2.2）。原档不落盘改写，沿用 `SaveMigration.cs:8` 注释 `v1 files remain untouched; load upgrades an immutable candidate before any live mutation.`。
- `NormalizeLegacyJson`(:49) 继续只处理已知旧字段改名（`FiberCost`→`MaterialCost`），**不**为 `Institution` 新增分支。
- `SavedGames.Validate`(:35) 增补：`Institution.Version ≥ 1`、五字段全在 §2.2 白名单内、`History.Length ≤ 32`、`AppliedTick ≤ World.Tick`、`Reason.Length ∈ [1,128]`；失败仍走既有 `Unsupported save format.` 之外的字段级异常，不混用标签。
- 兼容矩阵：`wire18/content15/save5`（含 wire12–17、save1–4）→ 内存升级 `wire19/content16/save6`。

## 8 · Checks 断言（并入现有单一计数器）

`tools/AntMind.Checks/Program.cs:9` 的 `Check(bool,label)` 计数器不变，输出仍是 `:502` 的单一 `All {checks} architectural checks passed.`。本分册新增断言 label（与总纲 §2 的 `contract/...` 断言并列、不重名）：

```
institution/whitelist/closed-enum
institution/defaults/match-world-json
institution/version/monotonic
institution/version/history-cap-32
institution/rollback/restores-snapshot
institution/idempotency/request-id-replay
institution/params/pure-function
institution/params/no-randomstate-read
institution/no-score/first-admission-unchanged
institution/no-score/created-items-unchanged
institution/order/no-new-rule
institution/visibility/history-not-in-snapshot
institution/migration/save5-defaults
institution/migration/original-bytes-untouched
```

另直接复用总纲断言：`contract/no-farming/rollback-does-not-mint`、`contract/conservation/single-ledger`、`contract/determinism/independent-subseeds`（制度侧不参与子种子，断言其不改写）、`contract/visibility/player-equals-agent`。

## 9 · 锚点标定题（烂 / 中 / 优 三档齐）

题名 `institution-reserve-crunch`，`scenarioId` 固定不带日期，`contractVersion="0.1.0"`，`axes=["institution"]`，`layers=["baseline","+institution","all"]`，`ticks` 同时跑 300 与 1200 两档（对照历史「300 步全场 0」「1200 步官方默认 8 分」），`seed` / `promptSha256` / `slot` 三档**完全相同**，只换 `strategyId`（同题对照，总纲 §7）。

| 档 | `strategyId` | 参考策略（写进题面的可判行为） | 预期 |
|---|---|---|---|
| 烂 | `poor` | 全程不读 `institution(...)`、不下发 `CommandKind.SetInstitution`；或每 Tick 乱下发制度命令导致 `expectedRevision` 冲突与重复 `RollbackInstitution` | 低储期断粮/无效回滚，L3 写对率与得分垫底 |
| 中 | `medium` | 只在 `stock` 低于单一阈值时经 MCP `command` 下发一次 `CommandKind.SetInstitution` 把 `reserve_policy` 切到 `loose`，其余字段不动 | 能避开断粮，但不会按威胁组合制度命令，中段 |
| 优 | `good` | 读 `institution(reserve_policy)` / `institution_version` 与事件流，分阶段组合下发 `CommandKind.SetInstitution`（`reserve_policy` + `defense_policy` + `ration_policy`），版本冲突时用同一 `requestId` 重放、必要时 `RollbackInstitution` 到已知好版本 | 三档中 L3 写对率与 `FirstAdmissionPoints` 最高 |

**怎么判一道题能不能用（作废判据，逐字沿用总纲 §8.5）**：三档在同一 `scenarioId` 上的 L3 写对率差值低于该题预设最小可分阈值，或三档 `FirstAdmissionPoints` 全部相同（含同为 0）→ **该题直接作废**，不得靠调分母、改计分口径或换题目难度来「救」。L5 开放题单列，不并入 L3；能力结论须 `grading.crossPipelineRecheck=true` 跨管道独立复核后才可写入文档，并留存原始输入与完整方法（总纲 §7）。

## 10 · 与其它分册的边界

- `nest-layout-contract-0.1.0.md`：`labor_policy` 会读 S2 的 `core.nest-construction`(33) 权重，但 S1 不得提前依赖 S2 结构；`admission_policy` 与粮仓容量无关，粮仓加成见巢穴分册。
- `creature-contract-0.1.0.md`：`defense_policy` 的威胁占比输入来自野怪可见投影，但制度不定义 `CreatureKind`、不读 `CreatureSeed`、不改 `EnemyView`。
- 冲突裁决顺序：总纲 §2 不变式 > 总纲 §3 Order 表 > 总纲 §5 版本表 > 本分册。
