# v0.19 中转据点与前线补给

2026-11-11。wire／Snapshot18、content15、save5。中转站稳定ID仍为cache，名称改为“中转据点”，保留6木材、100有效工时、40份容量和建筑蚁施工。

## 当前用途

据点共用一份实体库存，用作中转仓储、食物补给和Agent安排的驻守地点。物资仍由蚂蚁实际deposit／withdraw／transfer；补给食物也要真实运输进来。今后的运输系统可以复用现有building ID、位置、容量与库存，不另建第二份权威存储；本轮未提前实现运输系统。

BuildingDefinition.CanFeedAnts标识供餐能力，默认仅cache启用；厨房保持加工用途。建筑本身不增加通信范围、视野或人口，驻守蚂蚁仍按各自实际感知观察。玩家只规划地点和给方向，Agent组合运输、守卫和补给策略。

## 回血规则

新增显式动作 `eat_from(holderId,itemCode)`：0为巢穴，正数为据点building ID。蚂蚁需在一格交互范围内，建筑已建成、允许供餐，库存中有指定的可食用物品。

进食持续现有EatingTicks（默认40Tick／2模拟秒），完成才扣除一份并按FoodDefinition.HealAmount回血，上限为个体MaxHealth。水果3、肉6、浓缩6等现有数值不变。受击会中断，未完成不扣食物；满血、空库、不可食物、未建成和取消的据点均不提供治疗。多人争抢最后一份只允许一次成功消耗。

据点进食明确绑定该库存，不回退为吃携带物或远程巢穴食物。站内实物补给不受巢穴MinimumReserve约束；从巢穴提取食物运往据点时仍遵守储备约束。消费时一并移除实际来源段，计入FoodEaten、HealthRestored和ConsumedItems，不增加FirstAdmissionPoints或发展价值。

原 `eat(type)` 保留携带食物优先、其次本地巢穴的语义。明确 `eat_from(0,type)` 则只吃巢穴食物。

## Agent与默认守卫

- `building_can_feed(id)`：仅对本地可见且已建成、内容定义允许供餐的设施返回1。
- `nearest_supply_id/x/y/distance/food`：只从个体局部可见、已建成且有可食用库存的据点选择最近者；切比雪夫距离，同距按ID。food按内容物品顺序选择有库存且可回血的食物。无可用据点时ID／坐标／food为0、距离int.MaxValue。查询遍历按预算收费，单次决策内缓存选取结果。
- 现有holder_stock、building_phase、命名小组地点和station参数可继续组合据点物流。稳定ID不改为另一种对象。
- 新局默认兵蚁近战优先；局部安全且受伤时会走向可见补给据点并显式eat_from。没有可见补给点时保持原回巢／巢穴进食流程。旧档程序不自动替换，Agent需预检后显式提交。
- 单只与整群预览新增EatHolderId／EatFood，表示候选打算消费的来源和食物；仍不保证物理范围、库存竞争和未来行动成功。

```text
if (health < max_health && nearest_enemy_id == 0 && nearest_supply_id > 0) {
    if (nearest_supply_distance <= 1000) { eat_from(nearest_supply_id,nearest_supply_food); }
    else { set_target(nearest_supply_x,nearest_supply_y); }
}
```

建筑面板显示据点用途、现有补给食物和正在进食的蚂蚁数量。地图回血沿用既有绿色加号反馈，渲染不参与世界结算。

## 存档与验证

save5保存AntView.EatHolderId：-1为旧eat的自动来源，0为巢穴，正数为建筑。进食中存读档保留来源和完成Tick，不提前扣料、不重复消费。程序激活时按原规则取消进食并重置来源为-1。

已知save1～4／wire12～17在内存迁移，cache定义增加CanFeedAnts；旧“中转站”默认名称升级为“中转据点”，自定义名称保留。未携带新字段的旧蚂蚁按-1继续原进食语义，不增加库存、生命或来源点，不改写旧文件。未知版本仍拒绝。

本轮目标为仓储和实物补给据点；建筑生命、占领、免费被动回血、固定运输线路及全局自动派工不在本轮范围。构建、检查和实际补给验证以PROJECT_STATUS.md最终记录为准。

## 2026-11-11验收记录

完整460项检查通过，覆盖实体库存与来源守恒、空库／满血／非法食物／范围／施工／取消、受击中断、最后一份竞争、局部发现权限、共享程序预算、进食中存读档与旧格式迁移。最终全解决方案构建0警告／0错误；Release发布及独立IPC启动／单步通过。最后只修正生产面板旧名称，随后重建并再次发布、烟测，未重复核心检查。

真实桌面由官方STDIO MCP继续原T17132经营档，没有注入材料或生命。运输6份原有木材，在T17801由建筑蚁完成南线据点#2(55,77)，之后从巢穴运输水果。T18003据点存2水果，守卫#14空载24/50HP、EatHolderId=2、HealUntilTick=18033；T18083库存降到0，守卫30HP，FoodEaten26→28、HealthRestored75→81，证明消费来自据点。

之后在1×正常运行60.24现实秒，T18083→19287，守卫在无新增受击的情况下最终50/50HP，返回原驻守点(55,79)。期末据点余3水果，14活蚁／0死亡／蚁后100，所有个体LastError为空、摘要Problems为空。前段为真实宿主受控Step验证，后段为连续正常运行，两段不混记。

最终槽`v019-outpost-final`已在最后发布版本重载，暂停T19287、save5／wire18，旧17个存档SHA256全部未变。施工与运输组使用可读英文名，当前Windows STDIO桥的中文分组名乱码另列待修。共享补给运输为验收策略，保留在新档，尚未成为正式运输系统。单局短时验收不证明长期容量、配额或多据点平衡。

证据在`artifacts/outposts`：build-final.txt、checks.txt、publish-final.txt、desktop-smoke-final.txt、before-meals.json、supply-200.json、healed-80.json、realtime-end.json、release-restored.json及save-hashes-before.json。
