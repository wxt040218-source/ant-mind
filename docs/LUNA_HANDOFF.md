> v0.19：中转站升级为仓储与食物补给据点，保留稳定cache节点供以后运输系统使用；eat_from与本地补给查询见 [OUTPOSTS.md](OUTPOSTS.md)。wire18／content15／save5，兼容已知save1～4旧档。

> v0.18：wire17／content14／save4；标准供给目标使用首次入巢点，新增整群当前／激活状态预检和重复程序告警汇总／恢复。完整说明见 [PRIORITY_FIXES_V018.md](PRIORITY_FIXES_V018.md)；下方旧版本记录须对照当前入口。

> 当前v0.17：先读 [AGENT_EVENTS.md](AGENT_EVENTS.md)，425检查通过；wire16/content13/save4。Agent事件／游标等待、原子组配置、任务回执、摘要及候选预览已实现；日志非持久事件回放。危险实玩旧局已独立保存并恢复暂停，不自动推进。其他v0.16规则与边界继续适用；下方旧“下一步”须对照最新状态。

> v0.16.1导航更新：`set_target(x,y)` 设置持续目标，任意方向固定点移动；`move_to` 兼容。新增 `has_target`、`target_x/y`、`target_distance`、`target_reached`；详见 [FREE_MOVEMENT.md](FREE_MOVEMENT.md)。消息／存档形状保持。

> 当前v0.16：wire15 / content13 / save4；木材、专职建筑蚁、地图放置和独立透明素材见 [BUILDINGS_WOOD.md](BUILDINGS_WOOD.md)。下面v0.15及更早版本号为历史，个体成长与经济规则继续适用。

> v0.15 当前增量：wire14 / content12 / save3；每只蚂蚁独立三级经验，固定点属性加成与探索去重，规则47/48结算。新增 F12 游戏内控制台及 tools/AntMind.Console，共用受控命令解析；详见 [ANT_LEVELS_CONSOLE.md](ANT_LEVELS_CONSOLE.md)。旧版本说明仍作历史，不把本轮本地验证当成远端 CI。

> v0.14 当前规则：wire=13 / content=11 / save=2。科技就是虫群网络等级；厨房／厨师／浓缩二级，蛋白餐三级，混合营养餐四级，盛宴五级。升级用 DevelopmentValue 精确支付并保存找零，Inventory 仍为实物份数；配方 ExtraInputs 原子预留，来源汇总。完整现行说明和验证见 [TECH_FOOD_VALUE.md](TECH_FOOD_VALUE.md)，下方 v0.13 及更早的单输入、无价值差异、无旧档迁移等说明是历史记录。

> 2026-11-08 v0.13 当前实现：wire=12、content=10、save=1。新增厨师蚁、类型来源 CreditLot、ItemLedger 只读守恒汇总、果丛／纤维源、实体施工／中转／转交、加工台两配方、完整存读档与隔离评估。组合根统一在 Runtime/GameRules.cs，GameSession 与 evaluate_save 均使用；源头生长 2、活动采样 18、生产交互 30、工作 31、进食调整为 32、统计 49。动作显式调用，所有规划交给 Agent。新版事实以 MVP_SPEC.md、PRODUCTION.md 为准，下方早期“未实现存档”等历史描述已过时。

> 下一阶段规划：先读 [EXPANSION_IMPLEMENTATION_PLAN.md](EXPANSION_IMPLEMENTATION_PLAN.md)。这是 2026-11-08 开发前草案，不是已实现接口；从 M0 存档／统计开始，不跳过批次来源、防重复计分、发现权限或配方依赖检查。当前发布玩法仍见 MVP_SPEC.md，后续实现时逐阶段同步协议／存档／内容版本。

> 2026-11-08 v0.12：四张原透明 PNG 在 Assets/Roles，RoleArtwork 由菜单和地图共用；正式地图 content/world.json 为 128×128。MapCamera 为纯本地视野，支持滚轮锚定缩放／拖动／全图／蚁后定位，不改感知和世界状态。协议 11、schema 9 不变。206 项检查通过；相机和素材说明见 MAP_PRESENTATION.md。

