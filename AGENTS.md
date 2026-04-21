## 跨会话记忆文档
本文档伴随每个 user message 注入上下文，是跨会话的外部记忆。**它不是 changelog，也不是工作日志**——只承载"难以从代码 / git 重新发现的稳定知识"。

### 维护方法论（每次写之前过一遍）
- **保留**: 工具/环境怪癖；用户偏好；当前架构契约/不变量；常用命令；关键库 API 模式备忘
- **不保留**:
  - 已落地实现的步骤明细（代码即真相，git diff 可补）
  - "评估了但暂不做"的长篇分析（真要做时重评，旧分析多半已过期）
  - 逐次代码审阅 / 修复日志（git log 的工作）
  - 指向不存在文件的"核对说明"
- **三句自检**: ①一周后能从代码自己发现吗？能 → 不写。②是"决定"还是"过程"？过程 → 不写。③涉及未来改动会踩的不变量吗？是 → 一句话写进"架构契约"。
- **`## 最新进展`节**: 严格只留最近 1-3 条，超过即向下滚动删除。

## 已知工具/环境约束
- **删除文件**会被拦截，需要时用 `mv` 改名替代。
- 不要使用 `insert_edit_into_file` 工具，结果难以修补。
- 临时环境变量必须写成 `export VAR=val && cmd ...`，**不要**用 `VAR=val cmd` 形式（会触发命令行自动审批解析问题）。

## 用户偏好
- 主要用简体中文交流，术语/标识符等实体名称保留原始语言。
- 当模型被问及自身时声明使用 Claude Opus 4.7。

## 项目概览
Broker and Front of Stateful CLI Apps —— 通过 Named Pipe / Unix Domain Socket 把"长生命周期、有状态的 CLI 应用"托管在本地 broker 后面，前端 CLI / Tool Calling 通过 broker 复用同一应用进程，实现状态保持与多终端隔离。

## 仓库地图
- `src/PipeMux.Shared/` — 协议、连接默认值、路径 helper
- `src/PipeMux.Broker/` — broker 核心：endpoint server、进程注册、配置存储、管理命令
- `src/PipeMux.CLI/` — `pmux` 前端
- `src/PipeMux.Sdk/` — App 开发框架（`PipeMuxApp` + StreamJsonRpc）
- `src/PipeMux.Host/` — 通用宿主，反射加载任意 DLL 的 `static RootCommand` 入口
- `samples/Calculator/` — RPN 有状态计算器示例
- `samples/HostDemo/` — `PipeMux.Host` 多入口示例
- `tests/test-management-command-parse.sh` — `ManagementCommand` 解析探针
- `tests/test-management-commands-e2e.sh` — broker 集成 E2E（含重启回读）

## 常用命令
- 构建: `dotnet build PipeMux.sln --nologo`
- Parser 测试: `bash tests/test-management-command-parse.sh`
- Broker E2E: `bash tests/test-management-commands-e2e.sh`

## 当前架构契约（修改时必须保持的不变量）

### 配置持久化
- `BrokerConfigStore` = 内存视图 + 原子落盘，**不持有锁**。
- `BrokerConfigTomlCodec` 是 `BrokerConfig <-> TOML` 的唯一编解码点，`ConfigLoader` 与 `BrokerConfigStore` 都走它。
- 写入采用副本-落盘成功后再提交内存，避免内存/磁盘分叉。

### 管理命令编排
责任链（不要跨层倒灌）：
- `ManagementCommand`（Shared 协议层）— 仅做语法 token / option 解析，两遍法（先抽 `--name value`/`--flag`，剩余 token 才作位置参数；未声明 `--xxx` 直接失败）。
- `HostRegistrationRequest`（Broker）— `:register` 的语义校验、路径规范化、host command 构建。
- `ManagementHandler`（Broker）— 编排请求与响应映射；统一通过私有 `CreateOperationResponse` 把 `BrokerOperationResult` 映射成 response。
- `BrokerCoordinator`（Broker）— **唯一持锁者**，线性化"查配置 / 启停进程 / 管理命令"。
- `BrokerConfigStore` — 仅持久化。

