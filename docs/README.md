# PipeMux 使用说明

> **PipeMux** - Local Process Orchestration via Named Pipes
> 
> 面向 LLM Agent 的本地进程编排框架，通过 Named Pipe 实现 CLI 与后台持久进程的通信。

## 核心概念

```
┌─────────────────────────────────────────────────────────────────┐
│                        PipeMux 架构                              │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│   Terminal A                    Terminal B                       │
│   ┌─────────┐                   ┌─────────┐                     │
│   │ CLI     │                   │ CLI     │                     │
│   └────┬────┘                   └────┬────┘                     │
│        │ TerminalId:A                │ TerminalId:B             │
│        └──────────┬─────────────────┬┘                          │
│                   ▼                 ▼                            │
│              ┌─────────────────────────┐                        │
│              │        Broker           │  Named Pipe            │
│              │   (进程管理 + 路由)      │  pipemux-broker        │
│              └─────────┬───────────────┘                        │
│                        │                                         │
│          ┌─────────────┴─────────────┐                          │
│          ▼                           ▼                           │
│   ┌──────────────┐           ┌──────────────┐                   │
│   │ Calculator:A │           │ Calculator:B │   独立进程实例     │
│   │  Stack:[10]  │           │  Stack:[99]  │   按终端隔离       │
│   └──────────────┘           └──────────────┘                   │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

**核心特性**：
- **多终端隔离**：不同终端的 CLI 调用路由到独立的后台进程
- **状态持久化**：后台进程在多次 CLI 调用之间保持状态
- **跨平台**：支持 Linux/WSL、Windows、macOS

## 快速开始

Ubuntu 部署可参考 [`docs/ubuntu-deployment.md`](ubuntu-deployment.md)。

### 1. 配置 Broker

创建配置文件 `~/.config/pipemux/broker.toml`：

```toml
[broker]
socket_path = "~/.local/share/pipemux/broker.sock"

[apps.calculator]
command = "dotnet run --project /path/to/PipeMux/samples/Calculator -c Release"
timeout = 30
```

### 2. 启动 Broker

```bash
cd /repos/PieceTreeSharp/PipeMux
dotnet run --project src/PipeMux.Broker -c Release
```

### 3. 使用 CLI

```bash
# RPN 计算器示例
dotnet run --project src/PipeMux.CLI -c Release -- calculator push 10
# Output: Stack: [10]

dotnet run --project src/PipeMux.CLI -c Release -- calculator push 20
# Output: Stack: [10, 20]

dotnet run --project src/PipeMux.CLI -c Release -- calculator add
# Output: Stack: [30]
```

### 4. 管理命令

PipeMux CLI 支持以 `:` 前缀的管理命令，用于查看和管理 Broker 状态：

```bash
# 列出所有已注册的 App（从配置文件加载）
dotnet run --project src/PipeMux.CLI -c Release -- :list
# Output: calculator, myapp, ...

# 列出当前运行中的进程实例
dotnet run --project src/PipeMux.CLI -c Release -- :ps
# Output: calculator:tty:/dev/pts/0 (PID: 12345), ...

# 停止指定 App 的所有实例
dotnet run --project src/PipeMux.CLI -c Release -- :stop calculator
# Output: Stopped 2 instance(s) of calculator

# 注册一个由 PipeMux.Host 托管的 App
dotnet run --project src/PipeMux.CLI -c Release -- :register counter ./samples/HostDemo/bin/Debug/net9.0/HostDemo.dll HostDemo.DebugEntries.BuildCounter --host-path /repos/focus/PipeMux/src/PipeMux.Host/bin/Debug/net9.0/PipeMux.Host
# Output: Registered app 'counter'

# 移除注册（若在运行中可加 --stop）
dotnet run --project src/PipeMux.CLI -c Release -- :unregister counter --stop
# Output: Unregistered app 'counter' (stopped 1 process(es))

# 显示帮助信息
dotnet run --project src/PipeMux.CLI -c Release -- :help
```

**使用别名简化命令**（推荐）：

```bash
# 在 ~/.bashrc 或 ~/.zshrc 中添加
alias pmux='dotnet run --project /path/to/PipeMux/src/PipeMux.CLI -c Release --'

