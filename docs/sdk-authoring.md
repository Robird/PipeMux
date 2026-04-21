# 开发自己的 PipeMux App

`PipeMux.Sdk` 让你把一个普通的 `System.CommandLine` 程序变成可被 Broker 托管、能保留进程内状态的 PipeMux app。

> 想直接复用现成的 sample？看 [`../samples/Calculator/README.md`](../samples/Calculator/README.md)。
> 想用反射加载已有 DLL（无需新建项目）？跳到本页末尾的"用 PipeMux.Host 托管现成 DLL"。

## 最小示例：一个 counter app

```csharp
using System.CommandLine;
using PipeMux.Sdk;

var service = new CounterService();

var incrementCommand = new Command("increment", "Increment counter");
incrementCommand.SetAction(parseResult => {
    parseResult.InvocationConfiguration.Output.WriteLine(service.Increment());
    return 0;
});

var getCommand = new Command("get", "Get current counter value");
getCommand.SetAction(parseResult => {
    parseResult.InvocationConfiguration.Output.WriteLine(service.Get());
    return 0;
});

var resetCommand = new Command("reset", "Reset counter");
resetCommand.SetAction(parseResult => {
    parseResult.InvocationConfiguration.Output.WriteLine(service.Reset());
    return 0;
});

var rootCommand = new RootCommand("My Counter App") {
    incrementCommand,
    getCommand,
    resetCommand
};

var app = new PipeMuxApp("my-counter");
await app.RunAsync(rootCommand);

sealed class CounterService {
    private int _counter;

    public string Increment() => $"Counter: {++_counter}";
    public string Get() => $"Counter: {_counter}";
    public string Reset() {
        _counter = 0;
        return "Counter reset";
    }
}
```

## 关键设计点

1. **状态放在进程内对象里**。Broker 复用同一 app 进程时，状态自然延续；按终端隔离实例靠 Broker 自动完成，app 自身不需要感知"会话"。
2. **命令解析不变**。仍然由 `System.CommandLine` 负责，写法和普通 CLI 程序一致；`PipeMuxApp.RunAsync()` 负责接管 stdin/stdout 并跑 JSON-RPC 协议。
3. **目标框架**：当前为 `net10.0`，`System.CommandLine` 使用 `2.0.6`。完整 API 模式（`Arguments.Add` / `SetAction` / `parseResult.InvocationConfiguration.Output` 等）见 [AGENTS.md](../AGENTS.md) 中"关键库 API 模式备忘"。

## 注册到 Broker

把发布产物（或 `dotnet run` 命令）加到 `~/.config/pipemux/broker.toml`：

```toml
[apps.my-counter]
command = "/path/to/MyCounter"
auto_start = false
timeout = 30
```

或用 `pmux :register`（适合 `PipeMux.Host` 托管的形态，参见下一节）。

调用方式与所有 PipeMux app 一致：

```bash
pmux my-counter increment
pmux my-counter get
```

## 用 PipeMux.Host 托管现成 DLL

如果你的程序只是一份"导出 `static RootCommand BuildXxx()` 的 DLL"，无需自己拉起进程，直接用通用宿主：

```bash
pmux :register my-app /absolute/path/to/MyApp.dll My.Namespace.Entries.BuildRoot
```

Broker 会用 `PipeMux.Host` 反射加载这个入口，并把它当作普通 PipeMux app 暴露出来。完整示例见 [`../samples/HostDemo/`](../samples/HostDemo/)。

## 进一步

- 调试自己的 app → [developer-guide.md](developer-guide.md)
- 架构与 Broker 行为 → [architecture.md](architecture.md)
- Calculator sample 的完整结构 → [`../samples/Calculator/README.md`](../samples/Calculator/README.md)
