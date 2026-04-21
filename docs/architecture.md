# PipeMux 架构说明

> PipeMux 的稳定架构概念：三层结构、通信协议、责任边界。实施进度与变更日志请看仓库根的 [AGENTS.md](../AGENTS.md) 和 git 历史。

## 背景与动机

### 当前痛点

LLM Agent 使用编辑器时的典型模式是 `read_file` + `replace_string_in_file`：

- **上下文堆积**: 每次 read_file 都向上下文注入历史快照，token 浪费严重
- **多处匹配困难**: 当 oldString 匹配多处时容易失败
- **大段替换成本高**: 需要完整精确复述原文，token 和认知负担都高
- **无状态交互**: 每次都是全新开始，无法利用持久化状态（光标位置、选区、undo 栈等）

### 核心洞察

**LLM Agent 需要的不是"文件系统"，而是"有状态的交互式编辑器"**：

- 输入: CLI（Agent 普遍擅长的 `run_in_terminal`）
- 输出: Markdown（直接可注入 LLM 上下文）
- 状态: 持久化进程（光标、选区、装饰、undo 栈）
- 渲染: 实况信息只显示一份，而非历史快照堆积

### 哲学转变

从"CLI 时代"（无状态、一次性执行）到"PipeMux 时代"（有状态、交互式、Markdown 渲染）：

- 传统 TUI: 基于 2D 终端网格（受限于物理终端/显示器）
- PipeMux: 基于 Markdown 文档（适配 LLM 上下文的天然载体）

## 架构设计

### 三层结构

```
┌─────────────────────────────────────────────────────────────┐
│  消费者层                                                     │
├─────────────────────────────────────────────────────────────┤
│  CLI 前端 (PipeMux.CLI)                                       │
│    - 命令: pmux <app> <command> [args...]                    │
│    - 输出: 直接 print Markdown 到 stdout                     │
├─────────────────────────────────────────────────────────────┤
│  Tool Calling 适配（自研 Agent 环境集成）                     │
│    - 工具: pipemux_<app>_<command>(args)                     │
│    - 输出: 直接注入 LLM 上下文                                │
└─────────────────────────────────────────────────────────────┘
                            ▼
┌─────────────────────────────────────────────────────────────┐
│  中转层: PipeMux.Broker                                       │
├─────────────────────────────────────────────────────────────┤
│  职责:                                                        │
│    - 进程管理 (Start / List / Close / Auto-start)            │
│    - 请求路由 (CLI args → RPC call)                          │
│    - 会话管理 (按终端隔离实例)                                 │
│    - 配置驱动 (注册后台应用)                                   │
│                                                               │
│  通信:                                                        │
│    - 前端 ↔ Broker: Named Pipe / Unix Domain Socket          │
│    - Broker ↔ 后台应用: JSON-RPC over stdin/stdout           │
└─────────────────────────────────────────────────────────────┘
                            ▼
┌─────────────────────────────────────────────────────────────┐
│  应用层: 后台 PipeMux 应用                                     │
├─────────────────────────────────────────────────────────────┤
│  PipeMux App (基于 PipeMux.Sdk)                               │
│    - System.CommandLine 解析命令                              │
│    - 状态保留在进程内对象                                      │
│    - 通过 PipeMuxApp 接管 stdin/stdout 与 JSON-RPC           │
│                                                               │
│  通用宿主: PipeMux.Host                                       │
│    - 反射加载任意 DLL 的静态 RootCommand 入口                  │
│    - 让"普通 System.CommandLine 程序"零侵入接入 Broker         │
└─────────────────────────────────────────────────────────────┘
```

### 关键设计决策

#### 1. 通信协议

- **前端 ↔ Broker**：
  - Named Pipes (Windows) / Unix Domain Socket (Linux/Mac)
  - 避免端口占用，基于文件系统权限控制
- **Broker ↔ 后台应用**：
  - JSON-RPC over stdin/stdout（通过 `StreamJsonRpc` 实现）
  - 简单、跨平台、易调试

#### 2. 会话管理

- **按终端自动隔离**：CLI 根据当前终端标识（参见 [`../src/PipeMux.Shared/TerminalIdentifier.cs`](../src/PipeMux.Shared/TerminalIdentifier.cs)）路由到独立 app 实例。
- **显式覆盖**：通过 `PIPEMUX_TERMINAL_ID=<id>` 环境变量固定到指定会话，便于脚本与 CI 场景。

#### 3. 配置驱动

Broker 默认从 `~/.config/pipemux/broker.toml` 读取配置；示例见 [`examples/broker.toml`](examples/broker.toml)。配置的解析与持久化由 `BrokerConfigTomlCodec` 唯一编解码点把守，详见 [AGENTS.md](../AGENTS.md) 的"架构契约"。

## 技术选型

### 开发环境

- **.NET 10.0**：现代 C#、跨平台、高性能

### 依赖库

- **Tomlyn**：TOML 配置解析
- **StreamJsonRpc** + **Nerdbank.Streams**：JSON-RPC 实现，使用 `NewLineDelimitedMessageHandler`
- **System.CommandLine 2.0.6**：CLI 参数解析（前端与 SDK app 共用）

### 跨平台策略

- **Named Pipes**：`System.IO.Pipes.NamedPipeServerStream`（Windows + Linux/Mac）
- **Unix Domain Socket**：`System.Net.Sockets.UnixDomainSocketEndPoint`（.NET 5+）
- 端点解析由 `BrokerConnectionResolver` 对 server 与 client 对称实现，统一支持环境变量覆盖。

## 关键挑战与设计原则

### 1. 进程生命周期管理

后台应用崩溃或 RPC 连接异常时，Broker 通过 `Process.HasExited` 以及 RPC 调用层捕获到的超时 / `ConnectionLostException` 共同把 `AppProcess` 标记为不健康（见 `ProcessRegistry.AppProcess.IsHealthy`），不健康的实例会在下次访问时被剔除并按需重启。`BrokerCoordinator` 是唯一持锁者，线性化"查配置 / 启停进程 / 管理命令"，`ProcessRegistry` 仅维护实例索引。

### 2. 并发控制

多个 CLI 实例可能同时操作同一会话；后台应用内部按需要做会话级锁，Broker 侧不强制串行化业务请求，只串行化注册/启停这类管理操作。

### 3. 错误传播

后台应用错误通过结构化的 `Response.Fail` 与 JSON-RPC error code 传递，CLI 端基于响应直接返回非零退出码（System.CommandLine 2.0+ 的退出码契约）。

### 4. Token 效率

Markdown 渲染粒度由各 app 自行控制（全屏 / 视口 / delta），Broker 不介入展示策略。

## 参考与灵感

- **Language Server Protocol (LSP)**：编辑器 ↔ 语言服务器的 JSON-RPC 通信
- **Debug Adapter Protocol (DAP)**：调试器的标准化协议
- **Jupyter Kernel Protocol**：Notebook ↔ 计算后端