> 2026-11-08 v0.11：用户明确要求由 Agent 完成全部规划。小组取消独立程序，仅为任务数据容器；每蚁种一个全群程序槽，组代码／回滚固定拒绝。新增原子 AssignAnts、命名参数／地点增删和 inspect_group；个体通过显式蚁后请求读取缓存，DSL param("key")／group_x/y("point")。UI 仅观察，不要求玩家手动配置。协议 11、内容 schema 9，运行预算保持，静态节点上限 512。以下早期组覆盖说明已被本条取代。

> 2026-11-08 v0.10：分类食物 FoodStore、组内蚁种程序、小组命令、显式进食／跟随／认领；协议 10、内容 schema 9。新增 ClaimRule(16)、FollowRule(19)、EatingRule(31)、ClaimCleanupRule(46)，与原战斗及采运规则一同注册。当前以 MVP_SPEC.md／V0.10_PLAN.md 为准；下面旧版本说明仅供历史参考。Programs 查询必须匹配 GroupId 和 Species，不能再只对 Species 使用 Single。库存只改 Stock，Inventory 为派生总量；init Inventory 仅用于旧夹具初始化，会重置为水果。

> 2026-11-08 v0.9：兵蚁／敌对甲虫／生命与显式攻击／尸体食物／蚁后战败。协议 v9、内容 schema 8。加入新宿主时注册 EnemySpawnRule(0)、EnemyMovementRule(21)、CombatRule(25)，不得省略战斗或只绘制敌人。

> 2026-11-08 v0.8：里程碑规则 Order 50、前置／限时目标、Snapshot.Milestones；Programs 嵌入 content/programs 中的修正策略作为新局默认。协议 v8、内容 schema 7。完整存档未实现。

> 2026-11-07 v0.7：对象库／1～5 食物容量／玩家与 Agent 统一已发现投影／MCP 连接灯。旧 Visibility 停用，Generated 为 null。协议 v7、内容 schema 6，详见 OBJECT_DATA.md 及当前 MVP_SPEC.md；以下较早版本说明仅供演进参考。

> 2026-11-07 v0.6：蚁群网络付费升级、通信范围按等级派生、科技 requiredNetworkLevel 门槛。新增规则必须用 WorldState.IsTechnologyUnlocked 检查相关科技；旧 SetQueenRange 停用。协议／快照 v6、内容 schema 5，以当前 MVP_SPEC.md 为准。

> 2026-11-07 最新数值：搬运容量逐级 1～10；速度按实际负载递减，空载 120、携带 10 份为 60 单位/Tick。侦察蚁空载／载物 200／20。同负载不因容量升级而提速。

> 2026-11-07 v0.5：搬运蚁载量可升级；蚁后信息改为程序显式请求／上报，物理交互为单次操作。协议／快照 v5，内容 schema 4。规则以当前 MVP_SPEC.md、ANT_DSL.md 为准。

> 2026-11-07 蚁后菜单补充：生产通用蚂蚁卵，在对应蚁种菜单消耗卵孵化；API 为 ProduceEgg / HatchEgg，旧 Recruit 已停用，协议／快照版本为 5。

> 2026-11-07 更新：当前实施基线为 [v0.4 蚁后通信](MVP_SPEC.md)，操作接口见 [AGENT_API.md](AGENT_API.md)。信息素规则已取消；Agent 现可调用全部现有玩法按钮，包括 Visibility。

# Luna 开发交接

先阅读 PROJECT_STATUS.md、MVP_SPEC.md 和 FRAMEWORK.md。v0.3 已按用户指示进入实现：侦察蚁与搬运蚁共存，各自运行受限 DSL；双蚁种、种类级程序槽、Tick 边界更新和个体调试已接入。手动存读档、当前 Codex 会话 MCP 接入及人工 UI 验收仍待完成。切换模型不改变玩法、权限及确定性契约。

## 已实现基础

