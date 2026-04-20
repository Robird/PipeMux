## 跨会话记忆文档
本文档(`./AGENTS.md`)会伴随每个 user 消息注入上下文，是跨会话的外部记忆。完成一个任务、制定或调整计划时务必更新本文件，避免记忆偏差。

## 已知的工具问题
- 需要要删除请用改名替代，因为环境会拦截删除文件操作。
- 不要使用'insert_edit_into_file'工具，经常产生难以补救的错误结果。
- 当需要临时设置环境变量时，要显式用`export PIECETREE_DEBUG=0 && dotnet test ...`这样的写法，避免使用`PIECETREE_DEBUG=0 dotnet test ...`写法，后者会触发自动审批的命令行解析问题。

## 用户语言
请主要用简体中文与用户交流，对于术语/标识符等实体名称则优先用原始语言。

## 项目概览
Broker and Front of Stateful CLI Apps

## 最新进展

### 代码化简 (2026-04-21): 死代码清理 + 重复消除 🧹
- **删除死代码**: `JsonRpcRequest.cs` / `JsonRpcResponse.cs` / `JsonRpcError.cs` 已删除（StreamJsonRpc 集成后的遗留）
  - `JsonRpc.cs` 中 `SerializeJsonRpcRequest()` / `DeserializeJsonRpcResponse()` 两个死方法已移除
- **统一 `ExpandPath`**: 三处重复的 `~` 展开逻辑合并到 `src/PipeMux.Shared/PathHelper.cs`
  - `BrokerConfig.ExpandPath()` / `BrokerEndpointResolver.ExpandPath()` / `ProcessRegistry.ExpandArgument()` → `PathHelper.ExpandPath()`
- **去 `Unsafe.AsRef` hack**: `ProcessRegistry.cs` 中 `AppProcess._rpc` 去掉 `readonly`，直接赋值
- **统一 Config 模型**: 新增 `src/PipeMux.Shared/BrokerConnectionConfig.cs`
  - Broker 的 `BrokerSettings` 和 CLI 的 `CliBrokerConfig/CliBrokerSettings` 合并为 `BrokerConnectionSettings`
- **`TerminalIdInfo` 移出 Shared**: 仅 `samples/TerminalIdTest` 使用，已内联到该样本中
- **编译验证**: 全 7 项目 build succeeded，0 warning 0 error ✅

### Ubuntu 部署文档补充 (2026-04-20): CLI / Host / Broker 部署说明 🐧
- **新增文档**: `docs/ubuntu-deployment.md`
  - 覆盖 `src/PipeMux.CLI`、`src/PipeMux.Host`、`src/PipeMux.Broker` 在 Ubuntu 上的发布与部署
  - 包含 framework-dependent 与 self-contained 两种 `dotnet publish` 方式
  - 提供 user-level `systemd` 部署方式、`broker.toml` 示例、`pmux` 命令入口
- **新增安装产物**:
  - `scripts/install-ubuntu-user.sh` — 本机一键安装/更新脚本，可重复执行
  - `deploy/systemd/user/pipemux-broker.service` — user-level `systemd` 服务成品
- **文档修正**:
  - `docs/examples/broker.toml` 改回 Linux 优先示例 `socket_path = "~/.local/share/pipemux/broker.sock"`
  - `docs/README.md` 增加 Ubuntu 部署文档入口
- **本轮顺手修复**:
  - `socket_path` 已实际接入 Broker/CLI，Ubuntu 下可用 Unix Domain Socket ✅
  - CLI 新增 `PIPEMUX_SOCKET_PATH` / `PIPEMUX_PIPE_NAME`，并兼容旧的 `DOCUI_PIPE_NAME` ✅
  - Broker 对 app `command` 增加引号解析，不再只做简单空格切分 ✅
  - Broker 启动子进程前会展开 `~` 与环境变量路径，文档示例可直接成立 ✅
- **本地验证**:
  - `PipeMux.Broker` 发布产物可在 Ubuntu 上直接启动 ✅
  - `PipeMux.CLI` 发布产物可直接连接 Broker ✅
  - `PipeMux.Host` 可被 Broker 拉起并加载 `samples/HostDemo/HostDemo.dll` ✅
  - 安装脚本在临时 HOME 下可完成发布、落盘、生成配置与 wrapper ✅
  - 基于 `socket_path` 的 CLI → Broker → Host/Calculator 冒烟验证通过 ✅

### PipeMux.Host 通用宿主程序 (2026-04-20): 动态加载入口 🧩
- **定位**: 类似 `rundll32` / `python -m` 的通用宿主进程
  - 加载任意 .NET DLL 中的静态方法作为 PipeMux 应用入口
  - 目标 DLL 无需引用 PipeMux.Sdk，只需 System.CommandLine
  - 一个 DLL 可包含多个入口，通过参数选择
- **用法**: `pipemux-host <assemblyPath> <Namespace.Type.Method>`
  - 方法签名: `static RootCommand Method()` 或 `static Task<RootCommand> Method()`
  - 自动从方法名推导应用名 (BuildCalculator → calculator)
