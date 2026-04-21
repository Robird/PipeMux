# PipeMux 源码开发与调试

这份文档面向“直接在仓库里改代码、调试 sample、排查 Broker/CLI 行为”的场景。这里保留 `dotnet run --project ...` 的做法，但它只代表源码工作流，不代表安装后的日常使用方式。

如果你已经通过 [`../scripts/install-ubuntu-user.sh`](/repos/focus/PipeMux/scripts/install-ubuntu-user.sh) 把当前机器装成稳定环境，并且只是想调用 `pmux`，请改看 [`ubuntu-deployment.md`](ubuntu-deployment.md)。

## 适用场景

- 正在修改 `PipeMux.Broker`、`PipeMux.CLI`、`PipeMux.Sdk` 或 sample 代码
- 想把仓库里的 `samples/Calculator` 直接挂到 Broker 后面调试
- 想验证 `PipeMux.Host` 加载 `HostDemo.dll` 的行为

## 1. 构建源码

```bash
cd /path/to/PipeMux
dotnet build PipeMux.sln --nologo
```

## 2. 准备配置

创建 `~/.config/pipemux/broker.toml`，或参考 [`examples/broker.toml`](examples/broker.toml)：

```toml
[broker]
socket_path = "~/.local/share/pipemux/broker.sock"

[apps.calculator]
command = "dotnet run --project /path/to/PipeMux/samples/Calculator -c Debug"
auto_start = false
timeout = 30
```

Windows 下可改用：

```toml
[broker]
pipe_name = "pipemux-broker"
```

## 3. 启动 Broker

```bash
dotnet run --project src/PipeMux.Broker -c Debug
```

Broker 会读取 `~/.config/pipemux/broker.toml`，并在首次请求某个 app 时按配置启动对应进程。

## 4. 调用 CLI

另开一个终端：

```bash
dotnet run --project src/PipeMux.CLI -c Debug -- :list
dotnet run --project src/PipeMux.CLI -c Debug -- calculator push 10
dotnet run --project src/PipeMux.CLI -c Debug -- calculator push 20
dotnet run --project src/PipeMux.CLI -c Debug -- calculator add
dotnet run --project src/PipeMux.CLI -c Debug -- :ps
```

常见管理命令：

```bash
dotnet run --project src/PipeMux.CLI -c Debug -- :stop calculator
dotnet run --project src/PipeMux.CLI -c Debug -- :help
```

## 5. 调试由 PipeMux.Host 托管的 DLL

如果你要验证 `PipeMux.Host` 的加载路径，可以先确保 solution 已构建，然后注册 `samples/HostDemo` 里的入口：

```bash
dotnet run --project src/PipeMux.CLI -c Debug -- :register \
  counter \
  /path/to/PipeMux/samples/HostDemo/bin/Debug/net10.0/HostDemo.dll \
  HostDemo.DebugEntries.BuildCounter \
  --host-path /path/to/PipeMux/src/PipeMux.Host/bin/Debug/net10.0/PipeMux.Host
```

然后就可以像普通 app 一样调用：

```bash
dotnet run --project src/PipeMux.CLI -c Debug -- counter inc
dotnet run --project src/PipeMux.CLI -c Debug -- counter get
dotnet run --project src/PipeMux.CLI -c Debug -- :unregister counter --stop
```

这条命令链适合调试“Host 是否正确找到 DLL / 入口方法 / 依赖”的问题。安装工作流下通常不需要传 `--host-path`，因为 `pmux-host` 会在 PATH 中。

## 6. 终端隔离调试

想模拟不同会话时，可以显式覆盖终端标识：

```bash
export PIPEMUX_TERMINAL_ID=dev-session-a && dotnet run --project src/PipeMux.CLI -c Debug -- calculator push 10
export PIPEMUX_TERMINAL_ID=dev-session-b && dotnet run --project src/PipeMux.CLI -c Debug -- calculator push 99
```

## 7. 运行测试

```bash
dotnet build PipeMux.sln --nologo
bash tests/test-management-command-parse.sh
bash tests/test-management-commands-e2e.sh
dotnet run --project samples/TerminalIdTest
```

## 进一步阅读

- 已安装日常使用: [`ubuntu-deployment.md`](ubuntu-deployment.md)
- 源码调试配置示例: [`examples/broker.toml`](examples/broker.toml)
- Calculator sample: [`../samples/Calculator/README.md`](/repos/focus/PipeMux/samples/Calculator/README.md)
- 架构说明: [`pipemux-broker-architecture.md`](pipemux-broker-architecture.md)
