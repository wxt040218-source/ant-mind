> v0.19：中转站升级为仓储与食物补给据点，保留稳定cache节点供以后运输系统使用；eat_from与本地补给查询见 [OUTPOSTS.md](OUTPOSTS.md)。wire18／content15／save5，兼容已知save1～4旧档。

# Agent 事件与诊断 v0.18

> 当前wire17／content14／save4，MCP共15工具。新增整群预检、程序告警汇总／恢复及供给目标修复，完整约定见[PRIORITY_FIXES_V018.md](PRIORITY_FIXES_V018.md)。下文原v0.17行为说明和425项检查为历史；本轮实际验证以PROJECT_STATUS.md为准。

2026-11-10；wire / Snapshot 16、content 13、save 4。本轮基于 [10分钟实玩](AGENT_PLAYTEST_10M.md) 的接口问题实现，不改变发现、通信、显式行动、经济守恒或同种共享代码规则。

## 使用流程

1. `observe_summary()` 取得 `Summary.WorldId`、版本、风险和 `EventCursor`。
2. `watch_events(worldId, action="upsert", id="queen-danger", name="蚁后警戒", kinds=["enemy_entered_zone","queen_attacked","ant_died"], radiusCells=10)`。
3. 首次从 `after=0` 读取；后续使用上次 `Events.Cursor` 调用 `read_events(worldId, watchId="queen-danger", after=cursor, timeoutSeconds=30)`。
4. 收到提醒后查询摘要或具体对象，再通过原有管理命令、职业代码和任务数据响应。事件本身不执行防御或升级。

不填中心时警戒圆跟随蚁后；也可填写 `centerX/centerY` 固定地点（1000单位/格）。半径1～1024格。`groupId`、`species` 仅筛选本方蚂蚁事件，不把敌情错误绑定到敌人的“组”。

等待发生在 MCP 服务中，每至多250ms做一次短读取，不占住 Runtime actor。一次等待0～30秒，超时返回空页，支持取消；等待期间其他命令、模拟和连接心跳继续工作。Agent 必须有仍在执行的任务才能继续等待与响应，聊天结束不会使模型永久运行。暂停的世界不会产生新的模拟事件。

## 可订阅事件

| kind | 含义 |
|---|---|
| enemy_seen | 首次发现敌人 |
| enemy_entered_zone | 新观察确认存活敌人进入警戒圆；观察到退出后再次进入可再次提醒 |
| enemy_defeated | 观察到已知敌人生命从正数降为0 |
| queen_attacked | 蚁后生命减少，critical级 |
| ant_attacked | 存活本方蚂蚁生命减少 |
| ant_died | 新增本方死亡记录，critical级 |
| building_ready | 观察到设施转为建成 |
| production_completed | 已观察设施的配方结束且产出增加 |
| task_received | 个体显式请求后接收到新的小组／版本 |
| program_error | 新的执行错误或程序版本下的错误 |
| action_failed | 同原因连续失败达到 `failureThreshold`，默认3次 |
| stock_below | 指定注册物品库存小于等于 `stockThreshold`；从不满足到满足时提醒 |

默认监控 `default`：半径8格、敌人入圈、蚁后受击、蚂蚁死亡、设施建成、程序错误和连续失败。可删除或同ID修改。最多32个监控（含默认），ID为1～40位ASCII字母、数字、点、短横线或下划线；同ID相同配置重试不重复注册或产生初始提醒。

`fireInitial=true` 默认在注册时对**当前Tick实际观察到的**圈内敌人或已满足的低库存条件提醒；不会用旧坐标当作当前入侵。连续失败按新的决策尝试统计，同一个失败状态不每Tick累加；成功后清除，达到阈值后不持续刷相同提醒。新原因重新计数。

敌情只使用发现投影的位置、生命和观察Tick。未知敌人不发消息、不参与区域测试；失去视野不视作退出，也不会读取其真实位置来推断是否越界。建筑提醒基于最后观察快照；视野空档中的多次加工可能合并或无法还原，**不是完整生产流水账**。需要累计收益使用 `Summary.Economy`，需要实物数量查询公开设施库存。`ObservedTick` 与事件生成 `Tick` 分开，避免把延迟观察当作即时真值。

## 游标、重连和边界

- 环形日志最多2048条，所有监控共享序号；一次默认50条，`limit` 为1～100。
- `Items` 包含 Sequence、WorldId、Tick、Revision、WatchId、Kind、Severity、对象ID、允许公开的位置和说明。读取不消费事件，可重放。
- 后续必须使用响应的 `Cursor`；`HasMore=true` 时继续读取，不先进入长等待。不同监控／过滤器应保存各自的游标。
- `OldestCursor` 表示最早保留序号。`Gap=true` 表示落后太久，先重读摘要补齐现状，再消费保留消息；日志不保证无限重放。
- 传入错误WorldId返回 `world_conflict`；超前／负数游标、非法分页返回错误。不要把错误当作“没有危险”。
- 监控和日志是**宿主会话诊断数据**，不进入存档。MCP客户端短暂退出不清除；游戏宿主退出或加载存档后重新初始化（只保留默认监控），序号重置。加载会旋转WorldId，重新观察、注册和建游标。
- 监控配置不改变模拟Tick、revision或RNG，不提供永久运行的模型、桌面系统通知或外部推送服务。

