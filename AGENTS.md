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

### 第二轮化简落地 (2026-04-21): 端点对称 + 锁集中 + 去冷启动 sleep + 测试提速 ✅
- **背景**: 上一轮代码审阅后续修复完成后再做一轮“小而稳”的化简，按收益排序合并实施
- **修复 / 化简**:
  - **S5 端点解析对称化**: `BrokerConnectionResolver.ResolveServerEndpoint` 现在也优先读 `PIPEMUX_SOCKET_PATH` / `PIPEMUX_PIPE_NAME`，与 `ResolveClientEndpoint` 完全对称；不必改 toml 即可注入路径，同时绕开 apphost 在隔离 HOME 下 `LocalApplicationData` 解析为空的怪行为
  - **S4 测试脚本改用 binary**: `tests/test-management-commands-e2e.sh` 不再走 `dotnet run`，broker/cli 都直接执行构建产物，脚本运行时间从约 1 分钟降到约 15 秒；BROKER_PID 即 broker 自身，cleanup 不再需要 `pkill -P` 兜底
  - **S3 去掉冷启动 sleep**: `BrokerServer.HandleRequestAsync` 删掉 `await Task.Delay(100)`，冷启动等待交由 StreamJsonRpc + `settings.Timeout` 兜底；每次首请求节省 100 ms
  - **S1 锁集中到 Coordinator**: `BrokerConfigStore` 不再自带 `_brokerGate`，所有锁集中由 `BrokerCoordinator` 持有；`Store` 退化为纯 “内存视图 + 原子落盘”，对外暴露 `IReadOnlyDictionary<string, AppSettings> Apps`，`Coordinator` 在 gate 内访问；顺手删除未使用的 `BrokerConfigStore.TryGetApp`
- **验证**:
  - `dotnet build PipeMux.sln` succeeded ✅ (0 warning, 0 error)
  - `bash tests/test-management-commands-e2e.sh` passed，约 15s，零进程/临时目录残留 ✅
- **未做的事**:
  - `ProcessAcquisitionResult` / `BrokerOperationResult` 改 record struct 的“美化型”优化本轮未做；当前形态已可读，无并发/正确性收益
  - `ConfigLoader` 与 `BrokerConfigStore` 共享 TOML codec 暂未提取，等下次新增 codec 行为再合并

### 代码审阅后续修复 (2026-04-21): 测试清理 + 解析鲁棒性 + 错误回收语义 ✅
- **背景**: 上一轮“管理命令后续化简”落地后审阅发现 4 个问题，本轮逐一修复
- **修复**:
  - `tests/test-management-commands-e2e.sh` 新增 `trap cleanup EXIT INT TERM`，且 cleanup 内 `pkill -P $BROKER_PID` 先回收 `dotnet run` 包装进程的 broker 子进程，再 kill 包装进程本身，杜绝失败/异常路径的进程与临时目录泄漏
  - `src/PipeMux.Shared/Protocol/ManagementCommand.cs` 重写参数解析为 “两遍法”：先抽走声明的 `--name value` 与 `--flag`，剩余 token 才作为位置参数；任何未声明的 `--xxx` 直接判失败而非无声吞掉。`:register --host-path /x app dll method` 与 `:unregister --stop app` 任意顺序均能正确解析
  - `src/PipeMux.Broker/BrokerCoordinator.cs` 新增 `CloseProcess(string processKey)`，用于按 process key 精确关闭单个进程；`BrokerServer.HandleRequestAsync` 失败回收路径改用它，不再借用面向 app 的 `StopApp` 做前缀匹配
  - 顺手简化 `BrokerCoordinator.SnapshotActiveProcesses` 的 `Select+Where+Select!` 三段式，并补齐 `BrokerCoordinator.cs` / 测试脚本的文件末换行
- **未做的事 (有意取舍)**:
  - apphost 在隔离 HOME 下 `Environment.GetFolderPath(LocalApplicationData)` 返回空（导致直接启动 broker binary 时 socket 路径退化为 `pipemux/broker.sock`）的 .NET 怪行为，本轮选择继续走 `dotnet run`，未引入 broker 端 `PIPEMUX_SOCKET_PATH` 支持，避免范围蔓延
- **验证**:
  - `dotnet build PipeMux.sln` succeeded ✅ (0 warning, 0 error)
  - `bash tests/test-management-commands-e2e.sh` passed，运行结束后 `/tmp/pipemux-mgmt-e2e-*` 与 broker 子进程均无残留 ✅

### CLI 失败退出码修正 (2026-04-21): 使用 InvocationContext.ExitCode 稳定传播 ✅
- **根因定位**:
  - `PipeMux.CLI` 当前引用 `System.CommandLine 2.0.0-beta4.22272.1`
  - 普通 app 调用路径使用 `SetHandler(async ...)`，失败时仅设置 `Environment.ExitCode = 1`；该版本下 `rootCommand.InvokeAsync(args)` 仍会返回 handler 默认的成功退出码，导致进程实际退出码不稳定地保持为 0
