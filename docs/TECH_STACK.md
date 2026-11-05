# 无游戏引擎技术栈

2026-11-05：根据发起人最新要求移除游戏引擎，采用 C# / .NET + Avalonia 桌面方案。玩法与可调初值继续以 [MVP_SPEC.md](MVP_SPEC.md) 为准；本文是技术规划，尚未建立源码工程。

## 技术选择

| 部分 | 选择 | 责任 |
| --- | --- | --- |
| 语言与运行时 | C#、.NET 10 LTS，目标 net10.0 | 核心、桌面和桥接统一语言 |
| 桌面界面 | Avalonia Desktop + MVVM | 窗口、面板、输入、主题与程序查看 |
| 地图绘制 | Avalonia 自定义 Control / DrawingContext | 像素地图、蚂蚁、资源、信息素与选择 |
| 模拟 | 无 UI 依赖的纯 C# 类库 | 固定 Tick、移动、采运、经济和统计 |
| 算法 | 自定义受限文本 DSL + C# 解释器 | 解析、校验、预算与独立记忆 |
| 扩展契约 | 独立 C# Contracts 类库 | 版本化 DTO、命令、规则接口与内容标识 |
| Agent | 独立 STDIO MCP 服务，使用官方 C# SDK | 连接 Codex 或其他 MCP 客户端 |
| 本地通信 | Windows 命名管道 + 有界长度前缀 JSON 消息 | 桥接进程连接运行中的桌面宿主 |
| 持久化 | System.Text.Json；JSON 存档、JSONL 操作日志 | 带 schemaVersion 的状态与回放 |
| 验证与发布 | xUnit、无界面评估器、dotnet publish、GitHub Actions | 规则验证、策略比较、Windows x64 包 |

Avalonia 提供桌面 UI 和绘图；模拟时钟、地图坐标、程序调度、规则与存档由项目实现。首版使用框架绘图 API，只有实测需要时才直接使用 SkiaSharp。无需游戏引擎、浏览器或引擎导出模板。

.NET 10 是项目目标，不把本机已安装的 .NET 9 当作长期基线。Avalonia 与 MCP SDK 选择实现当时兼容 net10.0 的稳定版本，首次成功构建后提交 global.json、Directory.Packages.props 和 packages.lock.json；CI 使用锁定还原。当前尚未锁定包版本或验证构建。

## 模块与贡献入口

```text
src/
  AntMind.Contracts/   DTO、命令、查询与扩展接口
  AntMind.Core/        确定性世界与规则
  AntMind.Programs/    DSL 解析、校验与计费解释器
  AntMind.Runtime/     Tick 调度、命令队列、快照与存档
  AntMind.Desktop/     Avalonia 窗口、MVVM 与像素地图
  AntMind.Mcp/         STDIO 工具与命名管道客户端
tools/
  AntMind.Evaluator/   无界面运行、回放和策略比较
tests/
  AntMind.Core.Tests/
  AntMind.Programs.Tests/
  AntMind.Integration.Tests/
content/               JSON 配置、默认程序与内容清单
assets/                原创像素素材
docs/
```

目录是规划，尚未创建。日常扩展优先通过 JSON 内容或普通 C# 项目引用完成，新增行为通过明确接口注册。首版不加入动态 DLL 热加载或通用插件市场。多人协作可以分别负责核心规则、解释器、桌面界面和 MCP 适配。

## 性能与分发

20 Tick／模拟秒，初始 10 只、最多 100 只工蚁。模拟在单独的串行执行上下文内更新，界面消费只读快照；后台运行不依赖窗口绘制频率。先绘制一个地图控件，避免每只蚂蚁对应一棵复杂 UI 控件树。暂停、单步与倍速共用 Tick 推进逻辑。

Windows x64 首发使用自包含目录发布并打 ZIP，保留必要的原生依赖；首版不采用单文件裁剪或 Native AOT。其他桌面平台仅保留架构适配空间，另行验证后再承诺支持。

## 官方资料

- [Avalonia 自定义控件与绘图](https://docs.avaloniaui.net/docs/custom-controls)
- [Avalonia 平台与环境要求](https://docs.avaloniaui.net/docs/supported-platforms)
- [.NET 支持策略](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
- [官方 MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk)
- [Codex MCP 文档](https://learn.chatgpt.com/docs/extend/mcp)
