# 安装 PipeMux（Ubuntu）

这份文档把 PipeMux 装到一台 Ubuntu 机器上，作为长期使用的稳定环境（命令入口为 `pmux` / `pmux-host`）。

- 装好之后怎么用 → [user-guide.md](user-guide.md)
- 想从源码调试 PipeMux 自身 → [developer-guide.md](developer-guide.md)

本文涉及当前仓库里的三个 .NET CLI/服务：

- `src/PipeMux.CLI`
- `src/PipeMux.Host`
- `src/PipeMux.Broker`

## 推荐方式

对于“本机日常使用、反复更新”这个场景，推荐直接使用仓库自带脚本：

```bash
./scripts/install-ubuntu-user.sh
```

这个脚本会：

- 发布 `PipeMux.Broker`、`PipeMux.CLI`、`PipeMux.Host`
- 安装到 `~/.local/share/pipemux/`
- 创建快捷命令 `~/.local/bin/pmux` 与 `~/.local/bin/pmux-host`
- 安装 user-level `systemd` 服务 `~/.config/systemd/user/pipemux-broker.service`
- 首次安装时自动生成 `~/.config/pipemux/broker.toml`

之后你每次修改代码，只要再次执行同一个脚本，就会更新本机安装的二进制文件并重启 Broker 服务。

## 安装后的默认目录布局

```text
~/.local/share/pipemux/
└── bin/
    ├── broker/
    │   └── PipeMux.Broker
    ├── cli/
    │   └── PipeMux.CLI
    └── host/
        └── PipeMux.Host
```

## Broker 配置

Broker 默认从 `~/.config/pipemux/broker.toml` 读配置；CLI 也从同一份文件解析连接方式。Ubuntu 上推荐使用 `socket_path`：

```toml
[broker]
socket_path = "~/.local/share/pipemux/broker.sock"

[apps.calculator]
command = "/path/to/Samples.Calculator"
auto_start = false
timeout = 30

[apps.counter]
command = "/absolute/path/to/PipeMux.Host /path/to/HostDemo.dll HostDemo.DebugEntries.BuildCounter"
auto_start = false
timeout = 30
```

对 `broker.toml` 里的 `command`，推荐直接写 `PipeMux.Host` 的绝对路径。原因是 broker 常常由 `systemd --user` 拉起，它继承到的 PATH 可能不包含 `~/.local/bin`；而 `pmux :register` 会自动解析并保存一个 broker 实际可执行到的绝对路径。

如需临时覆盖连接方式，可设置环境变量：

- `PIPEMUX_SOCKET_PATH` —— 覆盖 Unix Domain Socket 路径
- `PIPEMUX_PIPE_NAME` —— 覆盖 named pipe 名称（`DOCUI_PIPE_NAME` 仅作兼容保留）

## User-level systemd 服务

安装脚本默认已经 `daemon-reload` + `enable --now` 这个服务，下面的命令通常只在排查或手工管理时才需要。脚本会把单元文件复制到 `~/.config/systemd/user/pipemux-broker.service`：

```ini
[Unit]
Description=PipeMux Broker
After=default.target

[Service]
Type=simple
WorkingDirectory=%h/.local/share/pipemux
Environment=HOME=%h
ExecStart=%h/.local/share/pipemux/bin/broker/PipeMux.Broker
Restart=always
RestartSec=2
KillMode=mixed

[Install]
WantedBy=default.target
```

手工启用方式：

```bash
systemctl --user daemon-reload
systemctl --user enable --now pipemux-broker
systemctl --user status pipemux-broker
```

查看日志：

```bash
journalctl --user -u pipemux-broker -f
```

## 部署 CLI 命令

安装脚本会生成 `~/.local/bin/pmux` 包装命令。之后就能直接调用：

```bash
pmux :list
pmux :ps
pmux calculator push 10
```

如果 `~/.local/bin` 还没进 PATH：

```bash
export PATH="$HOME/.local/bin:$PATH"
```

## 部署 Host 命令

`PipeMux.Host` 的用途更像通用宿主，不建议单独注册成 `systemd` 常驻服务。常见做法是：

1. 把 `PipeMux.Host` 安装到固定位置，如 `~/.local/share/pipemux/bin/host/PipeMux.Host`
2. 把目标 DLL 放到你自己的应用目录
3. 在 `broker.toml` 里把某个 app 的 `command` 指向 `PipeMux.Host + 目标 DLL + 入口方法`

例如：

```toml
[apps.greeter]
command = "/absolute/path/to/PipeMux.Host /opt/myapps/HostDemo.dll HostDemo.DebugEntries.BuildGreeter"
auto_start = false
timeout = 30
```

然后就能通过 CLI 访问：

```bash
pmux greeter hello Alice
pmux greeter history
```

## 验证建议

部署完成后，先做这几步：

```bash
pmux :list
pmux :ps
pmux calculator push 10
pmux calculator push 20
pmux counter inc
pmux counter get
```

如果你是在脚本、CI 或其他非交互环境里调用 CLI，建议显式指定终端标识，避免每次命中不同会话：

```bash
export PIPEMUX_TERMINAL_ID=deploy-demo
pmux counter inc
pmux counter get
```

## 附录：发布形态与注意事项（一般用户可跳过）

下面是给"想自己控制发布产物 / 打包 / 跨机器分发"的维护者看的。日常使用 `install-ubuntu-user.sh` 的用户不需要关心。

### 两种发布方式

**方式 1：依赖目标机已安装 .NET 10 Runtime**。优点是产物更小，推荐开发机和服务器都能稳定安装 .NET 的场景：

```bash
dotnet publish src/PipeMux.CLI/PipeMux.CLI.csproj -c Release -r linux-x64 --self-contained false -o ./artifacts/cli
dotnet publish src/PipeMux.Host/PipeMux.Host.csproj -c Release -r linux-x64 --self-contained false -o ./artifacts/host
dotnet publish src/PipeMux.Broker/PipeMux.Broker.csproj -c Release -r linux-x64 --self-contained false -o ./artifacts/broker
```

**方式 2：自包含部署**。优点是服务器不用额外安装 .NET Runtime，适合更独立的交付：

```bash
dotnet publish src/PipeMux.CLI/PipeMux.CLI.csproj -c Release -r linux-x64 --self-contained true -o ./artifacts/cli-sc
dotnet publish src/PipeMux.Host/PipeMux.Host.csproj -c Release -r linux-x64 --self-contained true -o ./artifacts/host-sc
dotnet publish src/PipeMux.Broker/PipeMux.Broker.csproj -c Release -r linux-x64 --self-contained true -o ./artifacts/broker-sc
```

两种方式发布后目录里都会直接出现可执行文件 `PipeMux.CLI` / `PipeMux.Host` / `PipeMux.Broker`，在 Ubuntu 上可直接执行，不用再写成 `dotnet xxx.dll`。

### 已验证的发布形态

在当前 Ubuntu 环境里实际验证过以下组合可启动：

- `PipeMux.Broker` 发布产物可直接运行
- `PipeMux.CLI` 发布产物可直接连接 Broker
- `PipeMux.Host` 可被 Broker 拉起并成功加载 `samples/HostDemo/HostDemo.dll`
- `socket_path = "~/.local/share/pipemux/broker.sock"` 的 Unix socket 路径已实际打通

### 注意事项

1. user-level `systemd` 服务更适合"本机日常使用"；如果你要做系统级常驻服务，再单独改成 `/etc/systemd/system/` 版本会更合适。
2. 若希望用户退出登录后仍保持 user service 存活，可按需执行 `loginctl enable-linger $USER`。
