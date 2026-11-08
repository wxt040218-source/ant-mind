# ANT MIND · 蚁智

给外部 Agent 玩的像素风蚁群经营游戏。玩家提出方向，Agent 编写职业程序、组织小组、安排生产与升级，蚁群在真实模拟中执行并显示结果。

当前开发版本 **v0.13**：Windows x64，.NET 10 / C#，Avalonia 原生桌面，无游戏引擎。wire=12、content=10、save=1。

## 目前可以做什么

- 侦察蚁、搬运蚁、兵蚁和厨师蚁：每蚁种一份共享受限 DSL、个体记忆和决策记录，支持热更新、版本与回滚。
- 自然水果、战斗掉落肉、可再生果丛、有限纤维源；发现权限对玩家和 Agent 一致，未知对象隐藏。
- Agent 自定义混编小组，以参数和命名地点安排采集、运输、守卫、建设和厨房工作。
- 实运材料建中转箱和加工台；厨师按代码加工水果浓缩／蛋白餐，全部转运与劳动均显式执行。
- 通用卵孵化、载量与大脑／蚁群网络升级，战斗、受伤和显式进食回血。
- 128×128 地图，像素职业形象，缩放／平移、对象观察、程序与生产收益面板。
- 完整手动存读档和同检查点隔离评估；首次入巢来源计分，防止反复倒运刷收益。

游戏不限时，不结算离线发展。能力模块、昼夜环境和跨区域开拓仍为后续计划。

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
| [当前游戏规格](docs/MVP_SPEC.md) | 全部当前玩法、数值、存档与限制 |
| [生产扩展交接](docs/PRODUCTION.md) | 厨师／物流／加工代码入口与实测证据 |
| [Agent 接口](docs/AGENT_API.md) | MCP 工具和命令 |
| [职业程序 DSL](docs/ANT_DSL.md) | 感知、预算、显式动作和生产查询 |
| [框架说明](docs/FRAMEWORK.md) | 运行、规则注册与分层 |
| [后续实施规划](docs/EXPANSION_IMPLEMENTATION_PLAN.md) | 已交付首个 M0～M3 闭环，M4／M5 待开发 |
| [游戏内核调研](docs/GAME_KERNEL_RESEARCH.md) | 候选机制与官方来源 |
| [协作约定](CONTRIBUTING.md) | 修改与验证要求 |

旧版本规格保留作历史，不代表当前状态。运行资料 artifacts、编译产物、存档与个人配置不上传仓库；其他开发者按入口文档自行复验。

## 验证

本地全解决方案构建 0 警告／0 错误，244 项检查通过，涵盖守恒、预算、战斗、来源计分、完整恢复及真实 STDIO MCP。实际 Agent 用自然材料在 T201 建箱、T592 建加工台，T937 送回 1 份水果浓缩、计 2 点；相同存档两条分支评估一致。详见 [验证记录](docs/PRODUCTION.md)。

这不是长期平衡或满人口性能结论。厨师帽是临时像素形象，精细动画和正式厨师美术待补。公开仓库尚未选择开源许可证；代码与素材的使用许可待共同开发者约定。
