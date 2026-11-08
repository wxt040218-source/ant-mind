> 2026-11-08 v0.13 当前实现：wire=12、content=10、save=1。新增厨师蚁、类型来源 CreditLot、ItemLedger 只读守恒汇总、果丛／纤维源、实体施工／中转／转交、加工台两配方、完整存读档与隔离评估。组合根统一在 Runtime/GameRules.cs，GameSession 与 evaluate_save 均使用；源头生长 2、活动采样 18、生产交互 30、工作 31、进食调整为 32、统计 49。动作显式调用，所有规划交给 Agent。新版事实以 MVP_SPEC.md、PRODUCTION.md 为准，下方早期“未实现存档”等历史描述已过时。

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

# 核心框架使用

从仓库根目录 PowerShell 运行：

```powershell
.\scripts\dotnet-local.ps1 restore AntMind.sln --locked-mode
.\scripts\dotnet-local.ps1 build AntMind.sln --no-restore
.\scripts\dotnet-local.ps1 run --project tools/AntMind.Checks --no-build
.\scripts\dotnet-local.ps1 run --project src/AntMind.Desktop --no-build
.\scripts\dotnet-local.ps1 run --project tools/AntMind.Evaluator --no-build -- 100
.\scripts\dotnet-local.ps1 run --project tools/AntMind.Evaluator --no-build -- --serve
```

含冒号的 MSBuild 参数通过 PowerShell 脚本转发时请整体引用，例如 '-p:UseSharedCompilation=false'。

桌面端和 --serve 默认占用 antmind-v1 管道，不能同时占用。多个实例给宿主与对应 MCP 设置相同且独立的 ANTMIND_PIPE 环境变量。

MCP 工具返回版本化 JSON 文本；玩家与 Agent 均只能读取已发现食物；Visibility 已停用。MCP 客户端启动 dotnet，参数为 src/AntMind.Mcp/bin/Debug/net10.0/AntMind.Mcp.dll 的绝对路径。stdout 专用于官方协议，日志写 stderr。先 observe，再以 observed revision、新 UUID 提交 command。冲突后重新读取并使用新 UUID。重试同 UUID 必须所有字段一致。以前已提供的全图信息无法从外部模型记忆中撤回，严格测试应新开局。

## 扩展入口

- content/world.json：schemaVersion=9，启动验证，未知字段与错误配置拒绝；包含 Scout／Hauler 属性配置。
- IWorldRule：返回新 WorldState，显式传入 GameSession 的 extraRules；固定顺序执行。
- IAntProgram：仅接收个体 Perception、预算及按距离过滤的蚁后信息。Scout 和 Hauler 的内置策略以受限 DSL 实际编译和执行；每种类独立程序历史、Tick 边界替换和回滚由 Runtime actor 管理。
- Ant DSL：最多 8192 字符、256 个语法节点、8 层嵌套、固定上限循环、16 槽个体记忆；语法、变量、动作和预算规则见 [ANT_DSL.md](ANT_DSL.md)。
- Snapshot/GameCommand：新界面不必依赖 Avalonia，也不能复制模拟逻辑。
- 新 Agent 复用 MCP 或同一受控 IPC，不能直接读取世界真值。
- 新协议操作先扩展 DTO、Runtime 校验和架构检查，再接入 UI/MCP。

双蚁种采运、按种类增员、资源补充、库存经济、五级共享大脑、蚁后通信、文本 DSL、程序版本／回滚和个体决策记录已实现。手动存档和事件日志仍待开发；命令 receipts 仅在会话内有效。配置、规则和所有权决定未来行为，UI 只显示结果。

## 发布

```powershell
.\scripts\dotnet-local.ps1 publish src/AntMind.Desktop -c Release -r win-x64 --self-contained true -o artifacts/desktop
```

发布输出包括 content/world.json。不裁剪，不合并单文件。进程启动、窗口可见和交互分别记录验证，不混称完整游戏通过。

发布后运行 scripts/check-desktop.ps1 可隐藏启动发布进程并检查 IPC 单步；不做视觉验收。Windows 运行时还原图由 Directory.Build.props 中的 RuntimeIdentifiers 统一声明。
