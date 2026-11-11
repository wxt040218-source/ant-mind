# v0.18 供给目标、整群预检与告警汇总

2026-11-11；wire／Snapshot17，content14，save4。由v0.17.1实玩反馈的前三项改进组成。数量、来源计分、发现权限、显式动作和职业共享代码不变。验收和本轮游玩结果见PROJECT_STATUS.md。

## 供给目标

标准“稳定供给”和“运输冲刺”改为first_admission_points，分别使用累计首次入巢50／200点。deliver及deposit(0,...)都使用Economy.Admit；加工品所含多个原料来源点正常计入。出巢、放下、重捡、再次交付均不增加。目标明确以点为单位。

原delivered指标及Delivered字段保留，含义仍为显式deliver动作计数，供自定义目标及历史统计使用。职业界面显示真实采样时段内的新食物点，避免把旧计数标为全部供给。

已知wire12～16／save1～4旧档只在内存升级。仅替换ID、标题、说明、指标、阈值、限时和前置均匹配的两个内置旧目标；用户自定义目标不替换。利用已有首次入巢点在原Tick结算可达进度，不推进时间、改变库存或随机数。历史完成／失败保留；新开启的限时目标从迁移Tick计时。原文件不改写。

## 整群策略预检

新增只读MCP工具 `preview_program_batch(species, source)`。同一actor版本编译一次，按ID检查该职业全部存活个体；无匹配个体返回invalid_preview，不返回空的通过结果。

每只提供Current和Activation两份决策。Current使用当前受限感知、记忆和目标。Activation复用真实换程序的重置函数：清空记忆、目标、劳动／进食状态等，保留位置、负载、成长和已缓存的任务。两份均在当前观测Tick，不执行下一Tick规则。

PreviewBatch返回WorldId、Revision、Species、Budget、AntCount、EvaluatedDecisions、MaxSpent、Passed、FailedAntIds、Results及Warnings；Results含AntId、GroupId、Current、Activation。MaxSpent仅是本批已采样决策的最大指令消耗；Passed仅表示本批没有执行错误，不证明未来分支、物理动作成功或战斗安全。查询不安装程序、不改变世界、个体记忆、随机数、事件日志和版本。

```powershell
python scripts/game-agent.py preflight --species scout --source content/programs/scout-explorer.ant --output artifacts/preflight.json
```

F12及独立控制台共用：

```text
preflight {"Species":"scout","Source":"wait();"}
```

原preview_program保留单个当前状态查询。批量预检也不代替真实游玩或隔离多步评估。

## 重复程序告警

按个体、程序版本和错误原因聚合。首次program_error立即发出；持续重复每200模拟Tick至多追加一条批次。暂停时补发尚未发布的重复次数。每条Count为本批新增错误决策次数，FirstTick／LastTick为本批最早／最近发生Tick，ProgramVersion为对应版本。相同旧状态不每Tick加计数，各批Count相加可得总错误次数。

短暂的无错误等待不立即结束聚合；最近一次错误后连续200Tick无该错误，且已有更新的无执行错误决策，才发program_recovered。恢复事件的Count为该段总错误次数，FirstTick／LastTick为整个错误段范围。这表示执行恢复，不保证物理行动成功。默认监控包含恢复事件，自定义监控需明确订阅program_recovered。

新原因、新版本立即提醒；死亡和蚁后受击等事件不受节流。批次及恢复都是追加的新序号，不修改旧事件，不破坏游标、分页和重放。分组或职业改变时先按旧归属补发余下次数，再开始新归属错误段，避免跨组混算。不同监控各自匹配，不应跨监控相加Count。日志仍为2048条有界会话数据；加载清空监控、聚合及游标，不提供持久事件回放或常驻模型。

## 验证范围

回归覆盖真实入库与防倒运刷点、wire16旧目标迁移且原文件不变、自定义目标／失败状态保留、首只通过而其他个体超预算、激活记忆重置、个体发现权限、整群查询不变性，以及200次交替错误汇总为3条且总次数准确、稳定恢复、紧急消息及时、新原因／版本／死亡不被吞并。真实STDIO检查同时验证工具发现与批量调用。

最终构建0警告／0错误、440项检查通过，Release发布／IPC烟测与真实游玩通过；详见[实玩报告](PLAYTEST_V018_2026-11-11.md)及PROJECT_STATUS.md。
