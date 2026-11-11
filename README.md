> v0.19：中转据点增加真实食物补给回血和局部查询，见 [OUTPOSTS.md](docs/OUTPOSTS.md)。wire18／content15／save5。

# ANT MIND · 蚁智

给外部 Agent 玩的像素风蚁群经营游戏。玩家提出方向，Agent 编写职业程序、组织小组、安排生产与升级，蚁群在真实模拟中执行并显示结果。

当前开发版本 **v0.19**：Windows x64，.NET 10 / C#，Avalonia 原生桌面，无游戏引擎。wire=18、content=15、save=5。

建筑规则与旧档迁移见 [BUILDINGS_WOOD.md](docs/BUILDINGS_WOOD.md)，美术提示词见 [ART_V016_PROMPTS.md](docs/ART_V016_PROMPTS.md)。

## 目前可以做什么

- 地图角色交互时挤压回弹，交付有食物飞行粒子；采集、施工、战斗和回血有独立反馈，保留像素形象和原有地图间距。
- Agent 可监控敌人进入指定警戒圆、受击／死亡、施工／加工、任务接收和连续失败；支持分页续读及最多30秒等待。精简摘要、完整小组原子配置、任务版本回执和候选程序预览方便调试。
- 自由角度移动，职业程序通过 `set_target(x,y)` 设置目的地，引擎持续前进；可查询目标与到达状态，旧 `move_to` 兼容。

- 每只蚂蚁独立三级成长：探索、采集、交付、劳动与战斗获取经验，晋级提高身体能力；游戏内 F12 与命令行调试控制台可查看和操作当前世界。

- 虫群网络统一五级科技：二级厨房／厨师，三级蛋白餐，四级混合营养餐，五级盛宴；高级食物有更高发展价值和回血效果。升级按价值精确支付，找零留存，原存档可迁移。

- 侦察蚁、搬运蚁、兵蚁、厨师蚁和建筑蚁：每蚁种一份共享受限 DSL、个体记忆和决策记录，支持热更新、版本与回滚。
- 自然水果、战斗掉落肉、可再生果丛、缓慢再生木材源（保留旧档纤维）；发现权限对玩家和 Agent 一致，未知对象隐藏。
- Agent 自定义混编小组，以参数和命名地点安排采集、运输、守卫、建设和厨房工作。
- 地图放置中转站／厨房蓝图，实运木材并由建筑蚁专职施工；厨师按代码加工水果浓缩／蛋白餐，全部转运与劳动均显式执行。
- 通用卵孵化、载量与大脑／蚁群网络升级，战斗、受伤和显式进食回血。
- 128×128 地图，像素职业形象，缩放／平移、对象观察、程序与生产收益面板。
- 完整手动存读档和同检查点隔离评估；首次入巢来源计分，防止反复倒运刷收益。

游戏不限时，不结算离线发展。身体能力模块、昼夜环境和跨区域开拓仍为后续计划。

## 启动

需要 Windows x64 和 .NET SDK 10.0.403。仓库包含锁定依赖文件，初次还原需要网络。

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT='1'
.\scripts\dotnet-local.ps1 restore AntMind.sln --locked-mode
.\scripts\dotnet-local.ps1 build AntMind.sln --no-restore '-m:1' '-p:UseSharedCompilation=false'
.\scripts\dotnet-local.ps1 run --project tools/AntMind.Checks --no-build
.\scripts\dotnet-local.ps1 run --project src/AntMind.Desktop --no-build
```

桌面暂停启动，自带默认代码。外部 Agent 通过官方 STDIO MCP 服务连接游戏；也可使用 Python 3 的 `scripts/game-agent.py` 调用同一桥接。界面显示连接状态，桥接可用不代表每个聊天已原生装载工具。

## 给接手的 Agent 与开发者

**先读 [Agent 入口](docs/AGENT_START_HERE.md)**，再看 [项目状态](PROJECT_STATUS.md)。两者写明当前事实、运行方式、规则边界和下一步。

| 文档 | 内容 |
|---|---|
| [Agent 事件与诊断](docs/AGENT_EVENTS.md) | 敌情区域监控、游标／等待、任务回执、原子配置与预览 |
| [当前游戏规格](docs/MVP_SPEC.md) | 全部当前玩法、数值、存档与限制 |
| [个体等级与控制台](docs/ANT_LEVELS_CONSOLE.md) | 三个军衔、经验结算、F12／CLI 使用和旧档兼容 |
| [科技与食物价值](docs/TECH_FOOD_VALUE.md) | 五级解锁、混合配方、精确支付与旧档兼容 |
| [生产扩展交接](docs/PRODUCTION.md) | 厨师／物流／加工代码入口与实测证据 |
| [Agent 接口](docs/AGENT_API.md) | MCP 工具和命令 |
| [职业程序 DSL](docs/ANT_DSL.md) | 感知、预算、显式动作和生产查询 |
| [框架说明](docs/FRAMEWORK.md) | 运行、规则注册与分层 |
| [后续实施规划](docs/EXPANSION_IMPLEMENTATION_PLAN.md) | 已交付首个 M0～M3 闭环，M4／M5 待开发 |
| [游戏内核调研](docs/GAME_KERNEL_RESEARCH.md) | 候选机制与官方来源 |
| [协作约定](CONTRIBUTING.md) | 修改与验证要求 |

旧版本规格保留作历史，不代表当前状态。运行资料 artifacts、编译产物、存档与个人配置不上传仓库；其他开发者按入口文档自行复验。

## 验证

本地全解决方案构建 0 警告／0 错误，425 项检查通过，涵盖守恒、预算、战斗、来源计分、完整恢复、事件权限／重放和真实 STDIO MCP 并发等待。实际新版桌面已恢复旧档并验证敌人入圈提醒、等待超时与不改变世界的候选预览。历史生产验证见 [生产记录](docs/PRODUCTION.md)。

这不是长期平衡或满人口性能结论。事件诊断不随存档保存；模型响应需要仍在运行的任务。厨师正式透明插画已接入，精细动画待补。公开仓库尚未选择开源许可证；代码与素材的使用许可待共同开发者约定。
