> v0.19：中转站升级为仓储与食物补给据点，保留稳定cache节点供以后运输系统使用；eat_from与本地补给查询见 [OUTPOSTS.md](OUTPOSTS.md)。wire18／content15／save5，兼容已知save1～4旧档。

> v0.18：wire17／content14／save4；标准供给目标使用首次入巢点，新增整群当前／激活状态预检和重复程序告警汇总／恢复。完整说明见 [PRIORITY_FIXES_V018.md](PRIORITY_FIXES_V018.md)；下方旧版本记录须对照当前入口。


> v0.16：先读 BUILDINGS_WOOD.md。所有新蓝图需木材与建筑蚁，旧纤维工地保留；厨师及建筑／甲虫透明PNG已接入。

# 给接手 Agent 的入口

当前版本v0.19，更新2026-11-11。本次增加中转据点仓储与食物补给回血，460项检查及实局施工、运输、治疗通过；保留v0.18供给目标修复、整群预检及告警汇总。请把本页、当前代码和当前状态作为事实来源；版本归档、研究文档和实施草案不代表全部已经实现。

## 先读这些

1. 根目录 AGENTS.md：项目工作约定。
2. PROJECT_STATUS.md：已完成、真实测试和未完成项，开始及结束任务均核对更新。
3. docs/MVP_SPEC.md：当前玩法、初值和边界，wire=18 / content=15 / save=5。Agent事件／诊断和新DSL详见docs/AGENT_EVENTS.md。
4. docs/BUILDINGS_WOOD.md：木材、专职建筑蚁、地图蓝图放置、save1～3迁移和新美术。
5. docs/ANT_LEVELS_CONSOLE.md：个体军衔、经验、F12 和命令行调试。
6. docs/TECH_FOOD_VALUE.md：当前五级科技、价值支付、混合食材与旧存档迁移。
7. docs/PRODUCTION.md：最新源头、物流、加工、厨师、来源计分和存档的代码入口及验收证据。
8. docs/FRAMEWORK.md、docs/LUNA_HANDOFF.md、docs/AGENT_API.md、docs/ANT_DSL.md：架构和接口。早期章节与当前规格冲突时，以本页和当前代码为准。

本项目没有游戏引擎或网页端，主程序是 Windows x64 Avalonia 桌面。不要恢复 Godot，不要把 Core 写成 UI 代码，也不要自动开启其他 Agent 或替用户切换模型。

## 构建和启动

