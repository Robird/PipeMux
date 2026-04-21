# PipeMux 使用说明

PipeMux 是面向 LLM Agent 的本地进程编排框架。它把长生命周期、有状态的 CLI App 托管在本地 Broker 后面，让前端 CLI 或 Tool Calling 可以反复复用同一应用进程，同时按终端隔离状态。

## 先选工作流

| 场景 | 推荐文档 | 典型命令 |
|------|----------|----------|
| 本机日常使用、希望命令稳定且不依赖仓库当前目录 | [`ubuntu-deployment.md`](ubuntu-deployment.md) | `pmux ...` / `systemctl --user ...` |
| 正在修改代码、调试 Broker/CLI/sample，接受直接从源码树运行 | [`pipemux-quickstart.md`](pipemux-quickstart.md) | `dotnet run --project ...` |

对大多数“已经在这台 Ubuntu 机器上装好了 PipeMux、只是想稳定调用 app”的 Agent 来说，优先走安装工作流会更合适；对贡献代码、排查问题、调试 sample 的场景，源码工作流更直接。

## PipeMux 在解决什么问题

- Broker 托管有状态 CLI App，让状态保留在后台进程里，而不是丢在单次命令执行里。
- CLI 根据终端标识把请求路由到独立实例，避免多个终端共享同一份会话状态。
- Broker 与 CLI 统一支持 Unix Domain Socket / named pipe，连接方式可以通过配置或环境变量覆盖。

## 已安装工作流

如果你已经执行过 [`../scripts/install-ubuntu-user.sh`](/repos/focus/PipeMux/scripts/install-ubuntu-user.sh)，或者准备把当前机器作为长期使用环境，推荐直接把已安装工作流当成主入口：

```bash
./scripts/install-ubuntu-user.sh
pmux :help
pmux :list
systemctl --user status pipemux-broker
```

这条路径的特点是：

- `PipeMux.Broker`、`PipeMux.CLI`、`PipeMux.Host` 会被发布到 `~/.local/share/pipemux/`
- `pmux` 与 `pmux-host` 会出现在 `~/.local/bin/`
- Broker 由 user-level `systemd` 服务托管，日常不需要手工开一个 `dotnet run` 终端

完整说明见 [`ubuntu-deployment.md`](ubuntu-deployment.md)。

## 源码开发工作流

如果你正在调试当前仓库里的代码，或者希望直接把 `samples/Calculator`、`samples/HostDemo` 挂到 Broker 后面跑，推荐使用源码工作流：

```bash
dotnet build PipeMux.sln --nologo
dotnet run --project src/PipeMux.Broker -c Debug
dotnet run --project src/PipeMux.CLI -c Debug -- :list
dotnet run --project src/PipeMux.CLI -c Debug -- calculator push 10
```

这条路径会把 `dotnet run` 明确保留在文档里，但只用于“改代码 / 调试”场景，不再把它伪装成默认日常使用方式。完整步骤见 [`pipemux-quickstart.md`](pipemux-quickstart.md)，示例配置见 [`examples/broker.toml`](examples/broker.toml)。

## 开发自己的 PipeMux App

`PipeMux.Sdk` 允许把普通 `System.CommandLine` 应用包装成可被 Broker 托管的有状态 app：

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

关键点：

1. 状态放在进程内对象里，Broker 复用同一 app 进程时，状态就会持续存在。
2. 命令仍然由 `System.CommandLine` 解析；`PipeMuxApp.RunAsync()` 负责接管 stdin/stdout 与 JSON-RPC 协议。
3. 当前项目目标框架是 `net10.0`，`System.CommandLine` 使用 `2.0.6`。

## 终端标识

PipeMux 会自动检测终端标识来实现多终端隔离：

| 环境 | 检测方式 | 标识格式 |
|------|----------|----------|
| VS Code 终端 | `VSCODE_IPC_HOOK_CLI` | `vscode-window:{uuid}` |
| Windows Terminal | `WT_SESSION` | `wt:{guid}` |
| 传统 Windows | `GetConsoleWindow()` | `hwnd:{hwnd}` |
| Linux/macOS | `/proc/self/fd/0` / session id fallback | `tty:/dev/pts/N` |

需要固定到某个会话时，可以显式指定：

```bash
export PIPEMUX_TERMINAL_ID=session-A && pmux calculator push 10
export PIPEMUX_TERMINAL_ID=session-B && pmux calculator push 99
```

源码工作流下也可以把 `pmux` 换成 `dotnet run --project src/PipeMux.CLI -c Debug --`。

## 测试

```bash
dotnet build PipeMux.sln --nologo
bash tests/test-management-command-parse.sh
bash tests/test-management-commands-e2e.sh
dotnet run --project samples/TerminalIdTest
```

## 进一步阅读

- [`ubuntu-deployment.md`](ubuntu-deployment.md) - Ubuntu 上的安装、发布与 systemd 使用方式
- [`pipemux-quickstart.md`](pipemux-quickstart.md) - 从源码树调试 Broker/CLI/sample 的步骤
- [`examples/broker.toml`](examples/broker.toml) - 偏向源码调试工作流的配置示例
- [`../samples/Calculator/README.md`](/repos/focus/PipeMux/samples/Calculator/README.md) - Calculator sample 的细节
- [`pipemux-broker-architecture.md`](pipemux-broker-architecture.md) - 更完整的架构说明