# 然后可以简化为
pmux calculator push 10
pmux :list
pmux :ps
pmux :stop calculator
pmux :register counter ./samples/HostDemo/bin/Debug/net9.0/HostDemo.dll HostDemo.DebugEntries.BuildCounter
pmux :unregister counter --stop
```

`:register` 仅覆盖 `PipeMux.Host` 托管 DLL 入口这一主路径。若需要自定义完整启动命令，请直接编辑 `broker.toml`。

## 开发自己的 PipeMux App

### 使用 PipeMux.Sdk

```csharp
using PipeMux.Sdk;
using System.CommandLine;

// 1. 创建你的服务类
public class MyService
{
    private int _counter = 0;
    
    public string Increment() => $"Counter: {++_counter}";
    public string Get() => $"Counter: {_counter}";
    public string Reset() { _counter = 0; return "Counter reset"; }
}

// 2. 定义命令
var service = new MyService();

var incrementCmd = new Command("increment", "Increment counter");
var getCmd = new Command("get", "Get counter value");
var resetCmd = new Command("reset", "Reset counter");

incrementCmd.SetHandler(() => Console.WriteLine(service.Increment()));
getCmd.SetHandler(() => Console.WriteLine(service.Get()));
resetCmd.SetHandler(() => Console.WriteLine(service.Reset()));

var rootCommand = new RootCommand("My Counter App")
{
    incrementCmd, getCmd, resetCmd
};

// 3. 启动 PipeMux App
var app = new PipeMuxApp("my-counter");
await app.RunAsync(rootCommand);
```

### 关键点

1. **状态在类中维护**：`MyService` 实例在进程生命周期内持久存在
2. **使用 System.CommandLine**：定义命令和参数
3. **调用 `PipeMuxApp.RunAsync()`**：接管 stdin/stdout，处理 JSON-RPC

### 项目文件

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/PipeMux.Sdk/PipeMux.Sdk.csproj" />
  </ItemGroup>
</Project>
```

## 终端标识机制

PipeMux 自动检测终端标识符，实现多终端隔离：

| 环境 | 检测方式 | 标识格式 |
|------|----------|----------|
| VS Code 终端 | `VSCODE_IPC_HOOK_CLI` | `vscode-window:{uuid}` |
| Windows Terminal | `WT_SESSION` | `wt:{guid}` |
| 传统 Windows | `GetConsoleWindow()` | `hwnd:{hwnd}` |
| Linux/macOS | `/proc/self/fd/0` / session id fallback | `tty:/dev/pts/N` |

**手动覆盖**：设置 `PIPEMUX_TERMINAL_ID` 环境变量可覆盖自动检测。

```bash
# 模拟不同终端
PIPEMUX_TERMINAL_ID="session-A" dotnet run --project src/PipeMux.CLI -- calculator push 10
PIPEMUX_TERMINAL_ID="session-B" dotnet run --project src/PipeMux.CLI -- calculator push 99
```

## 运行测试

```bash
cd /repos/PieceTreeSharp/PipeMux

# 端到端测试
./test-e2e.sh

# 终端标识测试
dotnet run --project samples/TerminalIdTest
```

## 项目结构

```
PipeMux/
├── src/
│   ├── PipeMux.Broker/      # Broker 服务器（进程管理 + Named Pipe）
│   ├── PipeMux.CLI/         # CLI 前端
│   ├── PipeMux.Sdk/         # App 开发 SDK
│   └── PipeMux.Shared/      # 共享协议和工具类
├── samples/
│   ├── Calculator/          # RPN 计算器示例
│   └── TerminalIdTest/      # 终端标识测试
├── tools/
│   └── TerminalIdTest/      # 详细终端检测工具
└── docs/
    └── README.md            # 本文档
```

## 技术栈

- **.NET 9.0**
- **StreamJsonRpc** - Microsoft 官方 JSON-RPC 库
- **Nerdbank.Streams** - 流处理工具
- **System.CommandLine** - 命令行解析
- **Tomlyn** - TOML 配置解析

## 常见问题

### Q: Broker 报 "pipe already in use"
A: 可能有残留进程。运行 `pkill -9 -f PipeMux` 清理。

### Q: CLI 报 "Broker not running"
A: 确保 Broker 已启动，且配置文件路径正确。

### Q: 不同终端共享了同一个进程实例
A: 检查终端标识检测是否正常工作。运行 `dotnet run --project samples/TerminalIdTest` 查看检测结果。

---

**更多信息**：参见 [docs/plans/docui-broker-architecture.md](plans/docui-broker-architecture.md)
