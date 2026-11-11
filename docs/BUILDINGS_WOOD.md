> v0.19：中转站升级为仓储与食物补给据点，保留稳定cache节点供以后运输系统使用；eat_from与本地补给查询见 [OUTPOSTS.md](OUTPOSTS.md)。wire18／content15／save5，兼容已知save1～4旧档。

# v0.16 木材、建筑蚁与可放置建筑

更新：2026-11-10。wire / Snapshot=15，content=13，save=4。本文取代旧文档中的“纤维施工、所有职业可建设、厨师临时帽子”说明；经济价值、个体经验和共享职业代码沿用 v0.14／v0.15。

## 玩法

打开右侧“建筑”页，选择中转站或厨房，再左键点击地图放置蓝图。鼠标移动时显示半透明预览，绿色／红色外框表示地点是否可用。右键、Esc 或“取消放置”退出，放置期间仍可缩放、拖动。落点吸附整数格；Agent 的 PlanBuild 仍接受每格1000单位的整数坐标。两个入口使用同一 Runtime 命令和 BuildingPlacement 校验。

蓝图不消耗巢内材料或食物，不自动建成。蚂蚁必须实际采集木材，走到工地，一格内显式 deposit；只有建筑蚁可以 work 推进蓝图，完工一次性消耗工地材料。已有中转站和厨房不阻挡行走，也不扩展虫群网络。

| 稳定 ID | 名称 | 科技 | 木材 | 有效工时 | 容量 |
|---|---|---|---|---|---|
| cache | 中转站 | 1 | 6 | 100 | 40 |
| workshop | 厨房 | 2 | 12 | 200 | 输入含在制20，输出20 |

建筑与蚁后、其他未取消工地中心至少相距2格。检查不读取未知资源或敌人，因此不能靠放蓝图探测隐藏物体。最多64条建筑记录，取消记录也占计数；暂不提供拆除已完工空闲建筑或建筑碰撞寻路。

不足材料时不推进工时或经验。非法工种不占工作位，单工位按有效工蚁ID顺序结算。每次 work 至多持续10 Tick，可被受击中断；未完成进度保留。取消工程留下原地材料，显式 withdraw 可取回；取消加工只退回实际在制输入。管理命令即时更新已知建筑投影，暂停时也能看到取消结果。

## 木材与新职业

新局8果丛、4木材源，不再新生纤维源。木材源每个容量60，600 Tick恢复1，满额不积攒；首个在巢穴西侧5格，其余按种子放置。木材 `wood` 不可食用、价值0、不计食物储备或首次入巢收益；仍走相同类型库存、负载减速、运输与守恒统计。`fiber` 保留为旧物品ID，数值代码不变。不要将 itemCode 硬编码为8：读取目录或用 `item_code("wood")`。

建筑蚁 `builder`：一级科技开放、初始0只，通用卵孵化，生命20、空载／满载移动110／70、载量2、视野4、工作率2。继承个体三级经验与成长。全群共享 `content/programs/builder.ant`，没有小组任务时等待；小组参数 `station` 指定工地ID、objective指定目的地，到达后显式 work，建成后等待。Agent 可自由改写这份职业代码。

厨师仍需二级科技，使用独立职业程序处理现有四条配方。完工厨房的加工劳动仍允许其他职业显式执行，厨师工作率2；只有蓝图施工受到 BuilderSpecies 约束。物流、分组、工位调度和新工地分配由 Agent 完成，没有自动派工或自动领取木材。

## Agent 与扩展接口

MCP工具仍为9个。`get_catalog` / `observe` 提供职业、物品、建筑和配方定义；`inspect_object` 支持建筑与源头独立记录。