- **修正方案**:
  - 将普通 app 调用路径改为 `SetHandler(async (InvocationContext context) => ...)`
  - 在 handler 内基于 Broker 响应显式设置 `context.ExitCode = 0/1`
  - 保留现有 `RootCommand` 帮助/解析行为，不引入额外命令分发重构
- **回归结果**:
  - 隔离 HOME 下复现确认：`pmux unknown-app test` 现返回退出码 `1`，stderr 为 `Error: Unknown app: unknown-app`
  - `bash tests/test-management-commands-e2e.sh` 已恢复对“卸载后 app invoke 失败”的非零退出码断言并通过 ✅

### 管理命令后续化简落地 (2026-04-21): 协调器提炼 + 自动化回归 ✅
- **新增结构收敛**:
  - 新增 `src/PipeMux.Broker/BrokerCoordinator.cs`，封装配置快照、进程获取/启动、停止、注销与共享 gate
  - `BrokerServer` 与 `ManagementHandler` 不再各自持有 raw lock / 生命周期编排细节，统一委托给协调器
- **新增自动化验证**:
  - 新增 `tests/test-management-commands-e2e.sh`
  - 覆盖 `register -> list -> invoke -> unregister(保护) -> unregister --stop -> list`，并额外校验 `broker.toml` 的写入与移除
- **实现取舍**:
  - 回归脚本对管理命令失败场景断言退出码；对“卸载后 app invoke 失败”暂按错误文本断言，因为当前 CLI 普通 app 错误路径的退出码传播仍不稳定
- **验证**:
  - `dotnet build PipeMux.sln` succeeded ✅
  - `bash tests/test-management-commands-e2e.sh` passed ✅

### 管理命令落地后的进一步化简评估 (2026-04-21): 小而稳优先，避免再做大重构 💡
- **当前判断**:
  - 经过“事务性写入 + 启动/卸载线性化 + host 参数收敛”后，当前实现已接近一个较好的局部最优；继续优化更适合做小范围收敛，而不是再引入大模型重构
- **值得优先考虑的后续化简**:
  - 为 `:register / :unregister` 补 1 条自动化 E2E 回归，优先覆盖保护语义与持久化闭环，降低后续修改风险
  - 若后续管理命令继续增多，可提炼一个 broker 协调器对象，封装 `BrokerServer + ManagementHandler + BrokerConfigStore` 之间共享的 raw lock / 生命周期编排
  - 清理轻微技术债：`BrokerConfigStore.TryGetApp()` 当前未使用，`BrokerServer.ProcessAcquisitionResult` 可在未来改成更轻的私有 record/tuple
- **暂不建议现在做的事**:
  - 不建议立刻把全局 broker gate 拆成 per-app gate；当前本地 broker 负载模型下，正确性收益明显高于并发收益
  - 不建议立刻把 `command` 字符串迁移为完整结构化 launcher 配置；只有在确实需要支持更多自定义启动形态时才值得承担迁移成本
- **策略建议**:
  - 如果只再做一轮“小而稳”的继续优化，首选“补自动化回归 + 提炼 broker 协调器”；前者补安全网，后者补可维护性

### 管理命令修正落地 (2026-04-21): 事务性写入 + 删除/启动线性化 + host 参数收敛 ✅
- **已实现**:
  - `BrokerConfigStore` 改为基于副本落盘成功后再提交内存，避免配置保存失败导致内存/磁盘分叉
  - `BrokerServer` 将“查配置 -> 复用/启动进程”收敛到同一 broker gate 内，与 `:stop` / `:unregister` 线性化
  - `:register` 的 host 选项收敛为 `--host-path`（兼容旧 `--host` 别名），语义为 `PipeMux.Host` 可执行路径，不再承诺支持任意 host command line
- **行为结果**:
  - 运行中 `:unregister <app>` 默认拒绝；`--stop` 时先停进程再删配置
  - `:register` 若显式提供 host 路径，会校验路径格式与文件存在性；更复杂启动命令仍建议手改 `broker.toml`
- **验证**:
  - `dotnet build PipeMux.sln` succeeded ✅
  - 隔离 HOME 冒烟验证通过：`register -> list -> invoke -> unregister(保护) -> unregister --stop -> list` ✅

### 管理命令后续修正方案评估 (2026-04-21): 事务性写入 + 删除/启动线性化 + host 参数收敛 💡
- **审阅结论**:
  - 当前 `:register` / `:unregister` 方向正确，但存在 3 个实现风险：配置落盘失败时内存/磁盘分叉、`unregister --stop` 与并发请求存在重新拉起窗口、`--host` 语义过宽但解析能力不足
