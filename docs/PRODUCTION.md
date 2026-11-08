# 生产扩展交接 v0.13

更新：2026-11-09。现行玩法见 MVP_SPEC.md，原方案见 EXPANSION_IMPLEMENTATION_PLAN.md。此页说明已经实现的第一条产业链及其边界。

## 从哪里继续

| 模块 | 当前入口 |
|---|---|
| 物品、来源段、容器、源头、建筑 DTO | Contracts/Production.cs、Colony.cs |
| 物品权威位置与守恒汇总 | Core/ItemLedger.cs，读取 Objects 和 Stock，不复制数量 |
| 生长、采集、物流、施工、加工、统计 | Core/ProductionRules.cs |
| 全规则组合、程序 Tick 激活 | Runtime/GameRules.cs |
| 管理计划、取消、完整存档、隔离评估 | Runtime/SavedGames.cs、SpeciesProgramCatalog.cs |
| 个体感知和物理意图 | Runtime/ProgramRule.cs、Programs/AntDsl.cs |
| 默认厨师程序 | content/programs/chef.ant |
| 实际 Agent 演示 | scripts/production-demo.py |
| 规则与恢复验收 | tools/AntMind.Checks/ProductionCheck.cs |

世界写入仍由 Runtime actor 串行处理；隔离评估在私有不可变世界上使用相同 GameRules，不接触实局。Core / Programs / Contracts 仍无 UI、MCP、文件系统或系统时间依赖。save format 与 wire / content 版本分开；当前 1 / 12 / 10。

新增规则排序：源头生长 2、局部观察 5、程序 10、显式通信 15、认领 16、活动采样 18、跟随 19、移动 20、敌移动 21、战斗 25、交互 30、工作 31、进食 32、刷新 40、发现 45、认领清理 46、统计 49、里程碑 50。同序规则按稳定 Id 排序。

## 计分与守恒约定

数量只存在实际持有者。CreditLot 只记其中尚未结算的单位段，不是第二份数量；已计分单位隐含零点。FoodStore.Take 与 ItemCredit.Split 统一先取未结算单位，余量作为已结算单位处理。Raw 水果／肉点数为 1，初始巢穴食物为 0。当前配方全部单输出单位，输出继承输入的剩余点数总和。

所有新增搬运路径必须同时移动数量及来源段；仅首次入巢 Economy.Admit 结算点数，巢穴持有段立即清零。别用 Delivered 评价新机制效率：它保留旧 deliver 的历史搬运量，不能作为新食物收益。其他消耗需要登记 Economy.Consumed，新生成需要 Economy.Created；ItemLedger.Imbalance 用于按类型验证。源头 harvest 只是转移，不登记第二次生成。

独立地面对象耗尽后保留 ID 与发现记录，清掉未结算段。批量 Snapshot 省略个体重复报告与缓存数组以控制消息体，程序实际感知和完整检查点均保留；公开源头和设施只取最后观察，不用隐藏真值刷新。未来扩大地图对象数量仍需要分页、长期墓碑压缩和性能测试。

## 建筑与厨师

蓝图免费、无实体阻挡。材料必须实运，建筑不能从巢穴远程扣物。建造和加工共用 work 劳动机制：一个工作位、每调用至多连续 10 Tick、伤害打断个体、进度存于建筑。所有职业可工作，chef.WorkRate=2，其余为 1。

默认厨师从显式蚁后任务缓存读取 station / recipe 整数和 objective 坐标。同职业只有一份代码；多个厨房小组仅参数不同。物流演示给搬运蚁提交一份共享程序，用命名 take/drop 地点及 source/from/to/item 参数编排供应、缓存和输出取回。它是 Agent 的演示方案，不是 Core 自动执行的固定职业任务。

首版配方只支持单类输入、单输出单位；没有自动加工循环、远程物品操作、自动接管死亡工位或新资源注入。下一批必须再 process。Chef 美术为旧身体＋代码帽子的原型，尚非新定稿 PNG。

## 已验证证据

- 完整解决方案构建 0 警告、0 错误；244 项检查通过，输出 artifacts/production-checks.txt。最后 UI 页签字号调整后 Desktop 另行构建通过。
- 检查包含类型守恒、混合来源拆分、防重复入巢得分、并发末份取货、实际纤维施工、无劳动不加工、加工食物回血、受击保留在制、稳定 ID 查询，以及原采运、战斗、小组、程序预算、真实 STDIO MCP 回归。
- 完整存档测试保存隐藏世界／RNG／记忆，恢复后相同 40 Tick 延续相同；旧 WorldId 写入拒绝。保存中途在制和待激活程序可恢复；损坏 JSON、非法槽名和坏程序不会替换当前世界。同存档两分支结果一致且实局 revision 不变。
- 实际桌面经官方 MCP 执行 scripts/production-demo.py，没有注入材料、资源或位置：T201 箱子建成，T592 加工台建成，T937 巢穴有 1 份水果浓缩，FirstAdmissionPoints=2。人口 11（5 侦察、5 搬运、1 厨师），水果 80；厨师末次耗 45／64 指令，无程序错误。
- 实际 MCP 从 production-demo 存档各评估 100 Tick：两分支均新增 2 点，浓缩库存＋1，零死亡、零程序错误；结果 artifacts/agent-live/production-evaluation.json。
- 真实窗口确认四职业菜单、厨师 2× 工作、生产页、可见源头和两座设施。最终版恢复同一 T937 演示成功并保持暂停；新证据 artifacts/agent-live/production-final.json。

演示阶段文件 production-cache.json / production-workshop.json / production-batch.json / production-complete.json 和日志都在 artifacts/agent-live。before-production-demo 和 production-demo 是实际桌面输出目录 saves 中的完整档。artifacts 与 bin 不进入 Git。

## 当前限制和后续

还没有长期满人口压力、所有分辨率／系统缩放、持续数小时经营的验收；单次评估受 5 秒 IPC 限制。没有完整事件回放、旧存档格式迁移、自动存档、离线发展或长期耗尽记录压缩。不得把此演示吞吐当作平衡结论或最优代码。

本轮只完成 M0～M3 的首个可运行闭环；完整调研规划中的能力科技、身体模块、昼夜环境与开拓属于 M4／M5，尚未开发。下一步优先更完整的多种子生产物流评估、长期数据量和观测分页，再扩展配方与内容。

补记：最终页签字体调整后 Desktop 构建成功；用户按 Esc 停止了后续界面自动化，因此未继续鼠标操作。现无运行中的游戏；production-demo 已复制到 quick 快速槽供重启后继续。
