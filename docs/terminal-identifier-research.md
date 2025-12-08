# 跨平台终端标识方案调研报告

## 摘要

本文档调研了在 PipeMux 框架中实现"TTY 隔离"功能所需的跨平台终端标识方案。目标是找到一个标识符，使得：
- 同一终端窗口内多次执行 CLI 命令返回相同标识符
- 不同终端窗口返回不同标识符

## 技术方案分析

### Linux/macOS 方案

#### 1. TTY 设备路径 (`/dev/pts/N`) ⭐ 推荐

**获取方式：**
- `readlink /proc/self/fd/0` (Linux)
- `ttyname(0)` 系统调用 (POSIX)

**优点：**
- ✅ 每个终端窗口分配独立的 PTY 设备
- ✅ 同一终端多次执行命令返回相同路径
- ✅ 简单可靠，无需复杂的 P/Invoke

**缺点：**
- ⚠️ macOS 没有 `/proc`，需要使用 `ttyname()` 或运行 `tty` 命令
- ⚠️ Docker 容器内 PTY 可能有命名空间问题
- ⚠️ 如果 stdin 被重定向，返回的不是 TTY

**实测结果：**
```
Terminal 1: /dev/pts/5
Terminal 2: /dev/pts/6
同一终端多次调用: 始终返回 /dev/pts/5
```

#### 2. Session ID (`getsid(0)`)

**优点：**
- ✅ 每个终端会话有唯一的 Session ID
- ✅ 子进程继承父进程的 SID

**缺点：**
- ⚠️ 需要 P/Invoke 调用 libc
- ⚠️ tmux/screen 中每个窗格是独立的 session

#### 3. 环境变量 (`$TTY`, `$SSH_TTY`)

**评估：** 不推荐
- `$TTY` 是非标准的，很多 shell 不设置
- `$SSH_TTY` 仅在 SSH 场景下可用

### Windows 方案

#### 1. `WT_SESSION` 环境变量 ⭐ Windows Terminal 推荐

**优点：**
- ✅ Windows Terminal 为每个标签页设置唯一 GUID
- ✅ 同一标签页多次调用返回相同值
- ✅ 无需 P/Invoke

**缺点：**
- ⚠️ 仅 Windows Terminal 可用
- ⚠️ 传统 cmd.exe/PowerShell 独立窗口无此变量

#### 2. `GetConsoleWindow()` API

**优点：**
- ✅ 传统控制台返回有效 HWND
- ✅ 标准 Win32 API

**缺点：**
- ❌ **Windows Terminal 返回 IntPtr.Zero**（这是关键问题！）
- Windows Terminal 使用现代架构，不依赖传统 console window

#### 3. 父进程追溯

追溯父进程直到找到 `conhost.exe` 或 `WindowsTerminal.exe` 的 PID。

**评估：** 作为最后的 fallback 方案

## 边界情况分析

| 场景 | Linux 方案结果 | Windows 方案结果 |
|------|---------------|-----------------|
| **标准终端** | ✅ `/dev/pts/N` | ✅ `WT_SESSION` 或 `HWND` |
| **SSH 连接** | ✅ 分配独立 PTY | N/A |
| **tmux/screen** | ✅ 每个窗格独立 PTY | N/A |
| **VS Code 终端** | ✅ `/dev/pts/N` | ✅ 可用 `TERM_PROGRAM` |
| **Windows Terminal 多标签** | N/A | ✅ `WT_SESSION` 区分 |
| **传统 cmd.exe** | N/A | ⚠️ 回退到 `GetConsoleWindow()` |
| **stdin 重定向** | ⚠️ 返回 null | ⚠️ 返回 null |
| **Docker 容器** | ⚠️ 可能需要特殊处理 | N/A |

## 推荐实现策略

```
Linux/macOS 优先级:
  1. readlink("/proc/self/fd/0") → /dev/pts/N
  2. ttyname(0) via P/Invoke
  3. getsid(0) 作为 fallback

Windows 优先级:
  1. $env:WT_SESSION (Windows Terminal)
  2. $env:TERM_PROGRAM + TTY (VS Code)
  3. GetConsoleWindow() HWND (传统控制台)
  4. 父进程追溯 (最后手段)
```

## 实现代码

见 `src/PipeMux.Shared/TerminalIdentifier.cs`

### 使用示例

```csharp
using PipeMux.Shared;

// 获取终端标识符
var terminalId = TerminalIdentifier.GetTerminalId();
// 返回值示例:
// Linux: "tty:/dev/pts/5"
// Windows Terminal: "wt:12345678-1234-1234-1234-123456789abc"
// 传统控制台: "hwnd:1A2B3C4D"

// 如果无法识别，使用进程 ID 作为 fallback
var terminalIdOrFallback = TerminalIdentifier.GetTerminalIdOrFallback();
// 返回值示例: "process-12345" (当无法识别终端时)

// 解析标识符类型
var info = TerminalIdInfo.Parse(terminalId);
Console.WriteLine($"Type: {info.Type}");   // "tty", "wt", "hwnd", etc.
Console.WriteLine($"Value: {info.Value}"); // "/dev/pts/5", GUID, etc.
```

## 测试验证

```bash
# 运行测试程序
cd PipeMux/samples/TerminalIdTest
dotnet run

# 验证多次调用返回相同值
for i in {1..5}; do dotnet run --no-build | grep "Terminal ID:"; done
```

## 结论

1. **Linux/macOS**: 使用 `/proc/self/fd/0` 读取 TTY 设备路径是最简单可靠的方案
2. **Windows**: 需要分层策略，优先使用 `WT_SESSION`，回退到 `GetConsoleWindow()`
3. **跨平台**: 已实现 `TerminalIdentifier` 类，封装了所有平台特定逻辑

## 后续工作

- [ ] 集成到 PipeMux.CLI 的路由逻辑中
- [ ] 添加 Docker 容器内的特殊处理
- [ ] 考虑持久化终端 ID 到文件系统（用于跨进程共享）
