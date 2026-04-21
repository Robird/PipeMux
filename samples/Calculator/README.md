# PipeMux Calculator Sample

如果你还没有可用的 `pmux` 命令，先看仓库文档入口：

- 已安装日常使用：[`../../docs/user-guide.md`](../../docs/user-guide.md)
- 安装与 systemd：[`../../docs/install.md`](../../docs/install.md)
- 源码开发 / 调试：[`../../docs/developer-guide.md`](../../docs/developer-guide.md)

这个示例展示的是当前 PipeMux 形态下的“有状态 CLI App”：

- 应用代码使用 `PipeMux.Sdk` 暴露一个 `invoke(string[] args)` RPC 入口
- PipeMux.Broker 负责拉起进程并通过 StreamJsonRpc 转发命令
- 命令本身仍由 `System.CommandLine` 解析
- 计算器状态保存在进程内，因此多次 `pmux calculator ...` 调用之间会持续存在

## 功能

- 栈操作：`push` `pop` `dup` `swap` `clear` `peek`
- 算术操作：`add` `sub` `mul` `div` `neg`
- 输出格式：每次命令执行后打印当前栈状态，例如 `Stack: [10, 20]`

## 示例

```bash
pmux calculator push 10
pmux calculator push 20
pmux calculator add
pmux calculator neg
pmux calculator peek
```

## 本地运行

直接作为普通 .NET 进程运行：

```bash
dotnet run --project samples/Calculator
```

通过 Broker/CLI 运行：

```toml
[apps.calculator]
command = "dotnet run --project /path/to/PipeMux/samples/Calculator -c Debug"
timeout = 30
```

然后：

```bash
pmux calculator push 10
pmux calculator add
```

## 实现说明

- [Program.cs](Program.cs) 里定义了 `RootCommand` 和所有子命令
- `StackCalculator` 是真正的有状态服务对象
- `PipeMuxApp` 接管 stdin/stdout 并提供给 Broker 调用的 RPC 入口

## 备注

这个 README 已不再描述早期“手写 JSON-RPC 请求/响应”的旧实现；当前 sample 基于 `PipeMux.Sdk + StreamJsonRpc + System.CommandLine`。
