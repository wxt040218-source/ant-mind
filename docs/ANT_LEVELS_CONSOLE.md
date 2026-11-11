> 当前v0.16：wire15 / content13 / save4；木材、专职建筑蚁、地图放置和独立透明素材见 [BUILDINGS_WOOD.md](BUILDINGS_WOOD.md)。下面v0.15及更早版本号为历史，个体成长与经济规则继续适用。

# v0.15 个体军衔与调试控制台

更新：2026-11-09。wire=14、content=12、save=3。规则适用于所有注册蚁种，目前包括侦察、搬运、兵蚁和厨师；同种程序仍全群共享，每只蚂蚁等级与记忆独立。蚁后掌管的全群科技／网络继续使用原有五级体系。

## 三个个体等级

| 等级 | 累计经验 | 移动速度 | 工作效率 | 攻击 | 生命上限 |
|---|---|---|---|---|---|
| 1 | 0 | 100% | 100% | 100% | 100% |
| 2 | 30 | 110% | 125% | 125% | 125% |
| 3 | 120 | 125% | 150% | 150% | 150% |

数值集中在 WorldConfig.Veterancy／content/world.json，可调，但等级固定最多三级。经验条满级封顶，CompletedWork 继续累计。载量仍由职业与载量升级决定（搬运 1～10），大脑预算与决策间隔继续由共同大脑决定。

移动和伤害使用整数计算；工作保留百分比余数，避免基础工作率 1／2 时加成被取整吞掉。二级厨师每四 Tick 实际完成十工作单位，三级厨师每 Tick 三单位。每次 work 最多连续十 Tick 的原约定不变，结尾只奖励实际剩余工作。晋级增加生命上限并保留当前生命，食物回血机制仍有用途。职业程序替换、回滚、调整小组不会重置经验。

## 经验来自什么

| 有效贡献 | 默认经验 | 限制 |
|---|---|---|
| 踏入蚁群未到达过的格子 | 1／格 | 同一格全群只奖励一次；同 Tick 按个体 ID 决定 |
| 从果丛／纤维源真实采集 | 1／单位 | 只有源头实际减少且货物增加才奖励 |
| 新食物首次入巢 | 1／原料点 | 复用来源段；出巢／重捡／再交付不能重复奖励 |
| 实际建设或加工 | 每十有效工作单位 1 | 个体保留不足十单位的余数；不按命令调用次数奖励 |
| 有效命中敌人 | 1／次 | 必须实际造成伤害 |
| 击杀敌人 | 额外 5 | 只在敌人首次死亡时奖励 |

出生、站着不动、重走老路、失败交互、攻击尸体、空工位、开始／取消配方均不发经验。施工或加工取消后，已经真实付出的劳动经验保留，不为取消本身发经验。源头补充的新资源可以继续采集成长；丢在地上再拿起不算新采集。

工作事件先累积到 PendingExperience，物理规则全部结算后由 AntExperienceRule（Order48）统一写个体，避免被交互规则的局部数组覆盖。新区域记录由 ExplorationExperienceRule（47）维护，之后才是统计49。待结算经验、已探索格子和工作余数纳入完整存档。

## 查询与代码

AntView 增加 Level、Experience、CompletedWork、WorkExperienceRemainder、WorkRateRemainder、LastExperienceTick／Work。Snapshot.AntLevels 和 get_catalog.AntLevels 提供阈值与加成。

个体 DSL 新增 `ant_level`、`experience`、`experience_to_next`、`move_percent`、`work_percent`、`attack_damage`。它们描述自己的身体和经验，不开放任何隐藏地图数据。一个共享程序可以按经验、负载、组数据选择行为，不增加个体专属程序槽。

地图用等级标记区分老兵，职业菜单显示各等级数量，个体观察显示经验、工作计数、最近贡献及当前生命上限。

## 游戏内控制台

点击地图下方 **控制台 F12**，或按 F12。独立窗口复用当前 GameSession，不创建另一个世界；一个游戏窗口只复用一个控制台窗口。