### 端点解析
- `BrokerConnectionResolver` 同时为 server 与 client 解析 endpoint，二者**对称**支持环境变量 `PIPEMUX_SOCKET_PATH` / `PIPEMUX_PIPE_NAME`（兼容旧 `DOCUI_PIPE_NAME`）。
- 默认值集中在 `BrokerConnectionDefaults`，不要在 Broker/CLI 各自重复。

### Launcher
- `:register` 当前**只承诺** `PipeMux.Host` 主路径（必填 `app / assembly / entry`，可选 `--host-path`）；复杂启动命令让用户手改 `broker.toml`。
- `AppSettings.Command` 仍是字符串 launcher（argv 由 `CommandLineParser` 解析，支持基本引号/转义）。引入结构化 launcher 的触发阈值见下文"演进触发阈值"。

### CLI 退出码
- 普通 app 调用路径在 `SetAction` 内基于 broker 响应直接 `return 0/1`（System.CommandLine 2.0+ 的退出码契约）。

## 关键库 API 模式备忘

### System.CommandLine 2.0.6（稳定版）
- 添加子项: `cmd.Arguments.Add / .Options.Add / .Subcommands.Add`（**没有** `AddArgument` 等）
- Argument/Option 描述: 构造函数不接受 description，用 `{ Description = "..." }`
- 调用入口: `rootCommand.Parse(args).InvokeAsync(invocationConfig?)`（**没有** `rootCommand.InvokeAsync(args)`）
- 输出重定向: `new InvocationConfiguration { Output, Error }`，传给 `Parse(...).InvokeAsync(config)`
- Action 内访问输出: `parseResult.InvocationConfiguration.Output`（注意：`parseResult.Configuration` 现在是 `ParserConfiguration`，**没有** Output/Error）
- Async action 推荐签名: `(ParseResult, CancellationToken) => Task<int>`

### 其他
- `StreamJsonRpc 2.22.x` + `Nerdbank.Streams 2.13.x`：`PipeMuxApp` 用 `NewLineDelimitedMessageHandler`（**不是** LSP header 风格）。
- `Tomlyn 0.17.x`：TOML 字段映射默认 PascalCase → snake_case，例如 `AutoStart` ↔ `auto_start`。

## 演进触发阈值（避免过早重构）
仅在以下信号同时出现时再考虑动结构性重构，否则按"小而稳"做局部收敛：
- **结构化 launcher**: 出现 ≥2 个真实需求需要 `working_directory`/`environment`/显式 argv，或频繁出现引号/路径空格导致跨平台启动错误。
- **强类型 `ManagementCommand`**: 再新增 ≥2 个带专属 payload/flag 的命令，或 `ManagementCommand` 字段增长到 8~10 个，或 CLI/Broker/序列化三处重复出现命令专属校验。

## 最新进展（最多保留 3 条）

### 2026-04-21 — System.CommandLine 升级到 2.0.6 ✅
- 全仓 `System.CommandLine` 包统一升到 `2.0.6`（CLI 之前还停留在 beta4，Sdk/HostDemo 在 beta5）。
- 按 [beta5+ migration guide](https://learn.microsoft.com/en-us/dotnet/standard/commandline/migration-guide-2.0.0-beta5) 完成 API 迁移：`SetHandler/InvocationContext/AddArgument` → `SetAction/ParseResult/Arguments.Add`；`CommandLineConfiguration` → `InvocationConfiguration`；`parseResult.Configuration.Output` → `parseResult.InvocationConfiguration.Output`。
- 验证：`dotnet build PipeMux.sln` 0w/0e；parser 与 E2E 两个测试脚本均通过。
- API 模式已沉淀到上节"关键库 API 模式备忘"。

### 2026-04-21 — AGENTS.md 重写 ✅
- 按"外部记忆方法论"压缩 AGENTS.md：从 ~600 行降到 ~110 行；删除已落地实现明细、暂不做的方案分析、逐次审阅日志、指向不存在文件的核对说明。
- 旧版本归档为 `AGENTS.md.bak`，可在需要追溯历史决策时查阅。
