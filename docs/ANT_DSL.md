> v0.19新增eat_from(holderId,itemCode)、building_can_feed(id)和nearest_supply_id/x/y/distance/food，均遵守局部感知及实际交互范围；详见[OUTPOSTS.md](OUTPOSTS.md)。

> v0.17新增：require_param("key")、require_group_x/y("key")严格读取已接收任务，缺失停止决策并清目标，配置为0合法；recipe_ready(station,recipeCode)检查局部工位、缓存科技、全部原料与输出容量。旧宽松查询保持兼容。preview_program只读执行候选一次，不结算实际动作，见 [AGENT_EVENTS.md](AGENT_EVENTS.md)。wire16/content13/save4。

> v0.16.1导航更新：`set_target(x,y)` 设置持续目标，任意方向固定点移动；`move_to` 兼容。新增 `has_target`、`target_x/y`、`target_distance`、`target_reached`；详见 [FREE_MOVEMENT.md](FREE_MOVEMENT.md)。消息／存档形状保持。

> v0.16当前接口：wire15 / content13 / save4。建筑定义 MaterialType/MaterialCost/BuilderSpecies，工地 ConstructionItem；新增 food_wood、species_builder、building_phase(id)、building_material(id)、building_cost(id)。PlanBuild新增公开建筑／蚁后2格间距校验，仅建筑蚁可施工；详细规则和示例见 [BUILDINGS_WOOD.md](BUILDINGS_WOOD.md)。

> v0.15 当前增量：wire14 / content12 / save3；每只蚂蚁独立三级经验，固定点属性加成与探索去重，规则47/48结算。新增 F12 游戏内控制台及 tools/AntMind.Console，共用受控命令解析；详见 [ANT_LEVELS_CONSOLE.md](ANT_LEVELS_CONSOLE.md)。旧版本说明仍作历史，不把本轮本地验证当成远端 CI。

> v0.14 当前规则：wire=13 / content=11 / save=2。科技就是虫群网络等级；厨房／厨师／浓缩二级，蛋白餐三级，混合营养餐四级，盛宴五级。升级用 DevelopmentValue 精确支付并保存找零，Inventory 仍为实物份数；配方 ExtraInputs 原子预留，来源汇总。完整现行说明和验证见 [TECH_FOOD_VALUE.md](TECH_FOOD_VALUE.md)，下方 v0.13 及更早的单输入、无价值差异、无旧档迁移等说明是历史记录。

> 当前 v0.13：wire=12、content=10、save=1。最新生产／存档接口见本文末尾 v0.13 节和 PRODUCTION.md。小组仅存数据，所有组共用每蚁种程序；Visibility、Recruit、SetQueenRange、组程序已停用。优先先读 AGENT_START_HERE.md 和 MVP_SPEC.md；下方旧版本演进描述不代表当前可用接口。

# 蚂蚁程序 DSL v0.11

每个蚁种共享一份程序，所有小组共用；每只蚂蚁有独立的 16 槽整数记忆、动作结果、待上传发现和蚁后缓存。

## 语法和预算

支持 let、赋值、if/else、repeat 固定次数（1..16）、整数运算和 memory[0..15]。上限 8192 字符、512 节点、8 层嵌套。每语句／表达式节点 1 点，动作额外 2 点，记忆读写额外 1 点；最近资源查询每遍历一条记录额外 1 点，索引查询固定单条成本。预算耗尽立即结束决策，保留已形成的合法意图和记忆更新。

## 显式动作

| 调用 | 执行规则 |
| --- | --- |
| set_target(x,y) | 设置绝对目标点，引擎按方向向量持续移动，受到身体／负载／等级速度限制 |
| move_to(x,y) | set_target的兼容别名，供旧程序使用 |
| pickup() | 只在当前位置附近拾取，一次最多填满当前有效载量；不自动走向资源 |
| drop() | 在当前位置放下全部携带食物；不交付，不计分 |
| deliver() | 只在巢穴附近交付全部携带食物；不自动回巢 |
| wait() | 清除目标并等待 |
| report_discovery() | 在通信范围内显式上传自己的资源发现 |
| request_queen_info() | 在通信范围内显式下载蚁后汇总，替换个体缓存 |

