> 当前 v0.13：wire=12、content=10、save=1。最新生产／存档接口见本文末尾 v0.13 节和 PRODUCTION.md。小组仅存数据，所有组共用每蚁种程序；Visibility、Recruit、SetQueenRange、组程序已停用。优先先读 AGENT_START_HERE.md 和 MVP_SPEC.md；下方旧版本演进描述不代表当前可用接口。

# Agent 操作接口 v11

MCP 工具：get_actions、observe、validate_program(source)、command、inspect_object(kind,id)。observe 返回经过当前信息权限过滤的世界、全部蚂蚁状态、蚁后已知资源、通信半径、各蚁种程序和历史。选中个体、查看程序等读取动作直接通过 snapshot 按 id／species 筛选，无需修改界面选择。

先 observe，取得 revision；每个新命令生成 UUID requestId，expectedRevision 使用最新值。revision 冲突时重新观察并换新 ID。相同 ID 只允许相同载荷重试。所有写操作串行进入 Runtime actor。

| kind | value／其他参数 | 对应功能 |
| --- | --- | --- |
| Pause / Resume / Step | 0 | 暂停／运行／暂停时单步 |
| Speed | 1、2、5 | 倍速 |
| ProduceEgg | reason | 蚁后生产 1 枚通用卵，扣一次资源 |
| HatchEgg | species=scout 或 hauler，reason | 对应蚁种菜单孵化，消耗 1 枚卵，不再扣资源 |
| UpgradeCarry | species=hauler，reason | 提升所有搬运蚁有效载量，现有与以后孵化成员共享 |
| UpgradeBrain | reason | 升级大脑 |
| Visibility（已停用） | 任意值 | 返回 discovery_only；玩家和 Agent 均无全图权限 |
| ApplyProgram | species、programSource、reason | 提交蚁种 DSL，下一 Tick 应用 |
| RollbackProgram | species、value=历史版本、reason | 回滚为新版本 |
| UpgradeNetwork | reason 必填 | 花食物提升蚁群网络等级，扩大通信范围并解锁达到等级要求的能力 |
| SetMinimumReserve | 0..1000000000 | 最低库存 |
| SetPopulationCap | 当前人口..100 | 总人口上限 |

成功／失败均返回 CommandResult，包括 Accepted、Code、Revision 和可选 Detail。失败不绕过经济、暂停或预算规则。MCP command 的 requestId、expectedRevision、kind、value、reason 参数均需提供；其余有默认值。

程序信息接口见 ANT_DSL.md。validate_program 仅做静态校验，不运行模拟。STDIO 子进程检查通过不等同当前 Codex 会话已加载工具。

未来新增按钮时，先加入 Contracts.CommandKind 和 Runtime 的参数校验及处理，再接入桌面按钮；get_actions 自动列举枚举。同步扩充参数说明和必要检查。存读档目前没有实现，后续接入必须版本化并提供真实结果，不能返回假成功。

协议版本现为 9，与旧版不兼容；本地客户端和宿主应一起重新构建。


蚁后页面与 API 使用同一 ProduceEgg / UpgradeNetwork 命令；蚁种页面使用 HatchEgg。旧 Recruit 命令返回 recruit_replaced_by_eggs，不能绕过生产／孵化流程。

初始卵数为 0；每枚卵生产成本默认 20，沿用 content/world.json 的 recruitmentCost 配置字段（兼容保留名称）。快照 EggProductionCost 为此费用；RecruitmentSpent 为累计产卵支出。EggsProduced = Eggs + EggsHatched，初始 10 只蚂蚁不计入孵化数。

卵生产当前为单次点击即完成，不自动生产或计时。最低库存约束作用于产卵；人口上限作用于孵化。人口满时可以保留或生产卵，孵化失败不消耗卵。新孵化蚂蚁出生于蚁后位置，使用所选种类当前程序和空记忆。快照同时返回 Eggs、EggsProduced、EggsHatched。


载量：observe.species 的 CarryCapacity 为有效容量；CarryLevel、NextCarryCapacity 和 NextCarryUpgradeCost 描述当前档位及下一次升级；最大档位的下一费用为 0。全局 CarryUpgradeSpent 记录资源支出。默认容量逐级 1～10，费用 80/120/180/260/360/480/620/800/1000；速度按实际负载从空载 120 降至携带 10 份时的 60（单位/Tick），升级本身不改变同负载速度，来自内容配置的 CarryMultipliers 和 CarryUpgradeCosts。

