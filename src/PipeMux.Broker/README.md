# PipeMux Broker

`PipeMux.Broker` 是当前系统的中转层，负责：

- 读取 `broker.toml`
- 按 app 名称查找并拉起后台进程
- 通过 Unix Domain Socket 或 Named Pipe 接收 CLI 请求
- 通过 StreamJsonRpc 调用后台应用的 `invoke` 方法
- 按终端标识隔离同一 app 的不同进程实例

## 当前结构

```text
src/
├── PipeMux.Shared/   # 共享协议、路径/端点解析、终端标识
├── PipeMux.Broker/   # Broker 服务本体
├── PipeMux.CLI/      # 前端命令入口
├── PipeMux.Sdk/      # App SDK
└── PipeMux.Host/     # 动态加载 DLL 入口的通用宿主
```

## 当前状态

当前实现已经具备：

- Broker/CLI 端到端通信
- Unix socket 与 named pipe 双传输
- 后台进程复用、超时与健康检查
- 管理命令 `:list` `:ps` `:stop` `:register` `:unregister` `:help`
- `PipeMux.Host` 动态加载 DLL 中的 `RootCommand` 入口

## 相关文档

- 使用指南：[`../../docs/user-guide.md`](../../docs/user-guide.md)
- 源码开发：[`../../docs/developer-guide.md`](../../docs/developer-guide.md)
- 安装与 systemd：[`../../docs/install.md`](../../docs/install.md)
- 架构说明：[`../../docs/architecture.md`](../../docs/architecture.md)