一次决策最终提交一个物理行动意图，最后一条物理动作生效；上传与下载标志独立，可与物理意图一起提交。重复多次调用不会在同一次决策里多走路、多拾取或多次通信。

拾取／放下／交付为单次尝试，处理后清除意图；失败须由程序再次调用。交互冷却默认 10 Tick。移动持续到达目标，过程中为 Running。食物放下、再次拾取均不计分，只有实际交付计分，且交付资源从携带状态移出。

通信在程序之后、移动之前结算。上传先于下载，通信冷却默认 10 Tick；范围或冷却不满足时失败，不自动重试。下载结果在下一次决策可见，同次调用后读取 queen_* 仍是之前的缓存。进入通信范围不会自动收发。缓存可在离线后继续读取，但不会更新；queen_info_tick 标记缓存时间。局部感知与待上传记录为被动观察，不自动传给蚁后。

## 可读变量

| 名称 | 含义 |
| --- | --- |
| ant_id, tick, x, y, nest_x, nest_y, width, height | 编号、Tick、固定点位置（1000 单位/格）及地图尺寸 |
| carry, capacity | 携带量与已计入载量升级的实际容量 |
| species_scout, species_hauler | 当前蚁种匹配时为 1 |
| nearest_amount, nearest_x, nearest_y | 自身局部感知内最近资源，无记录为 0 |
| nearest_distance | 最近资源的切比雪夫距离，无记录为 int.MaxValue；<=1000 表示在交互距离内 |
| at_nest | 在巢穴交付范围内为 1 |
| explore_x, explore_y | 可复现的个体探索目标 |
| queen_connected | 当前处于通信圆内为 1 |
| queen_count, queen_amount, queen_x, queen_y | 缓存条数及缓存中最近资源；无缓存为 0 |
| queen_info_tick | 最近成功下载 Tick，尚未下载为 -1 |
| action_status | 上次物理行动状态 |
| queen_request_status, queen_report_status | 上次下载／上传状态 |

状态数值：0 None，1 Running，2 Succeeded，3 Failed。动作的文字结果（arrived、picked_up、not_at_nest 等）通过蚂蚁快照和个体面板查看。

queen_x_at(i)、queen_y_at(i)、queen_amount_at(i) 读取按资源 ID 排序的缓存条目，索引越界为 0。也支持 abs、min、max。最近资源按曼哈顿距离选择，nearest_distance 用于规则的方形交互范围判断。所有查询受指令预算约束。

## 示例

```text
if (queen_connected) {
    report_discovery();
    request_queen_info();
}
if (carry > 0) {
    if (at_nest) { deliver(); }
    else { move_to(nest_x, nest_y); }
} else if (nearest_amount > 0) {
    if (nearest_distance <= 1000) { pickup(); }
    else { move_to(nearest_x, nearest_y); }
} else {
    move_to(explore_x, explore_y);
}
```

可使用 drop() 编写搬运接力／中转策略。蚁后信息是历史观察，可能过期；程序应处理失败和旧记录。

MCP validate_program 只校验不执行；ApplyProgram 和 RollbackProgram 按蚁种操作，在 Tick 边界生效。更新清空目标种类的程序记忆和旧目标，保留位置、携带量、通信缓存与待上传发现。旧版依赖 deliver 自动回巢或进范围自动通信的程序需要修改。


## 战斗扩展 v0.9

attack(enemy_id) 为显式物理动作，取代本次其他物理意图；不自动移动或持续攻击。只能攻击实际处于一格近战范围的存活敌人，并受职业攻击伤害与冷却约束。行为结果为 attacked、cannot_attack、attack_cooldown、enemy_not_found、enemy_out_of_reach。

