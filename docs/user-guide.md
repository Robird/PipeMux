# PipeMux 使用指南

> 已经装好 PipeMux？这份文档让你在 5 分钟内调通第一个 app。
> 还没装？先看 [install.md](install.md)。
> 想写自己的 app？看 [sdk-authoring.md](sdk-authoring.md)。

## 1. 验证安装

```bash
pmux :help
systemctl --user status pipemux-broker
```

`pmux :help` 应能列出管理命令清单与首次设置（First-time setup）引导；`systemctl` 应显示 `active (running)`。

如果 `pmux` 命令找不到，先把 `~/.local/bin` 加进 PATH：

```bash
export PATH="$HOME/.local/bin:$PATH"
```

## 2. 注册并调用第一个 app

PipeMux 自带一个有状态计数器示例 `BuildCounter`（源码位于 `samples/HostDemo`）。把它注册为名为 `counter` 的 app：

```bash
pmux :register counter \
  /absolute/path/to/PipeMux/samples/HostDemo/bin/Debug/net10.0/HostDemo.dll \
  HostDemo.DebugEntries.BuildCounter
```

按官方脚本装好的环境无需指定 `pmux-host` 位置——broker 会自动用安装目录里的版本。如果你把 `pmux-host` 放到了别处，加 `--host-path /absolute/path/to/pmux-host` 即可。

如果你是手写 `broker.toml`，优先写 `PipeMux.Host` 的绝对路径，不要假设 broker 服务启动时一定能从 PATH 找到 `pmux-host`。

调用：

```bash
pmux counter inc      # Counter: 1
pmux counter inc      # Counter: 2
pmux counter get      # Counter: 2
```

状态保留在 `counter` 后台进程里——下次再敲 `pmux counter get`，看到的仍是 `Counter: 2`。

## 3. 日常命令速查

| 想做的事 | 命令 |
|---|---|
| 查看 broker 知道哪些 app | `pmux :list` |
| 查看现在哪些 app 进程在跑 | `pmux :ps` |
| 停掉某个 app 的所有实例 | `pmux :stop <name>` |
| 注销 app（可选同时 `--stop`） | `pmux :unregister <name> --stop` |
| 看完整命令清单 | `pmux :help` |

注意：`pmux :list` 与 `pmux :help` 的输出会内嵌"First-time setup"引导（包含针对当前环境的精确示例），LLM Agent 可直接据此自驱动。

## 4. 多终端隔离

默认情况下，每个终端会话看到一个独立的 app 实例——你在终端 A 累加的 counter 不会影响终端 B。

需要在脚本 / CI / 多终端共享状态时，显式固定终端标识：

```bash
export PIPEMUX_TERMINAL_ID=session-A
pmux counter inc
pmux counter get
```

把 `PIPEMUX_TERMINAL_ID` 设为同一个值的所有调用，会路由到同一个 app 实例。

## 5. 排错速查

| 现象 | 先检查 |
|---|---|
| `pmux` 命令找不到 | `~/.local/bin` 是否在 PATH |
| 调用 app 一直挂起或失败 | `systemctl --user status pipemux-broker` 是否 `active` |
| Broker 起不来 / 不响应 | `journalctl --user -u pipemux-broker -n 100` 看错误 |
| 提示 app 未注册 | `pmux :list` 确认；必要时 `pmux :register ...` |
| 改了 DLL 代码但行为没变 | 后台进程仍在跑旧版本，`pmux :stop <app>` 后下次调用即加载新代码 |
| `:register` 报 "App already registered" | 错误消息已经给了下一步命令，按提示走 |
| 想完全重置 broker 状态 | `systemctl --user restart pipemux-broker` |

## 进一步

- 想写自己的 PipeMux App → [sdk-authoring.md](sdk-authoring.md)
- 想从源码调试 PipeMux 自身 → [developer-guide.md](developer-guide.md)
- 想了解架构 → [architecture.md](architecture.md)