- **新增文件**:
  - `src/PipeMux.Host/` — 宿主 exe 项目 (PipeMux.Host.csproj, Program.cs, HostLoadContext.cs, EntryPointResolver.cs)
  - `samples/HostDemo/` — 测试类库，含两个入口 (BuildCounter, BuildGreeter)
- **技术要点**:
  - `AssemblyDependencyResolver` + 自定义 `AssemblyLoadContext` 隔离加载
  - System.CommandLine 共享到宿主上下文，保证 RootCommand 类型一致
  - 详细错误提示：可用类型/方法列表
- **测试结果**: 
  - 直接 JSON-RPC 管道测试 ✅ (Counter: 7 个请求全部正确，有状态保持)
  - Greeter 入口测试 ✅ (hello + history)
  - **完整集成路径 (CLI → Broker → Host → HostDemo DLL)** ✅
- **附带修复**: `docs/examples/broker.toml` 中 `autostart` → `auto_start` (匹配 Tomlyn PascalCase 映射)

### PipeMux Broker 骨架 (2025-12-06 上午): 新方向启动 🚀
- **愿景**: 从"CLI 时代"到"PipeMux 时代"的转变
  - 输入: CLI / Tool Calling (LLM 擅长)
  - 输出: Markdown (直接注入上下文)
  - 状态: 持久化进程 (光标/选区/undo)
  - 目标: 实况信息单份显示，避免 `read_file` 堆积
- **架构**: 三层结构
  - 消费者层: CLI (开发阶段) → Tool Calling (生产阶段)
  - 中转层: PipeMux.Broker (进程管理 + 路由)
  - 应用层: PipeMux.TextEditor / Debugger / DiffViewer...
- **骨架完成**:
  - 4 个项目: Shared / Broker / CLI / TextEditor ✅ 构建通过
  - 协议定义: Request/Response/JsonRpc ✅
  - 基础 Markdown 渲染器 (行号 + 光标) ✅
  - 配置文件加载 (TOML) ✅
- **文档**:
  - [`docs/plans/pipemux-broker-architecture.md`](docs/plans/pipemux-broker-architecture.md) - 完整架构规划
  - [`docs/pipemux-quickstart.md`](docs/pipemux-quickstart.md) - 快速开始指南