局部可读变量：health（自身生命）、species_soldier、nearest_enemy_id、nearest_enemy_health、nearest_enemy_x/y、nearest_enemy_distance；无局部敌人时 ID/坐标/生命为 0、距离为 int.MaxValue。每次最近敌人查询按实际遍历条目计费。变量不读取全图或远处敌人的真实位置。兵蚁默认源见 content/programs/soldier-guard.ant，和其他蚁种一样通过 ApplyProgram 按种类替换。


## v0.10：类型食物、补给与编队

新增物理动作 eat(type)、follow(ant_id,distance)，以及独立协作请求 claim_food(food_id,quantity,ttl)、release_claim()。eat 吃一份，40 Tick 完成时扣资源；受击打断。先用携带同类食物，再尝试一格内巢穴库存，库存仍遵守最低储备。进食期间暂停决策，移动／攻击不并发。follow 的距离为固定点单位（1000 一格），0～16000；目标必须当前局部可见。其后的物理动作会覆盖之前的物理意图。

| 变量／函数 | 含义 |
|---|---|
| max_health | 个体生命上限；health 为当前生命 |
| food_fruit, food_meat | 当前 FoodDefinitions 中的水果／肉数字代码，避免硬编码序号 |
| carry_food_type, nearest_food_type | 携带／局部最近食物的类型代码；空负载时前者仍保留旧类型，请同时检查 carry |
| nearest_id | 最近局部食物编号，未见为 0 |
| nest_fruit, nest_meat, stock(type) | 一格内巢穴库存；远处返回 0，不提供远程库存感知 |
| queen_food_type_at(index) | 已经下载的蚁后食物记录类型 |
| group_id | 管理归属，0 未分组 |
| group_order_version | 已下载任务版本，0 尚未获取 |
| rally_x/y, objective_x/y, retreat_x/y | 缓存任务点；未下载则回退到巢穴 |
| nearest_ally_id, nearest_group_member_id | 最近局部队友／同组成员，无目标为 0 |
| ally_x(id), ally_y(id), ally_health(id) | 局部队友状态，未见返回 0 |
| claimed_amount(food_id) | 局部和蚁后缓存中未过期认领的总量，同蚁去重，不含未知真值 |

认领数量 1～100，期限 1～1200 Tick，每蚁一条；可替换或释放。只允许局部感知资源，认领不禁止其他蚂蚁 pickup。队友／敌人／资源查找及认领统计按遍历项收费，大队伍需要更高计算预算。新增变量与动作仍受限于原有节点、循环和指令预算。

content/programs/groups 提供探索、采集认领和兵蚁护送示例；需要针对小组对应蚁种主动提交，不自动分配职业。默认兵蚁受伤回巢后显式进食，低生命会返巢；其他用户程序不会被引擎强制改变。

新增常见动作结果：eating、ate_fruit、ate_meat、eating_interrupted、already_healthy、invalid_food_type、no_carried_food、insufficient_food、claim_updated、food_not_visible、food_fully_claimed、invalid_claim、following、ally_not_visible。进食失败不扣食物，成功只按实际缺失量恢复。


## v0.11 通用小组数据（取代独立组程序）

任务由 Agent 编程实现，小组只保存参数。新增 param("key")、has_param("key")，group_x("point")、group_y("point")、has_point("point")；仅接受受限的字面量键，不引入通用字符串运算。读取个体已经通过 request_queen_info 获取的 ReceivedOrder，缺失返回 0。不会自动调用动作。

便利变量 group_food_type、group_radius、group_task（默认样例 collect=1、explore=2、guard=3，任意其他 Activity 返回 0），task_collect／task_explore／task_guard 是默认样例常量，不限制 Agent 自定义任务。job_food_id/amount/x/y/distance 查询局部可见且符合 FoodFilter 与作业范围的食物；job_queen_amount/x/y 从旧蚁后缓存找同样条件的目标；job_explore_x/y 给出范围内确定性探索点。

