# 开发环境准备

2026-11-05：项目改为 C# / .NET + Avalonia 桌面方案。此文记录实际准备情况，不代表源码已经构建成功。

## 需要的环境

- Windows x64，首版开发与发布平台。
- .NET 10 LTS SDK x64，项目目标 net10.0。
- VS Code、C#、C# Dev Kit。
- Git；通过 GitHub PR 协作。

Avalonia Desktop、官方 MCP C# SDK 和 xUnit 均随源码工程通过 NuGet 还原，不需要独立安装游戏引擎或全局模板。Windows 上 Avalonia 除 .NET SDK 外无需额外工作负载。

## 当前电脑的核验结果

- 已有 .NET SDK 9.0.300 与 VS Code C#／C# Dev Kit。
- .NET 10 安装曾启动后取消，尚未确认安装完成；实现 M1 前须安装并核验 .NET 10 SDK。
- 当前无 C# 解决方案、Avalonia 程序、包锁文件或游戏 MCP 服务。
- 本轮清理结果与 GitHub 同步结果记录在根目录 PROJECT_STATUS.md。

此前为旧方案下载的 Godot 属于项目内便携文件，不是系统级安装。该方案已取消，不应再按旧说明下载编辑器或导出模板。

## 工程创建后的标准流程

以下是 M1 将提供的命令约定，目前源码项目尚不存在，不能作为现成启动命令使用：

```powershell
dotnet --info
dotnet restore AntMind.sln --locked-mode
dotnet build AntMind.sln --no-restore
dotnet run --project src/AntMind.Desktop
```

首次建立工程时固定 SDK、集中包版本和锁文件，补充 MCP 桥接启动方式、发布命令与实际验证结果。

## 官方资料

- [.NET SDK 下载](https://dotnet.microsoft.com/download)
- [.NET 支持策略](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
- [Avalonia 环境与平台要求](https://docs.avaloniaui.net/docs/supported-platforms)
- [Avalonia 自定义绘图](https://docs.avaloniaui.net/docs/custom-controls)
