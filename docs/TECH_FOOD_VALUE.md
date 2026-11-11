> 当前v0.16：wire15 / content13 / save4；木材、专职建筑蚁、地图放置和独立透明素材见 [BUILDINGS_WOOD.md](BUILDINGS_WOOD.md)。下面v0.15及更早版本号为历史，个体成长与经济规则继续适用。

# v0.14 科技与食物价值

更新：2026-11-09。wire=13、content=11、save=2。科技直接使用现有 NetworkLevel，不新增第二套研究经验或科技货币；升级虫群网络同时增加通信范围并开放内容。

## 五级成长线

| 等级 | 阶段 | 开放内容 |
|---|---|---|
| 1 | 基础采运 | 侦察／搬运／兵蚁、中转箱、基础食物 |
| 2 | 集体分工 | 厨房、厨师、水果浓缩 |
| 3 | 蛋白料理 | 蛋白餐 |
| 4 | 均衡营养 | 多食材组合的均衡营养餐 |
| 5 | 蚁王盛宴 | 以均衡营养餐进一步制作高级盛宴 |

范围与费用继续使用 NetworkRanges／NetworkUpgradeCosts：16/24/32/48/64 格，升下一级消耗 60/160/400/1000 价值点。原有大脑和载量升级保持自己的等级，不与科技强行绑定。

厨房沿用稳定 ID `workshop`，中文名改为厨房，仍为 12 纤维＋200 有效工作 Tick、输入／输出各 20、单工作位。原有物流、施工和劳动方式保留，Agent 必须组织实物运送，不远程扣建材。

**门槛在真实入口生效**：PlanBuild、work、process、HatchEgg 均检查相应等级。进食、运输、撤回在制材料不要求重新解锁食物；旧档已有厨房／厨师会保留，但低于等级时不能继续厨房施工或加工，不销毁物品、不免费提升等级。

## 食物与配方

| ID | 名称 | 每份发展价值 | 每份回血 |
|---|---|---|---|
| fruit | 水果 | 1 | 3 |
| meat | 肉 | 1 | 6 |
| fiber | 纤维 | 0，不可支付 | 不可食用 |
| fruit_concentrate | 水果浓缩 | 3 | 6 |
| protein_meal | 蛋白餐 | 4 | 12 |
| balanced_meal | 均衡营养餐 | 10 | 24 |
| royal_feast | 蚁王盛宴 | 28 | 60 |

| 配方代码／ID | 所需等级 | 输入→输出 | 工时 | 单批价值增长 |
|---|---|---|---|---|
| 1 / concentrate | 2 | 2 水果→1 水果浓缩 | 100 | 2→3，增值 1 |
| 2 / meal | 3 | 2 肉→1 蛋白餐 | 100 | 2→4，增值 2 |
| 3 / balanced | 4 | 1 水果浓缩＋1 蛋白餐→1 均衡营养餐 | 140 | 7→10，增值 3 |
| 4 / royal | 5 | 2 均衡营养餐→1 蚁王盛宴 | 200 | 20→28，增值 8 |

工作率仍为厨师 2，其余职业 1；每次显式 work 最多连续 10 Tick。表中均为有效工时，不是调用一次就瞬间完成。当前产物每批一份、质量均为 1。

多原料由 RecipeDefinition 的主 Input/InputCount 加 ExtraInputs 字典定义，Ingredients() 为完整输入。process 检查全部原料与输出空间后一次性移入 WIP；少一类就不动任何输入。完成只消耗一次在制、生成一次输出；取消只退回当前在制一次。所有来源点随完整输入汇总到输出，混合四份原料不能丢掉其中一类的点数。

## 支付和收益的含义

- Inventory 仍表示巢穴实际可食用份数，不改成点数；Stock 是逐类型实物数量。
- StoredFoodValue＝巢穴可食用数量×Value 的总和。DevelopmentChange 是已经消费整份食物后留存的找零价值，不能当实物搬运或进食。
- DevelopmentValue＝StoredFoodValue＋DevelopmentChange。卵、大脑、载量、虫群网络的费用及 MinimumReserve 均按这一个价值余额结算。
- 优先使用余额，再按低单位价值、类型 ID 顺序消耗食物，避免不必要地花掉高级料理。若只有价值 3 的浓缩，用 7 份支付 20，余下 1 点记入找零，不凭空返还一份食物，也不丢掉这 1 点。
- 从巢穴取食物或进食时，按整份的 Value 扣减同一个储备约束。野外自己携带的食物仍可显式吃，不远程调用巢穴存货。