- Contracts：版本化消息、命令、不可变快照；Point 每格 1000 整数单位。
- Core：不可变世界、种子随机、稳定规则顺序、蚁种配置和 ID 校验。移动速度、视野、容量及部分负载惩罚按种类结算；无 UI、IPC、MCP 依赖。
- Programs：局部 Perception、IAntProgram、确定性受限 DSL 与 InstructionBudget；16 槽个体记忆、局部资源／蚁后汇总查询、预算错误和分支记录已接入。
- Runtime：唯一写入者，256 容量 actor 队列；暂停时也处理管理命令；蚁种程序目录由 actor 持有，校验、版本、回滚和 Tick 边界激活均串行化。
- Transport：当前用户命名管道，4 字节小端长度前缀、1 MiB 上限、5 秒连接/请求时限，一连接一请求。
- Desktop：原生 Avalonia 组合根、地图和时间控制。能显示采运、库存、等级、通信范围和蚁种程序；选中蚂蚁时显示目标、记忆、最近决策及错误。完整 MVVM 尚未实现。
- Mcp：官方 C# SDK 2.3.1，STDIO，observe 与 command 返回版本化 JSON 文本；Agent 可显式调用 Visibility 切换可见模式，当前 Codex 会话仍未装载工具。
- Evaluator：指定 Tick 或 --serve 无界面宿主；与 Desktop 使用同一 Runtime，均暂停启动。
- Checks：架构检查和真实 MCP 子进程验收；从仓库根目录运行，Debug 输出须已构建。
- content/world.json：schemaVersion=9；版本化地图、资源、经济、冷却、五级大脑及侦察蚁／搬运蚁原型数值，发布时复制到输出目录。

## 不得破坏的契约

1. 新规则实现 IWorldRule，在组合根显式注册。Order 相同按 Ordinal Id 排序，重复 ID 拒绝。禁止用系统时间、全局 Random 或 UI 帧率驱动规则。
2. 3 格/秒在 20Hz 下为每 Tick 150 坐标单位。浮点仅用于绘制。
3. 个体程序只收到 Perception，不传完整 WorldState。新指令按采样/遍历项收费。
4. 外部写入只经过 GameSession。requestId 只能同载荷重试。ExpectedRevision 冲突后重新观察，使用新 ID 提交。
5. receipts 会话内最多 100000 条，不淘汰。满时拒绝新请求，旧请求仍返回原结果。还未持久化，重启不延续幂等性。
6. 每 Tick 或成功管理命令增加 revision，单步仅增加一次；观察不改变 revision。
7. Agent 已发现记录不从隐藏真值刷新；工蚁再次看见搬空点时移除旧记录，远处旧记录保留最后观察 Tick。
8. 运行时拒绝 Step；暂停允许 Step；倍速只有 1/2/5，不改变规则步长。
9. C# 规则是可信代码；DSL 不能调用任意 C#、网络、文件或系统接口。
10. 协议和未来存档形状变更需明确版本与兼容策略。
11. immutable 规则仍能抛错；宿主失败关闭队列，不能静默继续损坏世界。生产恢复策略尚待定义。
12. MCP 只调用当前信息模式投影，不使用 player=true；Visibility 已停用，不能切换到全图。

## 已完成的 v0.3 编程基础

- Scout 与 Hauler 各有默认 DSL 和独立版本历史；最多 32 个版本，更新只影响指定种类。
- 新版本先静态校验，再排队到下一个 Tick 激活；失败保留旧程序，回滚创建新版本记录。
- 更新目标种类的程序会清空其成员目标和 DSL 记忆，保留位置、负载及世界状态。
- MCP observe 返回蚁种统计、当前程序和版本历史；command 支持按种类提交 DSL、回滚及指定蚁种增员。
- Desktop 提供两种蚁种区分、程序查看、按种类统计和个体最近 20 次决策记录。

外部可信扩展可通过 `GameSession` 的 `extraRules` 参数追加 `IWorldRule`；内置规则仍会运行，最终按 `Order`、再按稳定 `Id` 排序。可复制 [规则扩展示例](RULE_EXTENSION_EXAMPLE.md) 起步。

## 下一阶段

1. M4：版本化手动存档，保存世界、随机状态、程序历史与个体记忆；兼顾 schema 迁移。
2. 评估器对多随机种子、更多数量比例及自定义程序做对照；当前有同种子三种比例入口。
3. 配置实际 Codex MCP 并重连，以工具出现及当前会话真实调用验收。自动子进程检查不等于当前 Codex 会话已加载工具。
4. 人工检查当前 Avalonia 窗口布局与点击蚂蚁操作；当前仅完成构建和自动架构检查。

只对已实现且验证过的内容勾选里程碑。任务结束更新 PROJECT_STATUS.md。扩展优先普通项目引用与明确组合根，不建立无调用方的插件总线。
