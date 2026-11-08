# 对象数据与发现权限

本局对象的权威数据在 AntMind.Core.EntityDatabase。它是不可变内存对象库，不是磁盘数据库或存档实现；统一保存独立的 food:<id>、ant:<id>、queen:<id> 记录，数据写入仍只经 Runtime actor。

- IWorldObject 提供 ID、Kind、Position、Properties。各类型使用自己的强类型记录，避免把所有玩法值堆进无类型字典。
- WorldState.Ants、Resources 是对象库派生的有序视图，规则通过 with 更新时写回该库，没有第二份权威数组。Resources 只列当前有余量的食物；Objects.All<ResourceView>() 包含已耗尽记录。
- 物体扩展先实现 IWorldObject，在规则组合根注册真实行为，再增加必要的受限投影及查询。新增记录不能直接绕过世界规则修改库存。
- 食物每处自然生成容量 1～5，容量独立于当前数量；零数量记录保留。放下的批量食物按容量拆分，不改变既有食物容量。
- 发现是食物自身属性，规则按蚂蚁实际感知范围更新。最后观察量与真值分开保存；没有视野时 UI 和 Agent 都不能读取变化后的真值。
- inspect_object 与玩家地图使用同一受限投影。未来新增查询、存档预览、统计接口同样不得输出未知食物、其坐标、容量或全局隐藏资源数量。
- 当前只做本局内对象库；存读档需另行保存完整世界、配置、随机状态、程序及对象记录，不能只保存某类对象就宣称游戏可继续。


v0.9 增加 enemy 对象的生命、出生 Tick、攻击冷却、尸体食物 ID 与最后观察数据；queen 增加生命，ant 增加生命与攻击意图。死亡 ant/enemy 记录保留，活动视图排除死亡对象，孵化按历史最大 ID 分配，不按活蚂蚁数分配。尸体食物 Origin=Carcass，来源敌人 ID 在独立属性中保留。


## v0.10 分类库存与小组

ResourceView.FoodType 和 AntView.CarryFood 为注册的食物 ID，与 Origin 无关。FoodStore.Amounts 是全群库存唯一权威映射，Inventory=Total。AntView.GroupId 为管理归属，ReceivedOrder 为仅显式通信更新的命令缓存；HealUntilTick 为进食结束 Tick，EatFood 记录选定类型，LastDamageTick 决定中断。Groups、Claims、FoodEaten、HealthRestored 为 WorldState 元数据，不复制位置真值到组指令。组内共享算法仍保留每蚁独立记忆。所有类型同样是内存状态，尚无磁盘存档。


## v0.11 小组为数据容器

取消组内独立程序；programCatalog 仅按 Species 存三种程序。GroupOrder 的 Parameters／Points 是 Agent 任意定义的数据，Activity 是自由标记；FoodFilter／RadiusCells／Objective 为便利采集查询参数，不是引擎强制行为。Ant.ReceivedOrder 仍仅由成功蚁后请求替换，MCP 修改真实组数据不直接改变离线个体缓存。AssignAnts 先验证所有活蚁再一次性修改，requestId 对批量内容做结构相等比较。