pickup_from(food_id) 显式拾取指定近处对象，防止重叠水果与肉拿错；仍检查实际距离、负载、冷却、资源数量。普通 pickup() 不强制遵守小组参数，自定义代码可以自行解释任务数据。资源查找结果只在本次决策内复用缓存，第一次扫描仍按每条记录收费；其他表达式／动作／循环预算规则不变。

共享程序静态节点上限 512，运行预算维持原配置 64／128／256／512／1024。所有小组共享同蚁种程序版本，旧组覆盖接口拒绝；content/programs/groups 的文件仅为可选蚁种级示例，不能 --group 提交，也不会自动提供分类任务行为。


## v0.13 生产扩展（协议 12）

新动作仍消耗基础两指令＋表达式预算，最后一个物理意图生效；感知查询按实际遍历项收费。

| 调用 | 参数含义 |
|---|---|
| harvest(sourceId, qty) | 一格内采集，受余量、类型、负载及冷却限制 |
| deposit(holder, item, qty) | 货物放入容器／工地，holder 0 是巢穴 |
| withdraw(holder, item, qty) | 本地提取；加工台优先取同类输出，其次输入；巢穴食物不得低于储备 |
| transfer(antId, item, qty) | 本地交给另一只蚂蚁，不改变来源点 |
| process(buildingId, recipeCode) | 显式开始一批，原料进入在制 |
| work(buildingId) | 最多连续 10 Tick 的建设／加工劳动，受击终止，进度保留 |

物品使用 `food_fruit / food_meat / food_fiber / food_concentrate / food_meal`，或稳定 ID `item_code("fiber")`。类型代码来自当前内容注册表的 1-based 顺序，未知 ID 返回 0。`species_chef` 是个体种类判断。

`source_count, building_count, queen_source_count, queen_building_count`；`source_id_at(index), building_id_at(index), queen_source_id_at(index), queen_building_id_at(index)` 的 index 从 0 开始，越界返回 0。

`source_x/y/amount/type(id)` 查询本地源头，未知返回 0；`queen_source_x/y/amount/type(id)` 是显式下载的旧源头信息。`building_x/y/ready/busy/work(id), recipe_work(id), holder_stock(id,item), holder_output(id,item)` 只读本地设施。缺少对象返回 0；因此先检验 id 是否在本地列表，再判断 ready。qty 范围 1～100，实际行动结果为 items_moved:实际量或失败原因。

默认厨师代码 content/programs/chef.ant：小组参数 station=设施ID、recipe=配方代码，objective=工位坐标；同一代码服务所有厨房组。其他物流可参考 scripts/production-demo.py 中 Agent 实际提交的共享搬运程序。批量 observe 为控制消息体省略个体报告与知识数组；这不会清除世界内程序缓存，存档保存完整缓存。


## v0.14 科技与价值查询

`technology_level` 为个体 KnownTechnologyLevel，只在 request_queen_info 成功后更新。`recipe_unlocked(code)` 按此缓存与公开配方门槛判断（遍历项计费），并不保证远处工位已有原料。`food_value(itemCode)` 返回该可食用物的单位发展价值，未知／纤维返回零。新常量 `food_balanced`、`food_royal`，也可用 item_code("balanced_meal") 等稳定 ID。

process(buildingId,recipeCode) 语法不变，多输入由配方公开 ExtraInputs 定义，不能在蚂蚁代码里私自合成。工程师可继续 work 建设，默认厨师对未解锁配方等待。保持所有组共享同职业代码，只用各组 station／recipe／地点安排不同工位。

## v0.15 个体查询

ant_level（1～3）、experience（满级封顶）、experience_to_next（满级0）、move_percent、work_percent、attack_damage 是自身可用数据。职业代码仍共享，可以按个体成长选择行为，不新增私有程序。变量查询照常消耗预算。升级不增加共同大脑预算或改变载量。
