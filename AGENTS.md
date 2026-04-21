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

### System.CommandLine 升级到 2.0.6 (2026-04-21): beta4/beta5 → 稳定版 ✅
- **目标**: 跟随 [migration-guide-2.0.0-beta5](https://learn.microsoft.com/en-us/dotnet/standard/commandline/migration-guide-2.0.0-beta5) 把整个仓库升级到当前最新稳定 `System.CommandLine 2.0.6`
- **升级前状态**:
  - `src/PipeMux.CLI` 仍停留在 `2.0.0-beta4.22272.1`，使用 `SetHandler(InvocationContext)` / `GetValueForArgument` / `AddArgument` 老 API
  - `src/PipeMux.Sdk` 与 `samples/HostDemo` 已经升到 `2.0.0-beta5.25306.1`，但 `PipeMuxApp` 仍用 beta5/6 的 `CommandLineConfiguration`，beta7+ 已拆为 `ParserConfiguration` + `InvocationConfiguration`
  - 各处 `parseResult.Configuration.Output/Error` 在新 API 下不再可用（`Configuration` 现在是 `ParserConfiguration`，无 Output/Error）
- **修改**:
  - 所有 `System.CommandLine` 包引用统一升到 `2.0.6`：
    - `src/PipeMux.CLI/PipeMux.CLI.csproj`
    - `src/PipeMux.Sdk/PipeMux.Sdk.csproj`
    - `samples/HostDemo/HostDemo.csproj`
  - `src/PipeMux.CLI/Program.cs`:
    - 去掉 `using System.CommandLine.Invocation;`
    - `Argument<T>(name, description)` → `new Argument<T>(name) { Description = ... }`
    - `rootCommand.AddArgument(...)` → `rootCommand.Arguments.Add(...)`
    - `SetHandler(async (InvocationContext ctx) => ...)` → `SetAction(async (ParseResult pr, CancellationToken ct) => { ...; return 0/1; })`，退出码改为直接 return
    - `rootCommand.InvokeAsync(args)` → `rootCommand.Parse(args).InvokeAsync()`
  - `src/PipeMux.Sdk/PipeMuxApp.cs`:
    - `new CommandLineConfiguration(_rootCommand) { Output, Error }` → `new InvocationConfiguration { Output, Error }`
    - `await config.InvokeAsync(args)` → `await _rootCommand.Parse(args).InvokeAsync(config)`
  - `samples/HostDemo/DebugEntries.cs` / `samples/Calculator/Program.cs`:
    - `ctx.Configuration.Output/Error` → `ctx.InvocationConfiguration.Output/Error`（适配 beta7+ 的 `ParseResult.Configuration` 已退化为 `ParserConfiguration`）
- **验证**:
  - `dotnet build PipeMux.sln --nologo` succeeded ✅（0 warning, 0 error）
  - `bash tests/test-management-command-parse.sh` passed ✅（9 个 parser case）
  - `bash tests/test-management-commands-e2e.sh` passed ✅（含 register/restart/list/invoke/unregister 完整闭环）
- **API 关键差异（备忘）**:
  - 命令/选项/参数的添加：旧 `AddArgument/AddOption/AddCommand` → 新 mutable 集合 `.Arguments.Add / .Options.Add / .Subcommands.Add`
  - Argument/Option 描述：构造函数不再接受 description，改用初始化器 `{ Description = "..." }`
  - 调用：`rootCommand.InvokeAsync(args)` 不再存在；改为 `rootCommand.Parse(args).InvokeAsync(invocationConfig?)`
  - 输出重定向：`InvocationConfiguration.Output/Error`（不再走 `CommandLineConfiguration`）
  - Action 内部访问输出：`parseResult.InvocationConfiguration.Output`（不是 `parseResult.Configuration.Output`）
  - Async action 推荐签名：`(ParseResult, CancellationToken) => Task<int>`
- **核对说明**:
  - 按提示尝试核对 `docs/reports/migration-log.md` 与 `agent-team/indexes/README.md`，当前仓库内仍不存在，无法执行 changefeed delta 预检查

### 管理命令持久化闭环补测 (2026-04-21): Archimedes 建议合理，已在 `tests/` 内做最小补洞 ✅
- **结论**:
  - Archimedes 提的缺口判断是合理的
  - 原有 `tests/test-management-commands-e2e.sh` 只覆盖“同一 broker 进程内 register 后立即 `:list` / invoke / unregister”的内存态闭环
  - 它虽然会检查 `broker.toml` 已写出，但**没有**验证 broker 重启后 `ConfigLoader + BrokerConfigTomlCodec` 能否把刚写出的配置正确回读
- **本轮修改**:
  - 仅修改 `tests/test-management-commands-e2e.sh`
  - 脚本内新增最小 `start_broker` / `stop_broker` helper，复用现有隔离 HOME / socket 方案
  - 在 `:register` 成功并确认 `broker.toml` 落盘后，新增一段：
    - 停掉 broker
    - 重新拉起 broker
    - 执行 `:list`，断言仍能看到 `counter`
    - 执行一次 `counter inc`，断言成功，借此验证配置回读后 broker 仍能按持久化配置启动 app
  - 由于重启后已经做过一次 `counter inc`，原脚本后续“list and invoke path”里的计数断言相应从 `Counter: 1` 调整为 `Counter: 2`
- **对 parser 探针默认入口的评估**:
  - 本轮**未**把 `tests/test-management-command-parse.sh` 强行并入 `tests/test-management-commands-e2e.sh`
  - 原因是这会把“parser 失败”和“broker 集成失败”混成同一条执行链，降低失败定位效率
  - 当前仓库里也没有一个明确的“管理命令默认统一入口”脚本/CI 约定；若要真正纳入默认入口，更合适的做法是未来在 `tests/` 单独加一个轻量 wrapper 或在 CI 命令约定里串联，而不是把 parser 探针塞进 E2E 脚本本体
- **验证**:
  - `bash tests/test-management-command-parse.sh` passed ✅
  - `bash tests/test-management-commands-e2e.sh` passed ✅
  - 新增回归段已验证通过：`register -> stop broker -> restart broker -> :list -> counter inc`

### 并行结果集成核对 (2026-04-21): 当前实现基本兼容，但建议补 1 轮很小的测试修补后再进主分支 ⚠️
- **核对范围**:
  - `HostRegistrationRequest`
  - `BrokerConfigTomlCodec`
  - `AcquireProcess(string?)`
  - `CreateOperationResponse(...)`
  - parser 测试探针与 `tests/test-management-commands-e2e.sh`
- **只读核对结论**:
  - 当前工作树里的这些并行结果**实现层面彼此兼容**
  - `ManagementHandler -> BrokerCoordinator -> BrokerConfigStore` 的职责边界是自洽的：
    - `ManagementHandler` 负责编排与响应映射
    - `BrokerCoordinator` 负责 gate 内线性化与生命周期
    - `BrokerConfigStore` 负责内存视图 + 原子落盘
    - `ConfigLoader` 负责路径解析与文本读取
  - 本地只读验证已通过：
    - `dotnet build PipeMux.sln --nologo` passed ✅
    - `bash tests/test-management-command-parse.sh` passed ✅
    - `bash tests/test-management-commands-e2e.sh` passed ✅
- **发现的主要遗漏**:
  - 当前 E2E 虽然覆盖了 `register -> list -> invoke -> unregister --stop`，但**没有 broker 重启后的回读验证**
  - 因此 `BrokerConfigStore` 写出的 TOML 与 `ConfigLoader + BrokerConfigTomlCodec` 读回之间，仍缺少一条真正的集成闭环测试；现有脚本更多验证的是“同一 broker 进程内的内存态 + 落盘副作用”
  - parser 探针项目当前也**不在 `PipeMux.sln` 内**，需要单独运行脚本才能覆盖；若后续 CI/人工只跑 solution build + E2E，parser 回归可能被漏掉
- **决策建议**:
  - 结论偏向 **“还需要补一轮小修”**
  - 这轮小修不一定要改产品代码，优先建议：
    - 给管理命令 E2E 补一段“register 后重启 broker，再 `:list` / invoke”的持久化回读校验
    - 明确把 parser 探针纳入默认验证入口（例如统一测试脚本或 CI 命令约定）

### 候选改进 7 评估 (2026-04-21): `AppSettings.Command` 暂未到结构化 launcher 重构阈值 💡
- **评估目标**:
  - 判断当前 `src/PipeMux.Broker/AppSettings.Command` 从字符串 launcher 演进为结构化 launcher 配置，是否已经具备“实际痛点 + 低风险收益”两个条件
  - 若未来要做，收敛最小可行结构、受影响文件范围与迁移路径；本轮仅做验证与方案裁剪，不修改产品代码
- **当前结论**:
  - **现在还不建议做**
  - 当前字符串模型确实不是最强类型，但在本仓库现阶段仍主要承载两种简单形态：
    - 单个可执行文件，如 `"/path/to/Samples.Calculator"`
    - 可执行文件 + 少量参数，如 `"PipeMux.Host <dll> <entry>"`
  - 这些形态已经被现有实现稳定覆盖：
    - `AppProcess` 启动时会先把 `Command` 解析成 argv，再喂给 `ProcessStartInfo.ArgumentList`
    - `:register` 已有意收敛为只生成 `PipeMux.Host` 主路径，明确把“自定义完整命令行”留给手改 `broker.toml`
    - 文档与测试也都围绕这两类 launcher 组织，尚未出现第三类启动需求把字符串模型逼到失真
- **为什么当前还没形成实际痛点**:
  - `AppSettings` 目前只有 `Command + AutoStart + Timeout` 3 个字段，`Command` 尚未和更多启动元数据纠缠在一起
  - 启动路径集中，真正消费 `Command` 的核心只有 broker 启动流程一处：`BrokerCoordinator -> ProcessRegistry/AppProcess`
  - `:register` 并没有暴露“任意 launcher 拼装器”；当前策略是主路径走强约束，复杂场景退回 `broker.toml` 手改，这实际降低了结构化 launcher 立刻落地的收益
  - 当前文档、样例、E2E 都仍把 `command = "..."` 视作稳定用户接口；现在改模型会先带来配置兼容和文档迁移成本，而不是立刻消掉明显 bug
- **字符串模型的真实边界 / 风险点**:
  - 当前字符串更像“broker 启动协议”，不是纯展示字段；若未来继续往里塞更多语义，容易让 `command` 承担超出 argv 的职责
  - `CommandLineParser` 目前只实现“基本引号/转义”语义，足够覆盖现有样例，但并不等于完整 shell 语义
  - 一旦后续需要表达下面这些信息，字符串会开始变得别扭：
    - `working_directory`
    - `environment` / per-app env vars
    - 显式区分 `executable` 与 `args`
    - 是否通过 shell 启动
    - 针对 `PipeMux.Host` 的强类型 `assembly` / `entry_method`
- **如果未来要做，建议的最小可行结构**:
  - 不建议直接上“抽象基类 + 多态 launcher family”大设计
  - **最小可行版本**应先只解决“argv 与额外启动元数据分离”：
    - `AppSettings.Command` 保留兼容入口（过渡期）
    - 新增 `AppLauncherSettings? Launcher`
    - `AppLauncherSettings` 只包含：
      - `string Executable`
      - `List<string> Arguments`
      - `string? WorkingDirectory`
      - `Dictionary<string, string>? Environment`
  - 启动时统一走一个 `LauncherStartInfoFactory`/helper：
    - 若 `Launcher != null`，直接用结构化字段构造 `ProcessStartInfo`
    - 否则回退解析旧 `Command`
  - 先不要在第一步就把 `PipeMux.Host` 单独做成专门 launcher kind；那会把当前问题从“拆 argv”过早升级成“协议建模”
- **如果未来还想继续强类型化，可分两阶段**:
  - 阶段 1：仅把 `Executable/Arguments/WorkingDirectory/Environment` 结构化，保留 `Command` 兼容读路径
  - 阶段 2：只有当 `PipeMux.Host` 成为主流注册路径、并且需要直接编辑/校验 `assembly + entry_method + host_path` 时，再考虑在 launcher 上加 `Kind=process|pipemux_host`
- **迁移成本评估**:
  - **不算极小改动，属于中等成本**
  - 受影响的不只是 `AppProcess`；还包括：
    - 配置模型与 TOML codec
    - `BrokerConfigStore` clone/落盘
    - `:list` / `:unregister` 输出文案（当前直接展示/返回原始 command）
    - `HostRegistrationRequest` 的持久化产物
    - 文档示例与测试脚本
  - 若要兼容旧配置，至少需要经历一段“双写或双读”过渡期；否则会直接破坏现有 `broker.toml`
- **潜在受影响文件范围（若未来实施）**:
  - 必改：
    - `src/PipeMux.Broker/BrokerConfig.cs`
    - `src/PipeMux.Broker/ProcessRegistry.cs`
    - `src/PipeMux.Broker/BrokerConfigStore.cs`
    - `src/PipeMux.Broker/BrokerConfigTomlCodec.cs`
    - `src/PipeMux.Broker/HostRegistrationRequest.cs`
    - `src/PipeMux.Broker/ManagementHandler.cs`
  - 高概率联动：
    - `docs/examples/broker.toml`
    - `docs/README.md`
    - `docs/ubuntu-deployment.md`
    - `tests/test-management-commands-e2e.sh`
    - 任何依赖 `command = "..."` 示例的文档/样例
- **建议的迁移路径（若未来触发重构）**:
  - 第 1 步：在模型中新增 `Launcher`，启动逻辑优先读 `Launcher`，为空时回退 `Command`
  - 第 2 步：`BrokerConfigTomlCodec` 做双向兼容，允许旧 `command` 与新 `[apps.<name>.launcher]`/等价字段共存
  - 第 3 步：`HostRegistrationRequest` 改为直接生成结构化 `Launcher`，同时为了兼容旧展示路径，可临时保留一个规范化 `Command`
  - 第 4 步：补一组配置 round-trip + 启动级测试，覆盖旧配置读取、新配置读取、`PipeMux.Host` 注册持久化
  - 第 5 步：确认文档和样例都切换后，再决定是否废弃 `Command`
- **建议的触发阈值**:
  - 至少出现 **2 个以上** 真实需求明确需要 `working_directory`、`environment`、shell 模式或显式 argv
  - `:register` / 未来管理命令被迫继续扩展 launcher 相关参数，已经不适合再把结果压成单字符串
  - 开始频繁出现“因为引号/转义/路径中空格导致注册或启动出错”的跨平台问题，且不能靠当前 helper 小修小补解决
  - 文档/用户示例里出现越来越多“复杂命令请手改 broker.toml”的例外说明，说明主路径已不再覆盖主流场景
  - 需要对 launcher 做更细粒度展示、审计、差异比较或管理命令编辑，而不只是打印整条命令字符串
- **本轮执行情况**:
  - 仅完成代码与文档验证、方案裁剪，**未修改产品代码**
  - 按提示尝试核对 `docs/reports/migration-log.md` 与 `agent-team/indexes/README.md`，当前仓库内仍不存在这两个文件，无法执行 changefeed delta 预检查

### 候选改进 5 落地 (2026-04-21): `ConfigLoader` / `BrokerConfigStore` 共用 TOML codec ✅
- **任务目标**:
  - 评估 `ConfigLoader` 的 TOML 读取与 `BrokerConfigStore` 的 TOML 写入，当前是否已足以抽出共享 codec/helper
  - 若风险低且有维护收益，则仅在 broker 内做最小收敛，不扩散到 server/CLI/管理命令解析
- **评估结论**:
  - **值得做，而且风险低**
  - 当前真正重复的不是文件 I/O，而是 `BrokerConfig` 的 3 类职责：
    - 默认配置构造
    - `BrokerConfig <-> TOML` 编解码
    - “保留 broker section、替换 apps section”的模型拼装
  - 这些逻辑已经同时存在于 `ConfigLoader` 与 `BrokerConfigStore`，继续分散会让后续新增字段时容易出现“读写两边漏改一侧”的维护风险
  - 但原子写文件、错误包装、管理命令/注册语义等仍不适合合并；本轮刻意不越过这个边界
- **已实现设计**:
  - 新增 `src/PipeMux.Broker/BrokerConfigTomlCodec.cs`
  - codec 只负责：
    - `CreateDefault()`
    - `Deserialize(string toml)`
    - `Serialize(BrokerConfig config)`
    - `CreateWithApps(BrokerConnectionSettings broker, IReadOnlyDictionary<string, AppSettings> apps)`
  - codec 内统一做 `BrokerConfig` 快照化/默认化：
    - 缺失 `Broker` 时补 `new BrokerConnectionSettings()`
    - 缺失 `Apps` 时补空字典
    - 输出时统一 clone 一份 `Broker` / `Apps`，避免读写侧各自手搓模型
  - `ConfigLoader` 改为只负责：
    - 找配置路径
    - 读文件文本
    - 缺文件时打 warning 并走 codec 默认配置
  - `BrokerConfigStore` 改为只负责：
    - 维护内存视图
    - 原子落盘
    - 调 codec 生成待写 `BrokerConfig` 与 TOML 文本
- **修改文件**:
  - `src/PipeMux.Broker/ConfigLoader.cs`
  - `src/PipeMux.Broker/BrokerConfigStore.cs`
  - `src/PipeMux.Broker/BrokerConfigTomlCodec.cs`（新增）
- **验证**:
  - `dotnet build PipeMux.sln --nologo` passed ✅（0 warning, 0 error）
  - `bash tests/test-management-commands-e2e.sh` passed ✅
- **核对说明**:
  - 按提示尝试核对 `docs/reports/migration-log.md` 与 `agent-team/indexes/README.md`，当前仓库内仍不存在这两个文件，无法执行 changefeed delta 预检查
- **实现备注**:
  - 工作树中存在并行未提交改动；本轮未回滚他人修改，仅在允许范围内做最小收敛
  - `BrokerConfigStore` 当前保留“内存视图 + 原子落盘”职责，没有把文件系统操作继续下沉到 codec，避免职责反弹
  - 由于仓库当前主线已使用通用 `TryRegisterApp(string, AppSettings, out string)` 注册接口，本轮保持该接口不变

### 候选改进 2 落地 (2026-04-21): `:register` 规范化/校验收敛到 broker helper ✅
- **变更背景**:
  - 在 `BrokerCoordinator/BrokerConfigStore` 收敛完成后，`:register` 仍有一段职责横跨 `ManagementCommand`、`ManagementHandler`、`BrokerConfigStore`
  - `ManagementCommand` 负责语法 token 解析本身是合理的，但 broker 侧的“路径校验 + 规范化 + host command 构建”继续分散，会让 handler/store 同时理解注册语义
- **本轮判断**:
  - 最小且合理的收敛点是：保持 `ManagementCommand` 只做语法解析；在 broker 内新增专用 helper，把 `:register` 的语义校验与 `AppSettings.Command` 构建集中；`ManagementHandler` 退回编排层，`BrokerConfigStore` 退回纯持久化层
  - 不需要改 `BrokerServer`；`BrokerCoordinator` 与 `BrokerConfigStore` 只做最小签名调整即可，不必扩大到新的并发/配置模型改造
- **已实现**:
  - 新增 `src/PipeMux.Broker/HostRegistrationRequest.cs`
  - `HostRegistrationRequest.TryCreate(...)` 统一处理：
    - 必填参数 usage 校验
    - assembly 路径展开与存在性校验
    - `--host-path` 路径形态校验与存在性校验
    - 默认 `pipemux-host` 回退
    - host command 引号转义与最终 `AppSettings` 构建
  - `ManagementHandler` 的 `HandleRegisterAsync` 改为仅调用 helper + `_coordinator.RegisterApp(...)`
  - `BrokerConfigStore` 的注册接口改为接受已准备好的 `AppSettings`，不再拼接 host command
- **当前职责边界**:
  - `ManagementCommand`: 语法解析（位置参数 / 选项）
  - `HostRegistrationRequest`: broker 语义校验、规范化、命令构建
  - `ManagementHandler`: 请求编排与错误映射
  - `BrokerCoordinator`: 线性化管理操作
  - `BrokerConfigStore`: 内存视图 + 原子落盘
- **验证**:
  - `dotnet build PipeMux.sln --nologo` passed ✅（0 warning, 0 error）
  - `bash tests/test-management-commands-e2e.sh` passed ✅
- **核对说明**:
  - 按提示尝试核对 `docs/reports/migration-log.md` 与 `agent-team/indexes/README.md`，当前仓库中仍不存在这两个文件
- **残余观察**:
  - `:register` 的语法解析仍留在 shared 协议层，这是刻意保留的边界；本轮未把 broker 语义倒灌回 `ManagementCommand`

### 候选改进 1 落地 (2026-04-21): 解析级轻量测试比继续扩 E2E 更合适 ✅
- **任务目标**: 重新评估“为 `ManagementCommand` 解析补测试”在当前仓库形态下是否仍是低成本高收益方案，并在结论成立时做最小落地
- **承载方式评估**:
  - 继续扩 `tests/test-management-commands-e2e.sh` 虽然不需要新项目，但它主要覆盖 `CLI -> Broker -> 配置/进程` 闭环；拿它承载 parser 边界 case，失败定位会落到 CLI/Broker/E2E 多层混在一起，性价比一般
  - 直接引入 xUnit/NUnit 之类正式单测项目当然可行，但当前仓库还没有测试基建；为 1 个 parser 文件补少量 case 就引入完整测试栈，样板和接入成本偏高
  - 最终采用折中方案：在 `tests/` 下新增极小控制台测试项目，直接引用 `PipeMux.Shared` 断言 `ManagementCommand.Parse(...)`，外层再包一层 shell 脚本，保持仓库当前“主要用 shell 跑测试”的习惯
- **已实现**:
  - 新增 `tests/ManagementCommandParseTests/ManagementCommandParseTests.csproj`
  - 新增 `tests/ManagementCommandParseTests/Program.cs`
  - 新增 `tests/test-management-command-parse.sh`
- **覆盖点**:
  - `:register --host-path /x app dll method` 与 `:register app dll method --host-path /x`
  - 旧别名 `--host`
  - `:unregister --stop app` 与 `:unregister app --stop`
  - 未知选项、缺少 option value、缺少位置参数时返回 `null`
- **验证**:
  - `bash tests/test-management-command-parse.sh` passed ✅（9 个 parser case）
  - `bash tests/test-management-commands-e2e.sh` passed ✅
- **结论**:
  - 在当前仓库阶段，这仍是值得做的最小有效方案：补到了 parser 语义空白，也避免把测试体系一下子扩到超出需求
- **备注**:
  - 按提示尝试核对 `docs/reports/migration-log.md` 与 `agent-team/indexes/README.md`，但这两个路径在当前仓库中仍不存在，无法执行 changefeed delta 预检查

### ManagementHandler 小化简 (2026-04-21): 统一 BrokerOperationResult 映射 ✅
- **评估结论**:
  - `ManagementHandler` 中 `:stop / :register / :unregister` 都使用同一 `BrokerOperationResult` 契约，并各自重复一段完全同形的 `Success ? Response.Ok : Response.Fail` 映射
  - 这已足够支撑一个很小的私有 helper；无需新增抽象层，也不值得把映射下沉到 coordinator/shared
- **最小实现**:
  - 仅修改 `src/PipeMux.Broker/ManagementHandler.cs`
  - 新增私有 `CreateOperationResponse(string requestId, BrokerOperationResult result)`，三处命令处理统一复用
  - 未修改 `BrokerCoordinator` / `BrokerConfigStore` / `BrokerServer`
- **验证**:
  - `dotnet build PipeMux.sln --nologo` 未能完成；环境中已有进程占用 `src/PipeMux.Broker/bin/Debug/net9.0/PipeMux.Broker.runtimeconfig.json`
  - 改用独立输出目录验证：`dotnet build src/PipeMux.Broker/PipeMux.Broker.csproj --nologo -o /tmp/pipemux-broker-build-check` passed ✅
  - 构建仅出现 1 条已有 warning：`BrokerCoordinator.cs(61,31) CS8604`
- **备注**:
  - 按提示核对时，`docs/reports/migration-log.md` 与 `agent-team/indexes/README.md` 当前仓库内仍不存在

### 候选改进 6 评估 (2026-04-21): `ManagementCommand` 暂未到强类型重构阈值 💡
- **评估目标**: 判断当前 `src/PipeMux.Shared/Protocol/ManagementCommand.cs` 是否应从“`Kind + 可选字段包`”演进为更强类型的命令模型
- **当前结论**:
  - 现在**还不建议立刻重构**；当前模型已有一些味道，但尚未造成明显复杂度失控
  - 主要原因是：字段总量仍小（`Kind + 5` 个命令专属字段 + `Flag`），消费者集中（CLI parse 1 处、Broker handler 1 处），且额外复杂度目前主要停留在“可表示无效状态”层面，还没有扩散成多处判空/多处协议兼容胶水
- **已观察到的设计味道**:
  - `Flag` 语义过泛，目前实际只代表 `unregister --stop`
  - `ManagementCommand` 可表示大量无效组合，例如 `Kind=Register` 但缺少 `TargetAssemblyPath`
  - `ManagementHandler` 仍需在 `Kind` 分发后再次读取对应字段，相当于做了一次“手动拆包”
- **为什么还没到阈值**:
  - 只有 `register` 真正携带多字段负载；`unregister` 只额外带一个布尔选项；`list/ps/help` 仍是零载荷命令
  - `Parse()` 虽然开始出现命令特化逻辑，但仍局限在单文件内，没有出现多层 helper / validator / serializer 协同维护的迹象
  - 当前 JSON 协议仍是简单 `System.Text.Json` 直接序列化 POCO；若改强类型，真正新增复杂度会落在多态序列化与协议兼容，而不是业务逻辑本身
- **如果现在就做，最小可行设计**:
  - 保留 `Request.ManagementCommand` 这个协议入口，但把 `ManagementCommand` 改成抽象基类或 interface
  - 派生为 6 个命令记录类型：`ListManagementCommand` / `PsManagementCommand` / `StopManagementCommand(app)` / `RestartManagementCommand(app)` / `RegisterManagementCommand(app, assemblyPath, methodName, hostPath)` / `UnregisterManagementCommand(app, stopRunningProcesses)` / `HelpManagementCommand`
  - `Parse()` 继续留在 Shared 层，但改为直接构造对应派生类型
  - Broker 侧 `ManagementHandler` 从 `switch (command.Kind)` 改为对运行时类型做模式匹配
  - 协议层需补 `System.Text.Json` 多态配置（discriminator 可继续沿用 `kind`）
- **若未来要做，受影响文件范围**:
  - 必改：`src/PipeMux.Shared/Protocol/ManagementCommand.cs`
  - 高概率需要联动：`src/PipeMux.Shared/Protocol/Request.cs`、`src/PipeMux.Shared/Protocol/JsonRpc.cs`、`src/PipeMux.Broker/ManagementHandler.cs`、`src/PipeMux.CLI/Program.cs`
  - 建议同步补测试：现有 `tests/test-management-commands-e2e.sh` 外，再加 parser/serialization 级测试，优先覆盖多态 round-trip
- **建议的触发阈值**:
  - 管理命令再新增 **2 个以上带专属 payload/flag 的命令**，例如 `reload/status/export/import` 之类，不再只是零载荷或单 `TargetApp`
  - `ManagementCommand` 的可选字段继续增长到 **8~10 个量级**，或再出现第 2 个像 `Flag` 这样的“语义复用字段”
  - CLI parse / Broker handler / 协议序列化三处开始出现**重复的命令专属校验或判空**
  - 为兼容新命令被迫引入 `OptionBag`、多个通用布尔位，或 `Kind` 分发后又嵌套第二层 `switch`
- **本轮执行情况**:
  - 只做评估与方案裁剪，**未修改产品代码**
  - 按文档提示核对 `docs/reports/migration-log.md` 与 `agent-team/indexes/README.md` 时，这两个路径在当前仓库中仍不存在

### Staged 代码审阅 (2026-04-21): 管理命令化简二轮复核通过 ✅
- **审阅对象**: 当前暂存区 `git diff --staged`
- **意图理解**:
  - 继续把 `:register / :unregister` 相关实现收敛到更清晰的 broker 协调层
  - 补齐 CLI 失败退出码、管理命令参数顺序无关解析、server/client 端点注入对称性
  - 用更贴近部署形态的 E2E 脚本为上述行为兜底
- **审阅结论**:
  - 设计方向合理：`BrokerCoordinator` 统一线性化“查配置 / 启停进程 / 管理命令”，`BrokerConfigStore` 退化为纯内存视图 + 原子落盘，职责边界比前一版更清楚
  - 实现上未发现新的正确性问题；`CloseProcess(processKey)` 也修正了此前错误回收借用 `StopApp` 前缀匹配的语义问题
  - `ManagementCommand` 两遍法解析能正确覆盖 `:register --host-path /x app dll method` 与 `:unregister --stop app`
- **额外验证**:
  - `dotnet build PipeMux.sln --nologo` passed ✅
  - `bash tests/test-management-commands-e2e.sh` passed ✅
  - 隔离 HOME 下补充验证：
    - `pmux unknown-app test` 返回退出码 `1`，stderr 为 `Error: Unknown app: unknown-app` ✅
    - `:register --host-path <path> app dll method` 与 `:unregister --stop app` 均通过 ✅
- **残余风险 / 观察项**:
  - 当前自动化仍主要覆盖 `PipeMux.Host` 主路径；若后续扩展更多管理选项，建议继续围绕 parser 顺序无关语义补 CLI/协议层测试

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