蚂蚁动作是 DSL 层：request_queen_info() 和 report_discovery() 只生成个体通信请求。读取缓存和发请求不同：queen_* 查询不会自动刷新缓存。AntView.QueenKnowledge 是成功请求后的历史数据，QueenInfoTick 标记时间；QueenRequestStatus、QueenReportStatus 与物理 ActionStatus 为独立结果，数值 0/1/2/3 对应 None/Running/Succeeded/Failed。

产卵和身体升级属于外部 Agent／玩家的发展命令，蚂蚁 DSL 本身不能花公共库存升级。查看 ANT_DSL.md 获取具体动作耗时、预算与单次交互规则。


蚁群网络快照：NetworkLevel、MaxNetworkLevel、QueenRangeCells（当前实际半径）、NextNetworkRangeCells、NextNetworkUpgradeCost、NetworkUpgradeSpent；满级后下一范围和费用为 0。Technologies 返回 Id、Name、RequiredNetworkLevel、Unlocked，便于 Agent 判断科技前置条件。默认等级 1～5，范围 16/24/32/48/64，升级费用 60/160/400/1000。升级网络与升级大脑相互独立。

SetQueenRange 已停用，返回 queen_range_requires_network_upgrade；get_actions 不列出此旧命令。UpgradeNetwork 必须提供 reason，检查最低储备、最大等级、revision 和 requestId。协议／快照 v9 与旧版不兼容，宿主及客户端同步重建；内容 schema 8。


v7 对象查询：inspect_object(kind="food"|"ant"|"queen", id) 返回 ObjectSnapshot，其 Food／Ant／Queen 为对应的独立对象数据。食物只返回已发现、最后观察的记录；未知与不存在的 ID 都返回 object_not_found。Generated 公开字段为 null，不能用总量推算隐藏资源。

连接指示：Snapshot.AgentConnected 和 ConnectedAgents 来自官方 MCP 握手后登记的会话，2 秒心跳／6 秒过期，不以工具调用次数或游戏暂停状态冒充在线。STDIO 每个进程为一个会话；命令行脚本结束后会话随之断开，灯恢复灰色是正常行为。

运行中游戏的调用示例：python scripts/game-agent.py observe；python scripts/game-agent.py inspect --kind food --id 4。使用 --hold-seconds 30 可保持该真实客户端会话 30 秒用于连接指示验收。此脚本调用官方 MCP 服务，不启动新的游戏世界。


Milestones 返回 Id、Title、Description、Metric、Target、Current、Status（locked/active/completed/failed）、StartedTick、CompletedTick、DeadlineTick。截止按模拟 Tick；目标只使用允许公开的指标。Agent 可读取这些目标规划经营，不可直接标记完成或修改进度。当前不发额外资源奖励；新局默认已采用修正后的侦察／搬运策略。


战斗 v9：HatchEgg(species="soldier") 消耗一枚通用卵；ApplyProgram/RollbackProgram 同样支持 soldier。attack 是蚂蚁 DSL 动作，不是可绕过个体感知和距离的管理命令。inspect_object(kind="enemy",id) 仍过滤未知目标；kind="ant" 可读取自己的死亡个体记录。Snapshot.Enemies 是已发现敌人的最后观察值，不能拿它替代蚂蚁本地感知。Defeated=true 表示蚁后已倒下，Resume、Step、HatchEgg 等返回 colony_defeated。


## v11：共享蚁种代码，Agent 自由规划

MCP 共六个工具：get_actions、observe、inspect_object、inspect_group(groupId)、validate_program、command。小组是参数容器，不支持组内独立程序。ApplyProgram／RollbackProgram 必须 groupId=0，按种类影响所有组；非零返回 species_program_only。旧 ClearGroupProgram 不再列出且固定拒绝。

observe 的 Groups 有名称、自由说明 Task、自由标记 Activity、Parameters（最多 64 个整数）、Points（最多 16 个命名坐标）；inspect_group 返回单组及活成员。Programs 只有三蚁种，GroupId 固定为 0（兼容字段）。未知食物与敌人仍按原发现权限过滤。

