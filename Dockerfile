# VERSION=0.1.0
# Ant Mind headless Linux 运行镜像（WXT 的 Linux 移植：仅确定性核心，去掉 Avalonia 桌面）
# 多阶段：SDK 10.0.403 编译 → runtime 10.0 运行 Evaluator --serve
# 设计依据见 docs/LUNA_HANDOFF.md、docs/FRAMEWORK.md：
#   - Core/Runtime/Programs/Contracts/Transport/Checks/Mcp/Evaluator 全不依赖 Avalonia，可原样跨平台；
#   - ContentLoader 从 AppContext.BaseDirectory/content/world.json 读世界（Directory.Build.targets 会把 content/ 复制到每个 Exe 输出目录）；
#   - SavedGames.DefaultDirectory = AppContext.BaseDirectory/saves，故存档目录随 Evaluator.dll 输出目录走；
#   - McpCheck.cs 从仓库根 spawn 相对路径 dll，因此运行镜像必须保留仓库目录结构，而非 publish 单个工程。

FROM mcr.microsoft.com/dotnet/sdk:10.0.403 AS build
WORKDIR /src
COPY . .
# RuntimeIdentifiers 已含 linux-x64；这里刻意不带 --locked-mode，让 restore 补齐 linux-x64 依赖段。
# 提交进仓库的 9 份 packages.lock.json 由 SDK 10 的 --force-evaluate 预先重生成（见移植报告）。
RUN dotnet restore AntMind.sln
# 只构建 headless 三工程及其依赖链，绕开 Avalonia 桌面工程在 Linux 下的 GUI 依赖。
# 统一 -c Debug：两 py 脚本与 McpCheck.cs 都硬编码 bin/Debug/net10.0/*.dll。
RUN dotnet build tools/AntMind.Checks/AntMind.Checks.csproj -c Debug --no-restore && \
    dotnet build tools/AntMind.Evaluator/AntMind.Evaluator.csproj -c Debug --no-restore && \
    dotnet build src/AntMind.Mcp/AntMind.Mcp.csproj -c Debug --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
# python3 供容器内 scripts/model-eval-bridge.py 使用：UDS CurrentUserOnly 要求 host 与 client
# 同一 UID、同一 TMPDIR、同一 mount namespace，评测驱动必须在同一容器里跑。
# tzdata/ca-certificates 保证 TZ=Asia/Shanghai 与 new-api 网关 HTTPS 调用可用。
RUN apt-get update && apt-get install -y --no-install-recommends python3 tzdata ca-certificates && \
    rm -rf /var/lib/apt/lists/*
ENV TZ=Asia/Shanghai \
    ANTMIND_PIPE=antmind-v1 \
    DOTNET_EnableDiagnostics=0
WORKDIR /app
# 保留整棵构建产物树：McpCheck.cs 的相对路径、各 bin/Debug/net10.0/content/world.json 都在其中。
COPY --from=build /src/ /app/
# 存档目录由 compose 把 /data/100_AppData/ant-mind-linux/ 挂到此处（HDD）。
# Evaluator --serve 与 MCP 经同一 GameSession(Gateway) 读写同一 saves 目录。
RUN mkdir -p /app/tools/AntMind.Evaluator/bin/Debug/net10.0/saves
# 刻意不写 EXPOSE：headless 版不监听任何 TCP 端口，host 与 client 只走 /tmp/CoreFxPipe_antmind-v1 的 UDS。
ENTRYPOINT ["dotnet", "tools/AntMind.Evaluator/bin/Debug/net10.0/AntMind.Evaluator.dll", "--serve"]