输入命令后按 Enter 或“执行”。输出保留最近 60000 字符；可查看程序错误、动作结果与对象数据。没有任意 C#／系统命令执行、经验注入或全图透视。它是游戏调试窗口，不是 Windows 终端。

## 命令行入口

第十个项目 `tools/AntMind.Console` 只依赖 Transport，Transport/CommandConsole.cs 是两种控制台共用的解析／执行实现。连接已运行的桌面或无界面宿主；默认管道 antmind-v1，隔离实例通过 ANTMIND_PIPE 指定。不依赖 Codex 是否已原生装载 MCP。

```powershell
# 交互模式，exit 只关闭此客户端
.\scripts\dotnet-local.ps1 run --project tools/AntMind.Console --no-build

# 单次命令
.\scripts\dotnet-local.ps1 run --project tools/AntMind.Console --no-build -- status
.\scripts\dotnet-local.ps1 run --project tools/AntMind.Console --no-build -- 'ant 11'
.\scripts\dotnet-local.ps1 run --project tools/AntMind.Console --no-build -- 'step 100'
```

常用命令：

```text
help
status
ants
ants scout
ant 1
inspect source 1
catalog
groups
pause
step 100
speed 2
resume
save before-change
load before-change
apply scout "C:\project\new scout.ant"
rollback scout 0
eval before-change 100
execute SetMinimumReserve {"value":20}
execute PlanBuild {"key":"workshop","location":{"x":65000,"y":64000}}
```

step 必须暂停，单次 1～1000 步；eval 按已有接口限制 1～1200 Tick，返回两条相同存档的隔离结果，不改实局。更多管理功能都可用 execute CommandKind JSON，字段与 GameCommand 一致，不认识的字段拒绝。每次变更先观察，再生成新 requestId／expected revision／WorldId；加载后自动使用新实例，不绕过 actor、花费、科技或发现权限。版本冲突会返回失败，不暗中重复写入。

apply 从客户端本地读取源码，支持带空格的引号路径，送入原有职业程序校验与 Tick 激活。命令行单次运行遇到错误、拒绝或未知对象返回非零退出码；正常查询返回0。CLI 退出和控制台窗口关闭不停止游戏，也不注册虚假的 Agent 连接指示。

## 存档与验证

save3 保留等级、经验、工作余数、探索记录。加载 v0.14 的 save2/protocol13 或 v0.13 save1/protocol12，先沿旧迁移升级经济规则，再启用三级成长；不发历史经验，所有旧蚂蚁从一级零经验开始，当前所在格记为已到达。旧版本没有探索轨迹，其他格从加载后的新活动开始记录。原文件不改写。

本地锁定还原、全解决方案构建通过，0 警告／0 错误，325 项检查通过。新增个体晋级／封顶、真实速度／伤害／分数工时、首次交付防刷、探索去重、非法劳动、保存与迁移、程序／小组变更保级、控制台权限和共享 actor 等覆盖。不是长期成长平衡结论。

实际桌面已看见控制台按钮、独立窗口、输入和输出，并在窗口内执行 status 查询成功。真实 MCP 完成带经验的建设／加工链，T927 送回浓缩；再由真实命令行控制台 step1000 推到 T1927，5 只侦察三级、1 厨师二级（经验51）、5 搬运一级（各自经验6～15），零程序错误，世界暂停。

演示保存为 `v015-rank-demo`，真实 CLI 又加载旧 quick 副本验证11只旧蚁全部一级零经验，再恢复此演示，等级和累计经验698保持；原 quick 的 SHA256 前后相同。未知对象和错误 JSON 的 CLI 退出码确认为1。

证据：artifacts/v015-checks.txt、artifacts/agent-live/v015-console-*.json、v015-restored-world.json、v015-legacy-world.json、v015-production/。生成数据和本机存档不上传 Git。本轮没有满人口长期压力、所有尺寸／所有控制台命令的独立人工验收，也没有原生当前聊天 MCP 工具装载的声明。