| command kind | 字段 |
|---|---|
| CreateGroup / UpdateGroup | name、task、可选 activity（自由字符串），foodFilter（空／注册食物 ID）、objectiveX/Y、radiusCells 1～32；Update 另需 groupId |
| AssignAnt | groupId、antId；groupId=0 移出 |
| AssignAnts | groupId、antIds 数组；原子分配，重复 ID／死亡／不存在拒绝整批 |
| SetGroupParameter | groupId、key、value（任意 int32） |
| RemoveGroupParameter | groupId、key |
| SetGroupPoint | groupId、key、pointX、pointY（地图内固定点坐标） |
| RemoveGroupPoint | groupId、key |
| DeleteGroup | groupId；释放成员和认领，蚁种程序保留 |

全部写入必须 reason、最新 expectedRevision 和 UUID requestId。相同 ID 相同批量内容可重试，改变内容拒绝。参数／命名点每次变动增加组 Version，只在下一次成功蚁后请求后更新个体缓存。key 长度 1～40，可用 ASCII 字母、数字、点、短横线和下划线；地点单位 1000／格。FoodFilter 等只是便利筛选参数，其他任务由 Agent 自己组织，不限制为预设任务类型。

```powershell
python scripts/game-agent.py command --kind CreateGroup --name Fruit --food fruit --objective 37000,31000 --radius 3 --reason "Fruit collection"
python scripts/game-agent.py command --kind AssignAnts --group 1 --ants 1,6,7 --reason "Assign members atomically"
python scripts/game-agent.py command --kind SetGroupParameter --group 1 --key retreat_health --value 10 --reason "Define strategy setting"
python scripts/game-agent.py command --kind SetGroupPoint --group 1 --key supply --point 32000,32000 --reason "Define supply point"
python scripts/game-agent.py group --group 1
python scripts/game-agent.py apply --species hauler --source content/programs/hauler-reliable.ant --reason "Update all haulers"
```

代码通过 param("retreat_health")／has_param("key")、group_x("supply")／group_y("supply")／has_point("key") 读取缓存；没有对应值返回 0。不自动产生撤退、补给或任何动作，Agent 必须编写 move_to／pickup_from／attack／eat 等明确行为。


## v0.13 实际接口（wire 12 / content 10 / save 1）

- command 新增 `expectedWorldId`：每次使用 observe.WorldId；恢复世界后旧 ID／缺失 ID 拒绝。revision 和 requestId 规则保留。
- PlanBuild：key=cache|workshop、pointX/Y、reason；Result.Detail 为建筑 ID。只计划，不给材料。CancelBuild：value=建筑ID，取消工地或在制批次，材料留在实体。
- SaveGame / LoadGame：key 为 1～40 个 ASCII 字母数字、下划线或连字符，reason 必填。LoadGame 成功后暂停且改变 WorldId。磁盘目录为宿主输出路径 saves；quick 是界面快速槽。
- get_catalog：公开物品／配方／建筑／种类定义；inspect_object 新增 kind=source|building，仍只返回已发现／已知数据。
- list_saves：只有槽名、文件大小和修改时间，不返回文件内容。
- evaluate_save(slot,ticks,species="",source="")：同一存档两条隔离分支，ticks 1～1200，候选替换一个蚁种源码。返回实际结束 Tick、NewFoodPoints、GrossDelivered、AntsLost、ProgramErrors、InventoryDelta；实局不受影响。5 秒 IPC 期限仍有效。
- observe 新字段 Sources、Buildings、Recipes、BuildingTypes、FirstAdmissionPoints、Economy、WorldId；Inventory 排除非食物 fiber，完整分类数量看 Stock。

最小操作顺序：观察→计划箱子／加工台→组内配置材料源和目的地→更新共享搬运代码→实际送纤维并 work→process / work→withdraw→deposit(0,物品,数量)。通信和物理操作都须由 DSL 明确发出。

可复验演示：新局暂停 T0 时执行 `python scripts/production-demo.py`。脚本先保存 before-production-demo，全部采集／施工／加工只经官方 MCP，结束保存 production-demo；已有运行局不会被它自动重置。完整证据位于 artifacts/agent-live/production-*.json。