## 精简观测与任务回执

`observe_summary` 返回经济速度、风险个体、已知敌情、设施材料缺口、小组行动／失败计数及回执，省略程序全文、历史和个体记忆。需要程序才调用 `observe`，需要单对象调用 `inspect_object`。

`Groups[].Receipts` 包含 RequestedVersion、ReceivedVersion、Connected，以及：

- `current`：已缓存当前组版本，即使随后离网也有效。
- `awaiting_request`：尚未收到当前版本，当前在网络范围内。
- `out_of_range`：尚未收到当前版本，当前不在网络范围内。

它是 Agent 的管理诊断，不会让蚂蚁程序越过通信。改完任务后只有程序显式 `request_queen_info()` 成功才会更新其缓存。

## 原子配置小组

`configure_group` 用一个命令创建（groupId=0）或完整替换指定小组：name、task、activity、foodFilter、radiusCells、antIds、parameters、points，以及 objectiveX/Y、rallyX/Y、retreatX/Y。Points是JSON坐标对象，例如 `{"supply":{"X":64000,"Y":64000}}`。

这是完整替换，成员列表精确生效、遗漏字典字段视为空、遗漏三个主要地点默认蚁后位置。最多64参数／16命名点；所有字段和成员先验证，失败整批无变化。成功只增加一次世界revision和一次目标组Version。任何写入仍需当前WorldId、revision、UUID requestId与reason；相同ID只可重试相同载荷。

原成员保留行为、记忆和旧任务缓存直到主动通信；成员归属发生变化时才重置行为、缓存并释放认领。每蚁种仍共用同一份程序。管理API不会自动完成拿取、移动、上报或劳动。

## DSL 和候选预览

- `require_param("key")`、`require_group_x/y("key")` 严格读取个体已经接收的任务。缺失产生 `missing_group_parameter:key`／`missing_group_point:key`，停止本次决策并清除移动目标；明确配置的0合法。旧 `param`／`group_x/y` 保留缺失返回0的兼容语义。
- `recipe_ready(stationId, recipeCode)` 只读局部可见设施，检查工位类型、空闲、缓存科技、全部原料与输出容量。仍需显式 `process` 和 `work`；此查询不是预留或保证动作必定成功。
- `preview_program(species, source, antId=0)` 用指定存活蚂蚁的真实受限感知与当前预算执行候选一次，返回意图、目标、预算消耗、拟写记忆、错误与旧任务提示。antId=0选该种第一只存活蚂蚁。**不提交程序，也不改变世界、个体记忆、RNG或revision**；不进行后续物理行动结算，不能替代实际采运／战斗验收。
- 新局默认兵蚁在近战敌人存在时先攻击，局部安全且巢穴有食物才考虑进食；默认厨师缺料时等待。旧档已有策略保留，若要换模板需显式提交。没有自动使旧局蚂蚁获得新行为。

## 脚本和控制台

```powershell
python scripts/game-agent.py summary --output artifacts/agent-live/summary.json
python scripts/game-agent.py watch --watch-action upsert --watch-id queen-danger --name "Queen perimeter" --kinds enemy_entered_zone,queen_attacked,ant_died --radius 10
python scripts/game-agent.py events --watch-id queen-danger --after 0 --wait 30 --output artifacts/agent-live/events.json
python scripts/game-agent.py preview --species hauler --ant 7 --source content/programs/hauler-reliable.ant
python scripts/game-agent.py configure-group --config artifacts/my-group.json
```

每次脚本调用使用真实官方STDIO桥，不会启动新世界。保存各监控自己的Cursor，不要在循环中一直after=0。脚本的configure-group JSON填MCP业务字段，如 `name,task,antIds,reason,parameters,points,groupId`；脚本补新鲜版本和UUID。低库存、初始提醒等完整选项可直接调用MCP。

F12及独立 `tools/AntMind.Console` 同时支持：

```text
summary
watches
events 0
watch {"Id":"danger","Name":"Queen perimeter","Kinds":["enemy_entered_zone","queen_attacked"],"RadiusCells":10}
```

控制台 `execute ConfigureGroup {"GroupConfiguration":{...}}` 复用相同Gateway／actor；没有直接世界写入或任意系统代码执行。

## 代码入口与验证

Contracts/AgentDiagnostics.cs 为契约；Runtime/AgentEventJournal.cs 为有界事件归并，Runtime/AgentDiagnostics.cs 为actor查询与受限预览；Transport/CommandConsole.cs 和 Mcp/Program.cs 为客户端入口。观察帧不修改模拟规则顺序。

全解决方案0警告／0错误，425项检查通过。新增检查含未知敌人和视野外真值变化不报警、重复失败去重、游标分页与缺口、会话重置、原子配置／幂等重试／任务显式接收、严格字段0值、预览不变性、安全模板、真实MCP并发等待时另一命令可成功及条件唤醒。

实际Release桌面IPC烟测通过；原wire15/save4世界成功迁移加载，仍暂停在T12007。注册蚁后10格警戒后读到当前已见enemy:8入圈提醒，after=1等待超时返回空页，搬运候选预览15/64指令且世界revision不变。证据在artifacts/agent-events-*；不是当前聊天原生装载MCP或长期压力测试证明。原存档保留，未推送GitHub。
