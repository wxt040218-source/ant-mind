# 项目开发约定

每次任务开始及恢复时先读取 PROJECT_STATUS.md，核对最新指令与实际代码，再简述当前进度及下一步。阶段变化与结束前更新目标、进度、决定、阻碍、验证、下一步、简短历史及更新时间；只记录已确认事实。

后续开发先读 docs/LUNA_HANDOFF.md 和 docs/FRAMEWORK.md。保持 Core/Programs/Contracts 无 UI/MCP 依赖；世界写入只走 Runtime actor；Agent 不可读取 player 全图投影；坐标使用每格 1000 整数单位。新增规则使用 IWorldRule 和稳定排序。

修改核心后运行解决方案构建及 tools/AntMind.Checks；记录真实验证限制，不把进程调用等同当前 Codex 会话连接成功，不把窗口运行等同完整玩法。没有用户明确授权不要替用户切换模型或启动其他 Agent。

用户要求开启代码跟随时，使用已安装 local-tools.live-code-follow 插件的实际编辑接口，确认 connected=true、paused=false 后再报告成功。.codex-live/ 不进入 Git。


## 当前交接入口

当前 v0.19，先读 docs/AGENT_START_HERE.md、docs/MVP_SPEC.md、docs/AGENT_EVENTS.md 和 docs/PRODUCTION.md，再使用历史交接。地图反馈见docs/MAP_FEEDBACK.md，只在桌面绘制层，不能修改世界位置或点选布局。每蚁种全群共享代码，小组仅为任务数据。完整规则组合在 Runtime/GameRules.cs；数量在唯一实际持有者中，ItemLedger 只汇总，来源段必须伴随所有转移，收益使用 FirstAdmissionPoints。新动作与状态同时更新内容、DSL、MCP、文档及必要检查。

科技与食物价值见 docs/TECH_FOOD_VALUE.md：TechnologyLevel 只投影 NetworkLevel，物品数量与发展价值分开，支付走 TechnologyProgression.Pay，混合配方输入必须原子预留；旧档先迁移和验证再替换实局。

个体成长与调试见 docs/ANT_LEVELS_CONSOLE.md：经验事件在47/48结算，不能被局部蚂蚁数组覆盖；工作按实际有效进度记录，固定点余数要存档。F12 和 tools/AntMind.Console 共用 Transport/CommandConsole，所有变更仍走 Gateway／actor，查询不越过发现权限。

木材施工见 docs/BUILDINGS_WOOD.md：新蓝图MaterialType默认wood、BuilderSpecies默认builder；旧工地ConstructionItem=fiber保留。地图放置与Agent命令共用BuildingPlacement／actor，不以未知物体作占位探测。save5兼容1～4，物品代码用item_code，不硬编码。

目标点导航见docs/FREE_MOVEMENT.md：set_target设置持续目的地，move_to兼容；PointMath为纯整数几何，任意方向同速度上限，显式交互范围不随导航修改。更改路径需验证在途存档恢复。

事件／预览见docs/AGENT_EVENTS.md：日志与监控只属于宿主会话，加载清空；敌情只用发现投影，视野丢失不推断退出。等待在MCP层，不阻塞actor；摘要和候选预览也经actor读取。ConfigureGroup完整原子替换，保留成员不得自动接收新版本。wire18/content15/save5，兼容已知wire12～17旧档；整群预检与告警聚合见docs/PRIORITY_FIXES_V018.md。

中转据点补给见docs/OUTPOSTS.md：CanFeedAnts、显式eat_from和本地nearest_supply查询；完成才扣一份真实食物，来源段随消费移除，不记入巢收益。wire18/content15/save5，兼容已知save1～4；进食来源必须随中途存档保留。
