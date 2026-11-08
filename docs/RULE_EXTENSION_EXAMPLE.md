# 规则扩展示例

开发者可创建普通 C# 项目引用 Core，实现 IWorldRule，并使用 GameSession(config, seed, extraRules: [...]) 追加。所有规则按 Order、再按 Id 的 Ordinal 顺序排序；重复 ID 拒绝。Runtime 内置局部观察规则 Order=5，程序=10，显式通信=15，移动=20，交互=30，资源刷新=40。

以下可选实验规则使通信半径每 200 Tick 在 8／16 格间切换；默认游戏不注册它。

```csharp
using AntMind.Core;
public sealed class PulsingQueenRule : IWorldRule {
    public string Id => "example.pulsing-queen";
    public int Order => 4;
    public WorldState Apply(WorldState world) => world with {
        Config = world.Config with { QueenRangeCells = world.Tick / 200 % 2 == 0 ? 8 : 16 }
    };
}
```

规则只能从输入不可变状态计算下一状态，不能使用系统时间、UI 帧率或全局随机数。随机扩展必须读写 WorldState.RandomState。C# 规则是可信开发者扩展；外部 Agent 的程序仍只使用受限 DSL。

新的可配置属性写入带版本内容并增加校验；新指令声明读取权限和预算；新操作先接入 Runtime 命令再接 UI／MCP，详见 AGENT_API.md。