需要 Windows x64、.NET SDK 10.0.403（global.json 锁定）。本机若有 .tools/dotnet 则脚本优先使用，否则使用系统 dotnet。初次克隆需要 NuGet 网络。

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT='1'
.\scripts\dotnet-local.ps1 restore AntMind.sln --locked-mode
.\scripts\dotnet-local.ps1 build AntMind.sln --no-restore '-m:1' '-p:UseSharedCompilation=false'
.\scripts\dotnet-local.ps1 run --project tools/AntMind.Checks --no-build
.\scripts\dotnet-local.ps1 run --project src/AntMind.Desktop --no-build
```

测试包含本机命名管道和真实 STDIO MCP 子进程，需要当前用户 IPC 权限。不要把进程启动等同窗口验收或当前聊天已原生装载 MCP。修改共享 DLL 前关闭运行宿主，先保存已有世界。

可用 `tools/AntMind.Evaluator -- --serve` 启动无界面宿主，规则来自同一 GameRules。桌面和无界面默认均占 antmind-v1，不能同时使用相同管道；隔离实例须为宿主和 MCP 设置同一个独立 ANTMIND_PIPE。

## 怎么实际玩

项目没有运行中的永久 Agent 服务。游戏独立执行最后有效的各职业程序，Agent 只在用户任务内调用工具。官方 MCP 桥 src/AntMind.Mcp 共15工具：get_actions、get_catalog、observe、observe_summary、inspect_object、inspect_group、validate_program、preview_program、preview_program_batch、command、configure_group、watch_events、read_events、list_saves、evaluate_save。

当前开发会话未原生装载游戏工具时，可以使用 Python 3 的 `scripts/game-agent.py`，它实际调用官方 STDIO 服务，并连接已经运行的宿主；没有启动第二个模拟世界，也不读 player 全图。

```powershell
python scripts/game-agent.py observe --output artifacts/agent-live/observe.json
python scripts/game-agent.py summary --output artifacts/agent-live/summary.json
python scripts/game-agent.py watch --watch-action upsert --watch-id danger --name "Queen perimeter" --kinds enemy_entered_zone,queen_attacked,ant_died --radius 10
python scripts/game-agent.py events --watch-id danger --after 0 --wait 30 --output artifacts/agent-live/events.json
python scripts/game-agent.py apply --species chef --source content/programs/chef.ant --reason '更新厨师策略'
python scripts/game-agent.py command --kind SaveGame --key checkpoint --reason '策略修改前保存'
python scripts/game-agent.py command --kind LoadGame --key checkpoint --reason '恢复已验证检查点'
```

完整观测 JSON 写在 --output，终端仅显示摘要。observe 读取 WorldId、Revision、当前程序和资源；新命令使用新 UUID 和观测的版本。相同请求只有相同载荷可重试。加载后 WorldId 改变，旧命令必须拒绝。UI、MCP、脚本不能直接改 world。

`python scripts/production-demo.py` 只接受 T0 暂停新局：保存起点，通过官方工具自行建组、编写共享搬运程序、采材料、建箱／加工台、让厨师加工并入巢，完成后保存 production-demo。它会修改新局程序和组，是可复验验收演示；不会自动重置已有经营局。

## 不能破坏的游戏约定

- 每个蚁种全群共享一份代码，小组只有自由任务数据、参数和地点；不能恢复组程序覆盖或让玩家手工安排每只蚂蚁。
- 所有移动、拿取、放下、采集、交付、通信、攻击、进食、建设和加工都由个体程序明确调用；管理 API 只规划，不传送材料。
- 局部感知；玩家和 Agent 都不能看未知食物／源头／敌人。视野外是最后观察缓存，显式上报／下载；未知与不存在 ID 查询相同。
- Core / Programs / Contracts 无 UI / MCP / 磁盘依赖；Runtime actor 是实局唯一写入者；规则使用 IWorldRule，稳定排序，20Hz，每格 1000 整数单位。
- 数量只在实际持有者；来源点伴随全部转移。ItemLedger 只汇总，不建第二份库存。收益用 FirstAdmissionPoints，不能用重复搬运 Delivered。
- 完整档保存隐藏真值、RNG、程序和缓存；公开观测不能当存档。恢复前编译校验，失败不得改当前局。停止期间不结算收益。
- 新配方、来源、行为、存档字段必须同步真实规则、内容校验、协议／文档和必要检查；不要只加按钮或空接口。

## 已验证和仍待完成

v0.17历史验收425项检查通过、全解决方案0警告／0错误。v0.18本轮验证以PROJECT_STATUS.md为准。v0.17实际桌面成功载入wire15旧档，在暂停T12007验证敌情入圈提醒、游标续读／超时、只读候选预览；真实MCP并发等待不阻塞命令且条件变化能唤醒。详细证据与会话边界见AGENT_EVENTS.md；当前运行状态以PROJECT_STATUS.md为准。

经济和生产先前验证继续有效，详细证据与限制见 ANT_LEVELS_CONSOLE.md；本轮未做长期满人口成长平衡或每一个命令的人工验收。

自动检查、原图与相机检查、真实 MCP、窗口目视分别记证据，不能混称完整玩法或性能证明。artifacts／bin／本机存档不上传 Git，其他开发者应自行运行检查与演示复验。

尚未实现：身体能力模块、昼夜／环境、开拓、离线发展、自动存档、未知／更早格式迁移、持久化完整事件回放、长期墓碑压缩和完整世界观测分页（事件已有分页）。满人口长时性能与多种子生产平衡待做。v0.16已接入独立厨师、建筑蚁、设施和甲虫插画。下一步先验证事件驱动经营与物流规模，再推进 M4／M5；完整候选需求在 EXPANSION_IMPLEMENTATION_PLAN.md。

当前新接口：get_catalog 含科技阶段及完整解锁列表；Snapshot.DevelopmentValue 负责可支付价值、Inventory 仍为份数。recipe_unlocked 与 technology_level 读取蚂蚁显式请求的科技缓存。厨房／厨师二级，四级开始多输入配方，详情以 TECH_FOOD_VALUE.md 为准。本轮修改在本地，旧 GitHub CI 记录只对应 v0.13。

当前个体等级不改变共享程序归属。inspect ant 与 ants 查询可看 Level／Experience／CompletedWork；DSL 可按 ant_level、experience、experience_to_next 和自身属性选择策略。新的 CLI 项目使解决方案共10个项目，依赖图仍保持核心隔离。当前save5接受已知save1～4，原存档不改写。