- `PlanBuild(key="cache"|"workshop", pointX, pointY, reason)`、`CancelBuild(value=id)`；失败 `site_occupied` 不改变版本。
- 建筑定义 `MaterialType`、`MaterialCost`、`BuilderSpecies` 可扩展其他建材与工种，`RequiredNetworkLevel` 仍约束蓝图／施工／加工。保存的 `ConstructionItem` 保留该工地的建材种类。
- 动作 `harvest(source,qty)`、`deposit(holder,itemCode,qty)`、`withdraw`、`work(site)`、`process(site,recipe)` 沿用原规则；0号holder是巢穴。
- 新增DSL `food_wood`、`species_builder`，以及只读局部工地的 `building_phase(id)`（未知0、蓝图1、已建成2、取消3）、`building_material(id)`（物品代码）、`building_cost(id)`。
- 上述对象查询仅使用局部感知；小组任务和蚁后缓存仍须显式通信，不能读取Agent投影代替个体感知。

典型Agent步骤：观察已发现木材源→生产卵／孵化建筑蚁→规划蓝图→分配搬运小组并设置物流数据→建筑组设置 station 和 objective→运足木材并实际施工→厨房建成后分配厨师和食物流。不要把蓝图库存、蚂蚁携带量或来源段复制成第二套数量。

## 存档与兼容

save4 / protocol15 可载入 save1／12、save2／13、save3／14。严格反序列化前只为已知旧格式重命名 `FiberCost`→`MaterialCost`；再逐级迁移内存候选，验证后才替换实局。原文件不改写，加载暂停、旋转WorldId，不补离线发展或历史经验。

旧工地保留 `ConstructionItem=fiber`、原成本、已存材料、施工进度；需建筑蚁继续施工。旧纤维源、库存、携带、在制和程序历史保留。迁移加入建筑蚁定义及默认程序、木材物品和按种子补充的木材源，生成量同步登记；重复载入save4不再追加。新规划默认用木材，旧已建成设施和经验继续有效。

## 美术

新增透明PNG：`Assets/Roles/chef.png`、`builder.png`；`Assets/World/cache.png`、`workshop.png`、`beetle.png`。菜单与地图共用RoleArtwork缓存并最近邻缩放；工地半透明并显示施工条，取消工程变淡。甲虫按用户意见用蓝绿色甲壳区分棕红蚁群。原四张角色PNG保留。

使用内置图片生成工具，以原侦察／搬运PNG为参考。完整提示词保存在 [ART_V016_PROMPTS.md](ART_V016_PROMPTS.md)，尺寸、四角透明度和SHA256在 `Assets/manifest-v016.json`；原图是高分辨率像素插画，不是严格48×48源图，也未制作动画帧。

## 验证与接手

全解决方案最终构建0警告／0错误，352项检查通过：新工种资格、非法工种不占位、木材生长与物品守恒、同工位冲突、最后工时计经验、取消原子性、物品代码、一级预算、旧字段／自定义成本迁移、存档幂等，及原有战斗、采运、科技、程序与真实STDIO MCP回归。证据 `artifacts/v016-build-final.txt`、`v016-checks-final.txt`。

真实桌面MCP生产：T1157完成两座木材建筑并送回1份水果浓缩，首次入巢点2、加工增值2、建筑蚁Lv2／经验35、厨师Lv1／经验20，无程序错误。相同档两分支各推进100 Tick均新增2食物点、0死亡／程序错误。证据 `artifacts/agent-live/v016-production/`；可重跑 `scripts/production-demo.py`，要求T0暂停的新世界，不能对既有蚁群强制重置。

原生窗口已检查建筑卡、六职业、实际地图点击创建#3蓝图；通过接口取消并留下记录。用户quick原档及v015成长档均在实宿主加载迁移成功，quick SHA256前后一致；随后恢复本轮演示。独立槽 `v016-construction-demo` 保存T1157，原quick未覆盖。当前未做满人口长期压力、所有分辨率或新建筑长期平衡测试；原生游戏MCP工具是否装载到当前聊天仍须单独验收。

最终Windows自包含包在 `artifacts/desktop/AntMind.Desktop.exe`，发布包IPC 0→1与最终窗口复核通过。最终包将厨师更新为v1后单步到T1158并保存在其saves/v016-construction-demo.json，仍暂停且无程序错误；原Debug演示槽T1157和用户quick均保留。发布验证不代表长期性能验收。
