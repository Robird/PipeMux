# PipeMux 快速开始

当前仓库里的主线是：

- `PipeMux.Broker` 负责进程管理与路由
- `PipeMux.CLI` 负责把用户命令转成请求
- `PipeMux.Sdk` 让普通 `System.CommandLine` 应用变成 PipeMux app
- `PipeMux.Host` 允许动态加载 DLL 中的入口方法

## 1. 构建

```bash
cd /repos/focus/PipeMux
dotnet build
```

## 2. 准备配置

创建 `~/.config/pipemux/broker.toml`：

```toml
[broker]
socket_path = "~/.local/share/pipemux/broker.sock"

[apps.calculator]
command = "dotnet run --project /repos/focus/PipeMux/samples/Calculator"
timeout = 30
```

Windows 下可改用：

```toml
[broker]
pipe_name = "pipemux-broker"
```

## 3. 启动 Broker

```bash
dotnet run --project src/PipeMux.Broker
```

## 4. 调用 CLI

另开一个终端：

```bash
dotnet run --project src/PipeMux.CLI -- :list
dotnet run --project src/PipeMux.CLI -- calculator push 10
dotnet run --project src/PipeMux.CLI -- calculator push 20
dotnet run --project src/PipeMux.CLI -- calculator add
```

## 5. 管理命令

```bash
dotnet run --project src/PipeMux.CLI -- :ps
dotnet run --project src/PipeMux.CLI -- :stop calculator
dotnet run --project src/PipeMux.CLI -- :help
```

## 进一步阅读

- Ubuntu 部署: [`ubuntu-deployment.md`](ubuntu-deployment.md)
- 架构说明: [`pipemux-broker-architecture.md`](pipemux-broker-architecture.md)
- 示例配置: [`examples/broker.toml`](examples/broker.toml)