FirstAdmissionPoints 保留原始食物首次入巢计分，重复搬运不增加。新增 ProcessingValueAdded：只在配方真正完成时记 `max(0,产出价值−实际在制输入价值)`。两项分别展示，移动、出巢、取消、开始加工和重复 work 都不能重复奖励。经济统计和隔离评估同时返回加工增值、价值变化，避免把运输次数当收益。

数量守恒继续由唯一实物持有者和 ItemLedger 汇总验证。找零是已消费食物转化后的发展价值，不是另一份物品库存；实际消耗种类及数量仍登记 ConsumedItems。付款入口统一为 TechnologyProgression.Pay，避免在 Runtime 重复登记 Tick 内已经发生的进食消耗。

## Agent 与 UI

- Snapshot 新增 TechnologyLevel（等于 NetworkLevel）、TechnologyEra、TechnologyEras、StoredFoodValue、DevelopmentValue、DevelopmentChange、ProcessingValueAdded。Technologies 自动汇总能力、蚁种、建筑和配方门槛，并提供 Category／ContentId／Unlocked。
- get_catalog 返回阶段、完整解锁列表、物品 Value、配方 ExtraInputs 和当前发展价值。管理命令名称不变，升级继续使用 UpgradeNetwork。
- 个体通过显式 request_queen_info 才更新 KnownTechnologyLevel；不会因网络升级自动知道新等级。DSL `technology_level` 是缓存值，`recipe_unlocked(code)` 根据缓存与公开配方检查，`food_value(itemCode)` 读公开价值。
- DSL 新增 food_balanced、food_royal；仍推荐 item_code("稳定ID")。默认厨师在未满足配方等级时等待，其他算法由 Agent 决定。
- 蚁后菜单展示虫群网络／科技阶段和完整解锁清单；未解锁厨师无法孵化。生产页显示原料组合、门槛、价值增长、回血和两种收益；顶部同时显示食物份数与发展储备。

## 扩展校验

建筑／配方／蚁种 RequiredNetworkLevel 不能超出现有网络等级。配方等级不能低于所需建筑；全部输入必须在该等级有自然来源或可达的前置配方，输入总量不能超过工位输入容量。配方图禁止循环，避免反复加工无限刷价值；注册表顺序不影响依赖判定。新食物、配方和科技主要通过内容配置扩展，新的物理规则仍须使用 IWorldRule。

## 存档迁移与验证

save=2 保存价值、余额、加工增值和混合配方。兼容 v0.13 的 format=1 / protocol=12：在内存中升级已知物品价值、厨房／厨师／配方门槛并加入两道后续配方。原 Tick、网络等级、数量、程序、记忆、在制和来源保留，不补历史增值，也不改写原文件。自定义旧配方占用了新代码等冲突时明确拒绝，不能猜测或覆盖。

本轮全解决方案构建 0 警告／0 错误，294 项检查通过。新增覆盖锁定规划／施工／加工／孵化、混合缺料原子性、来源汇总、四五级配方、精确找零、价值储备、科技缓存、依赖图、旧档兼容及所有厨师分支 64 指令预算。

独立宿主通过真实 MCP：二级网络下 T201 建箱、T592 建厨房、T937 入巢 1 份浓缩，库存 20 水果＋1 浓缩＝21 份／23 价值点，首次入巢 2 点、加工增值 1，程序零错误。相同存档各推进 100 Tick 均再产 1 浓缩，增值＋1、发展价值＋3，零死亡／错误。证据 artifacts/agent-live/v014-production 与 technology-checks-final.txt。

原桌面 quick 存档副本也经真实 MCP 成功加载为新协议，T937／原数量不变、科技仍一级、厨房未解锁，发展价值重算为 83；原 quick 文件 SHA256 前后一致。本轮不覆盖用户存档，没有窗口目视验收、长期满人口压力或新经济平衡结论。