- **推荐短期方案**:
  - `BrokerConfigStore` 对配置变更采用“失败回滚”或“副本写入后提交”，确保保存失败不会污染内存态
  - 用同一把 broker 级 gate 线性化“查配置 -> 判定/启动进程”和“stop/unregister”，先求正确性，再考虑按 app 细化锁粒度
  - 将 `:register` 的 `--host` 收敛为仅接受 `PipeMux.Host` 可执行路径（建议改名 `--host-path` 或 `--host-exe`），不再承诺支持任意 host command line
- **可接受的功能简化**:
  - `:register` 只覆盖“注册 PipeMux.Host 托管 DLL 入口”这一主路径；更复杂的启动命令继续通过手改 `broker.toml` 处理
- **实现取舍**:
  - 相比引入 per-app lock / 结构化 launcher 配置等更大重构，以上方案改动面更小，能优先修正正确性问题

### 管理命令落地 (2026-04-21): CLI 注册/移除 + Broker 独占持久化 ✅
- **目标**: 降低 `PipeMux.Host` 使用门槛，避免手改 `broker.toml`
- **新增命令**:
  - `pmux :register <app> <assemblyPath> <entryMethod> [--host <host-command>]`
  - `pmux :unregister <app> [--stop]`
- **架构实现**:
  - CLI 仅做命令解析并向 Broker 发送管理请求（不直接写配置）
  - Broker 新增 `src/PipeMux.Broker/BrokerConfigStore.cs`，对 `Apps` 做线程安全更新并原子落盘 `broker.toml`
  - `:unregister` 对运行中实例增加保护：默认拒绝，`--stop` 时先停进程再删除配置
- **协议扩展**:
  - `src/PipeMux.Shared/Protocol/ManagementCommand.cs` 新增 `Register/Unregister` kind 与注册参数字段
- **并发安全**:
  - `BrokerServer` 与 `ManagementHandler` 共享同一 `configLock`，避免请求处理与配置写入并发冲突
- **文档更新**:
  - `docs/README.md` 与 `src/PipeMux.Broker/README.md` 已补充新命令示例
- **验证**:
  - `dotnet build` succeeded ✅
  - 隔离 HOME 冒烟验证通过：`register -> list -> unregister`、运行中 `unregister` 防护与 `--stop` 行为均符合预期 ✅
- **备注**:
  - `docs/reports/migration-log.md` 与 `agent-team/indexes/README.md` 在当前仓库不存在，无法按提示核对 changefeed delta

### 后续化简落地 (2026-04-21): 端点统一 + 协议收敛 + sample/doc 清理 ✅
- **统一 Broker 连接默认值**:
  - 新增 `src/PipeMux.Shared/BrokerConnectionDefaults.cs` / `BrokerConnectionResolver.cs` / `BrokerEndpoint.cs`
  - `broker.toml` 路径、默认 socket path、默认 pipe name、env var 名称、client/server transport 选择逻辑统一下沉到 Shared
  - Broker 与 CLI 不再各自维护一套默认值和判定分支
- **强类型 `InvokeResult` 下沉**:
  - 新增 `src/PipeMux.Shared/Protocol/InvokeResult.cs`
  - Broker 改为 `InvokeAsync<InvokeResult>()` 强类型调用，不再手工解析 `JsonElement` 或兼容 Pascal/camelCase 属性名
  - `src/PipeMux.Sdk/InvokeResult.cs` 已重命名为 `.bak`，SDK 直接使用 Shared 协议类型
- **移除 `SessionId` 遗留抽象**:
  - 从 `Request` / `Response` 模型中移除 `SessionId`
  - CLI 不再输出未实现的 `[Session: ...]`
- **sample / 文档清理**:
  - `samples/Calculator/Program.cs` 抽取重复命令模板，减少样板 `try/catch + 输出` 代码
  - 更新 `samples/Calculator/README.md`、`src/PipeMux.Broker/README.md`、`docs/pipemux-quickstart.md`、`docs/README.md`
  - `docs/task4-implementation-report.md` 增加 historical note，避免与当前实现混淆
- **验证**:
  - `dotnet build` succeeded，0 warning，0 error ✅

### 后续化简候选分析 (2026-04-21): 可继续收敛的 4 个方向 💡
- **高优先级**:
  - 统一 Broker 连接默认值与配置路径：`broker.toml` 路径、默认 socket path、默认 pipe name、transport 选择逻辑仍分散在 Broker/CLI 多处，适合下沉到 Shared
  - 将 `InvokeResult`/app invoke 协议下沉到 Shared：Broker 目前仍以 `JsonElement` + Pascal/camelCase 双分支手工解析返回值，适合改为强类型反序列化
- **中优先级**:
  - 清理半成品 session 抽象：`Request.SessionId` / `Response.SessionId` 已存在，但 Broker 尚未真正使用；建议二选一，要么落地要么移除
  - 精简 sample / 文档：`samples/Calculator/Program.cs` 里命令处理重复较多，且部分 README/报告仍停留在旧 JSON-RPC 手写阶段
- **策略建议**: 若只做一轮“小而稳”的继续化简，优先做“连接配置统一 + 强类型 InvokeResult”；收益最高，风险最低

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
