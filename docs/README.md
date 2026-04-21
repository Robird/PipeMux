# PipeMux 文档

PipeMux 是面向 LLM Agent 的本地进程编排框架。它把长生命周期、有状态的 CLI App 托管在本地 Broker 后面，让前端 CLI 或 Tool Calling 可以反复复用同一应用进程，并按终端隔离状态。

## 我应该看哪一篇？

| 我的身份 / 想做的事 | 去这里 |
|---|---|
| 我已经装好 PipeMux，只想把它用起来 | [user-guide.md](user-guide.md) |
| 我还没装，想先在本机装好 | [install.md](install.md) |
| 我想写一个自己的 PipeMux App | [sdk-authoring.md](sdk-authoring.md) |
| 我要改 PipeMux 自身的源码或调试 sample | [developer-guide.md](developer-guide.md) |
| 我想理解 PipeMux 的内部架构与设计取舍 | [architecture.md](architecture.md) |

不确定从哪开始？大多数"已经在 Ubuntu 上装好、只想稳定调用 app"的场景，先看 [user-guide.md](user-guide.md)。

## PipeMux 在解决什么问题

- Broker 托管有状态 CLI App，让状态保留在后台进程里，而不是丢在单次命令执行里。
- CLI 根据终端标识把请求路由到独立实例，避免多个终端共享同一份会话状态。
- Broker 与 CLI 统一支持 Unix Domain Socket / Named Pipe，连接方式可通过配置或环境变量覆盖。

## 仓库其它入口

- 配置示例：[examples/broker.toml](examples/broker.toml)
- 演进中的提案：[rfc/](rfc/)
- Sample app 实现：[../samples/Calculator/README.md](../samples/Calculator/README.md)
- 开发者贡献规约与架构契约：[../AGENTS.md](../AGENTS.md)
