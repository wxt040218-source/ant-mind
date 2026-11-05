# 桌面版技术栈

2026-11-05：用户已确认 Godot + C#、Windows x64、项目级 MCP。当前仅完成规划，尚未安装、构建或验证。完整最小版规则见 [MVP_SPEC.md](MVP_SPEC.md)。

## 技术组合

| 部分 | 技术 | 责任 |
| --- | --- | --- |
| 引擎 | Godot 4 稳定版 .NET 版本 | 2D 像素画面、输入、音效与 Windows 导出 |
| 主语言 | C# | 游戏与桥接逻辑，减少维护边界 |
| 界面 | Godot Control + Theme | 地图外围面板、程序查看与调试 |
| 模拟 | 独立纯 C# 类库 | 资源、采运、信息素、经济、计费与回放 |
| 算法 | 受限文本 DSL 与 C# 解释器 | 共享程序、个体记忆、指令预算 |
| Agent | 当前 Codex + 项目级 MCP | 受控观察、更新程序、增员、升级 |
| 存储 | System.Text.Json，JSON 存档与 JSONL 日志 | 本地状态、配置、程序版本和操作 |
| 验证 | xUnit + 无画面评估器 | 规则、确定性、预算、信息过滤与策略对比 |
| 协作与发布 | GitHub PR、Actions、Releases | 后续自动验证与完整桌面包分发 |

运行环境使用所选 Godot 支持的 .NET LTS。SDK、目标框架、引擎和同版本 .NET 导出模板在接入阶段共同锁定，先验证编译和 Windows 运行，再固定版本。当前没有依赖锁文件，不声称兼容性已验证。

## Agent 接入

项目级 STDIO MCP 桥为推荐落地方式，桥接运行中的桌面游戏；本地 IPC 细节在最小连通性验证时确定。规划工具包括读取状态、读取／校验／应用程序、回滚、增员和升级。

首版没有内置模型服务调用或后台常驻优化器，用户在 Codex 指定单轮／多轮任务。游戏不需要配置模型服务商密钥；首次 MCP 配置可能需重启连接或会话，须实际核验当前 Codex 工具出现及读写成功。

## 模拟与性能

原型为 20 Tick／模拟秒、初始 10 只、最多 100 只工蚁，属于待验证目标。地图无障碍，不引入复杂寻路。目标移动与程序决策独立，固定速度与动作冷却不能被更频繁的指令突破。

先单线程更新模拟，使用稳定顺序、有种子随机数、有限查询和有界数据。渲染位置插值；只有实测需要时才加入批量绘制或后台模拟线程。

## 数据与发布

先采用本地存档，无数据库和云服务。手动存档保存完整世界、程序、记忆、随机数、配置和事件时序，读档暂停，无离线发展。

后续发布完整 Windows 导出目录的压缩包，含可执行程序及必要组件；必须在目标环境启动、运行采运闭环并检查存读档。公开发布与 CI 仅为后续计划，本轮未执行。

## 规划目录

```text
src/
  AntMind.Game/       Godot 场景、界面、画面适配
  AntMind.Core/       资源、行动、信息素、经济与统计
  AntMind.Programs/   DSL、校验、解释器、预算
  AntMind.Bridge/     MCP 与运行中游戏的本地通信
tools/
  AntMind.Evaluator/  无画面运行、回放与算法比较
tests/
  AntMind.Core.Tests/
  AntMind.Programs.Tests/
assets/
docs/
```

目录为规划，尚未建立源码工程。建议一人负责画面和交互，一人负责模拟、DSL 与桥接，具体人员待约定。

## 参考资料

- [Godot .NET 下载与模板](https://godotengine.org/download/windows/)
- [Godot C# 环境要求](https://docs.godotengine.org/en/stable/tutorials/scripting/c_sharp/c_sharp_basics.html)
- [Windows 导出](https://docs.godotengine.org/en/stable/tutorials/export/exporting_for_windows.html)
- [.NET 支持策略](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
- [Codex MCP 官方文档](https://learn.chatgpt.com/docs/extend/mcp)
