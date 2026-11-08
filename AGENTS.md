# 项目开发约定

每次任务开始及恢复时先读取 PROJECT_STATUS.md，核对最新指令与实际代码，再简述当前进度及下一步。阶段变化与结束前更新目标、进度、决定、阻碍、验证、下一步、简短历史及更新时间；只记录已确认事实。

后续开发先读 docs/LUNA_HANDOFF.md 和 docs/FRAMEWORK.md。保持 Core/Programs/Contracts 无 UI/MCP 依赖；世界写入只走 Runtime actor；Agent 不可读取 player 全图投影；坐标使用每格 1000 整数单位。新增规则使用 IWorldRule 和稳定排序。

修改核心后运行解决方案构建及 tools/AntMind.Checks；记录真实验证限制，不把进程调用等同当前 Codex 会话连接成功，不把窗口运行等同完整玩法。没有用户明确授权不要替用户切换模型或启动其他 Agent。

用户要求开启代码跟随时，使用已安装 local-tools.live-code-follow 插件的实际编辑接口，确认 connected=true、paused=false 后再报告成功。.codex-live/ 不进入 Git。


## 当前交接入口

当前 v0.13，先读 docs/AGENT_START_HERE.md、docs/MVP_SPEC.md 和 docs/PRODUCTION.md，再使用历史交接。每蚁种全群共享代码，小组仅为任务数据。完整规则组合在 Runtime/GameRules.cs；数量在唯一实际持有者中，ItemLedger 只汇总，来源段必须伴随所有转移，收益使用 FirstAdmissionPoints。新动作与状态同时更新内容、DSL、MCP、文档及必要检查。