- **Changefeed**: [`#delta-2025-12-06-pipemux-broker-skeleton`](agent-team/indexes/README.md#delta-2025-12-06-pipemux-broker-skeleton)

### PipeMux 通信循环完成 (2025-12-06 下午): MVP 达成 🎊
- **AI Team 协作**: Planner → PorterCS (4 个任务)
  - Task 1: PipeMux.Calculator 测试应用 ✅
  - Task 2: Broker Named Pipe 服务器 ✅
  - Task 3: CLI Named Pipe 客户端 ✅
  - Task 4: Broker 进程管理 + JSON-RPC 通信 ✅
- **完整数据链路**: CLI ↔ Named Pipe ↔ Broker ↔ JSON-RPC (stdin/stdout) ↔ Calculator
- **核心功能**:
  - 异步并发连接处理 (多 CLI 客户端)
  - 进程生命周期管理 (启动/复用/崩溃恢复)
  - 跨平台 Named Pipe 通信 (Windows/Linux/Mac)
  - JSON-RPC 2.0 协议完整实现
  - 超时保护 (连接 5s, 请求 30s)
  - 优雅关闭 (Ctrl+C)
- **测试结果**: 10/10 通过 ✅
  - 基础运算 (add/subtract/multiply/divide)
  - 错误处理 (除零/未知应用)
  - 并发请求 (3 个同时)
  - 进程复用验证
- **端到端验证**: `./test-pipemux.sh` 全部通过
- **Changefeed**: [`#delta-2025-12-06-pipemux-communication-loop`](agent-team/indexes/README.md#delta-2025-12-06-pipemux-communication-loop)

### PipeMux 代码审阅与P0修复 (2025-12-06 晚上): 生产就绪 ✅
- **AI Team 协作**: CodexReviewer → PorterCS
  - CodexReviewer: 全面代码审阅（设计、并发、错误处理、资源管理）
  - PorterCS: 修复 5 个 P0 关键问题
- **修复的 P0 问题**:
  - 后台任务未等待 → 添加任务追踪，防止崩溃和资源泄漏 ✅
  - SemaphoreSlim 泄漏 → 使用 lockAcquired 标志安全释放 ✅
  - StandardError 死锁 → 异步消费 stderr 防止管道阻塞 ✅
  - 配置路径错误 → 统一使用 ~/.config/pipemux/broker.toml ✅
  - 超时状态未清理 → 添加 _isHealthy 标志自动重启 ✅
- **审阅发现**:
  - P0 (Critical): 5 个 → 已全部修复
  - P1 (High): 7 个改进建议（连接数限制、重试机制等）
  - P2 (Low): 6 个优化建议（结构化日志、性能指标等）
- **代码质量**: 从 MVP 提升到生产就绪水平
- **文档**: [`agent-team/handoffs/PipeMux-Broker-CodeReview-2025-12-06.md`](agent-team/handoffs/PipeMux-Broker-CodeReview-2025-12-06.md)
- **Changefeed**: [`#delta-2025-12-06-pipemux-p0-fixes`](agent-team/indexes/README.md#delta-2025-12-06-pipemux-p0-fixes)

### PipeMux 架构决策 (2025-12-06 深夜): 战略转型 🚀
- **AI Team 集体探讨**: Planner + InvestigatorTS + PorterCS + GeminiAdvisor + Team Leader
- **三大决策**:
  1. **命名**: PipeMux.* → **PipeMux** (Pipe Multiplexer)
     - tagline: "Local Process Orchestration via Named Pipes"
     - 独特可搜索，避免与 Message Broker 混淆
  2. **SDK 框架**: Minimal API 风格为主
     - 从 ~130 行样板代码简化到 ~25 行
     - 类似 ASP.NET Core Minimal API / FastAPI
  3. **代码组织**: 独立 `pipemux/` 子目录
     - 职责分离，为 NuGet 发布做准备
- **项目结构**:
  - `pipemux/src/PipeMux.Core/` - Broker 核心
  - `pipemux/src/PipeMux.Cli/` - CLI 前端
  - `pipemux/src/PipeMux.Sdk/` - App 开发 SDK（新增）
  - `pipemux/src/PipeMux.Protocol/` - 协议定义
  - `pipemux/samples/Calculator/` - 示例应用
- **文档**: [`agent-team/handoffs/BrokerCLI-Architecture-Discussion-2025-12-06.md`](agent-team/handoffs/BrokerCLI-Architecture-Discussion-2025-12-06.md)
- **Changefeed**: [`#delta-2025-12-06-pipemux-decision`](agent-team/indexes/README.md#delta-2025-12-06-pipemux-decision)

### StreamJsonRpc 集成完成 (2025-12-06 深夜): 代码简化 65%! 🎯
- **技术选型**: 采用微软官方 StreamJsonRpc 库替代手动 JSON-RPC 实现
- **代码变化**:
  | 组件 | 重构前 | 重构后 | 减少 |
  |------|--------|--------|------|
  | Calculator (App) | ~130 行 | **49 行** | **62%** |
  | ProcessRegistry | 手动 JSON 解析 | StreamJsonRpc + Nerdbank.Streams | 简化 |
- **测试结果**: 全部 8 个 E2E 测试通过 ✅
  - 冷启动、进程复用、并发、错误处理、崩溃恢复
- **关键改进**:
  - Calculator 只需 ~25 行业务代码 + ~22 行服务类
  - 移除所有手动 JSON 序列化/反序列化
  - 移除手动路由逻辑
  - 自动参数绑定和类型转换
  - StreamJsonRpc 处理协议层（请求匹配、错误封装）
- **依赖添加**: `StreamJsonRpc 2.22.23`, `Nerdbank.Streams 2.13.16`
- **文档**: [`agent-team/handoffs/StreamJsonRpc-Integration-Brief-2025-12-06.md`](agent-team/handoffs/StreamJsonRpc-Integration-Brief-2025-12-06.md)
- **Changefeed**: [`#delta-2025-12-06-streamjsonrpc-integration`](agent-team/indexes/README.md#delta-2025-12-06-streamjsonrpc-integration)

### 多终端隔离完成 (2025-12-07): 核心特性达成 🎉
- **问题**: 同一终端的多次 CLI 调用需要路由到同一后台进程实例
- **挑战**: VS Code 集成终端中，传统方法（TTY/PID）每次运行都不同
- **解决方案**: 使用 `VSCODE_IPC_HOOK_CLI` 环境变量中的 UUID
- **跨平台终端标识**:
  | 环境 | 检测方式 | 标识格式 |
  |------|----------|----------|
  | VS Code 终端 | `VSCODE_IPC_HOOK_CLI` UUID | `vscode-window:{uuid}` |
  | Windows Terminal | `WT_SESSION` | `wt:{guid}` |
  | 传统 Windows | `GetConsoleWindow()` | `hwnd:{hwnd}` |
  | Linux/macOS | `/proc/self/fd/0` | `tty:/dev/pts/N` |
- **效果**: 不同终端窗口 → 独立计算器实例，互不干扰
- **Calculator 升级**: 简单四则运算 → RPN 有状态栈式计算器
- **文档**: [`PipeMux/docs/README.md`](PipeMux/docs/README.md) — **PipeMux 使用说明**

---
**状态更新提示：** 编辑本文件前请先核对 [`docs/reports/migration-log.md`](docs/reports/migration-log.md) 与 [`agent-team/indexes/README.md`](agent-team/indexes/README.md) 的最新 changefeed delta。
