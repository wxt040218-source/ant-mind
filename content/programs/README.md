> v0.11 当前：所有组同类蚂蚁只执行一个共享程序，--group 提交已拒绝。根目录三份默认程序能读取缓存小组参数；Agent 通过通用 API 管理小组，代码通过 param("key")、group_x/y("point") 任意解释任务。下面 v0.10 的按组提交说明已废弃，groups 子目录仅为可选蚁种级语法示例。

# 可提交的蚁种程序

这些 DSL 文件现在编译嵌入 Programs 程序集，是新局默认职业策略；修改文件后重新构建。仍可通过 Agent API 热更新当前游戏。当前没有完整存读档，运行中其他临时版本不会自动跨重启保存。

- scout-explorer.ant：分段巡查、周期返巢、明确确认上传，无主动拾取搬运。memory[0] 为阶段（0 选 X／4 选 Y／3 巡查／1 返巢／2 等待通信）；[1] 为巡查截止 Tick，[2] 为通信起始 Tick，[3..4] 为目标坐标，[5] 为路线序号。
- hauler-reliable.ant：缓存目标调度，到达后局部验证、拾取，空点返巢刷新，失败交互重试。memory[0] 为阶段（0 调度／1 在途或到达处理／2 返巢／3 等待通信），[1] 为缓存目标索引，[2] 为通信起始 Tick。

应用示例：

```powershell
python scripts/game-agent.py apply --source content/programs/scout-explorer.ant --species scout --reason "Scout patrol with committed return"
python scripts/game-agent.py apply --source content/programs/hauler-reliable.ant --species hauler --reason "Validate targets and refresh stale information"
```

暂停时需单步一次或继续运行，程序才会在 Tick 边界激活。每次替换会清空该种类程序记忆与目标，身体、负载与世界保留。

BehaviorProgramCheck 覆盖 64 指令预算下的主要状态和 20 条资源记录，以及 5000 Tick 采运回归。密集堆积更多资源时仍受游戏指令预算限制，不作无上限规模保证。

- soldier-guard.ant：兵蚁在巢穴附近守卫，局部发现敌人后接近并显式 attack；随程序集作为新局默认兵蚁程序。


v0.10：soldier-guard.ant 具备显式回巢进食行为。groups/*.ant 是可提交的小组示例，不自动覆盖全群：scout-expedition 探索目标点，hauler-collector 认领并运送局部食物，soldier-escort 跟随局部组员、攻击及低血补给。以 --group ID 指定覆盖，--species 仍必须匹配职业；暂停时单步激活。初级预算用于小队原型，大量局部队友／资源可能需要升级。
