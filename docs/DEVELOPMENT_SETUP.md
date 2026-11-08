# 开发环境准备

项目使用纯 C# / .NET 10、Avalonia 桌面界面和独立 MCP 服务，不依赖游戏引擎。本文记录当前电脑的配置和其他 Windows 开发者的准备步骤；核心框架已创建，使用命令见 [FRAMEWORK.md](FRAMEWORK.md)。

## 需要的环境

- Windows x64。
- .NET 10 SDK x64。仓库用 `global.json` 固定 10.0.403 基线，并允许使用同一主次版本中的较新功能带；SDK 路径先查仓库本地目录，再查系统安装。
- VS Code 与 Microsoft C#、C# Dev Kit 扩展。
- Git。

Avalonia 项目模板只用于创建桌面工程；Avalonia 与 MCP 的 NuGet 依赖会由后续源码工程还原。当前不需要安装游戏引擎、额外 .NET 工作负载或全局 PATH 配置。

## 当前电脑

- .NET 10.0.403 已用微软官方安装器安装到系统 .NET 目录，并保留仓库忽略目录 `.tools/dotnet` 中的同版本 SDK。SDK 安装器与 ZIP 包的 SHA-512 均与微软发布元数据一致；原有 .NET 9.0.300 未修改。
- 系统 `dotnet` 主机为 10.0.14；仓库根目录的 .NET 10 `global.json` SDK 搜索路径支持 VS Code 解析本地 SDK。
- Avalonia 模板包 `Avalonia.Templates` 12.1.5 已安装到仓库内的 `.tools/dotnet-home`。
- 仓库本地 NuGet 缓存位置为 `.tools/nuget`。
- `scripts/dotnet-local.ps1` 优先使用仓库内 SDK；其他开发者安装系统级 .NET 10 后也可用同一脚本。
- VS Code 已有 C# 2.160.4 与 C# Dev Kit 3.40.215。
- 已有解决方案、框架桌面程序和 MCP 服务；实际验证结果见 PROJECT_STATUS.md。

## 首次准备另一台 Windows 电脑

1. 安装 [.NET 10 SDK x64](https://dotnet.microsoft.com/download/dotnet/10.0)。本仓库不提交 SDK 二进制文件。
2. 安装 VS Code 和 Microsoft C#、C# Dev Kit 扩展。
3. 克隆仓库后，在 PowerShell 中进入仓库目录并检查 SDK：

```powershell
.\scripts\dotnet-local.ps1 --info
.\scripts\dotnet-local.ps1 --list-sdks
```

`scripts/dotnet-local.ps1` 优先使用仓库内 SDK，并将 CLI 首选项、模板用户目录和 NuGet 包缓存放在被 `.gitignore` 忽略的 `.tools` 目录。若仓库内没有便携 SDK，脚本会使用系统安装的 `dotnet`。VS Code 需要 .NET 10 或更新版本的系统 `dotnet` 主机，才能读取 `global.json` 中的本地 SDK 搜索路径。

## Avalonia 模板

当前电脑已安装 Avalonia 模板 12.1.5。需要重装时执行：

```powershell
.\scripts\dotnet-local.ps1 new install Avalonia.Templates::12.1.5
.\scripts\dotnet-local.ps1 new list avalonia
```

Avalonia VS Code 扩展是可选的 XAML 辅助工具，不是编译或运行项目的前提；目前没有自动安装。后续创建工程时再按需要选择并记录扩展。

## 后续源码工程命令

解决方案已创建，可执行以下命令：

```powershell
.\scripts\dotnet-local.ps1 restore AntMind.sln --locked-mode
.\scripts\dotnet-local.ps1 build AntMind.sln --no-restore
.\scripts\dotnet-local.ps1 run --project src/AntMind.Desktop
```

第一次构建时固定 NuGet 版本、提交锁文件，再记录桌面端、无界面核心与 MCP 接入的实际验证结果。

## 官方资料

- [.NET 10 SDK 下载](https://dotnet.microsoft.com/download/dotnet/10.0)
- [.NET 支持策略](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
- [Avalonia IDE 支持](https://docs.avaloniaui.net/tools/ide/)
- [Avalonia 自定义绘图](https://docs.avaloniaui.net/docs/custom-controls)
