# PipeMux Broker Architecture Plan

> 创建时间: 2025-12-06  
> 状态: Planning / Prototyping

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

---

## 架构设计

### 三层结构

```
┌─────────────────────────────────────────────────────────────┐
│  消费者层                                                     │
├─────────────────────────────────────────────────────────────┤
│  阶段 1: CLI 前端 (PipeMux.CLI)                                │
│    - 开发/调试阶段使用                                        │
│    - 命令: pipemux <app> <command> [args...]                  │
│    - 输出: 直接 print Markdown 到 stdout                     │
├─────────────────────────────────────────────────────────────┤
│  阶段 2: Tool Calling (自研 Agent 环境集成)                  │
│    - 生产阶段使用                                            │
│    - 工具: pipemux_texteditor_open(path)                      │
│    - 输出: 直接注入 LLM 上下文                               │
└─────────────────────────────────────────────────────────────┘
                            ▼
┌─────────────────────────────────────────────────────────────┐
│  中转层: PipeMux.Broker                                        │
├─────────────────────────────────────────────────────────────┤
│  职责:                                                       │
│    - 进程管理 (Start / List / Close / Auto-start)           │
│    - 请求路由 (CLI args → RPC call)                         │
│    - 会话管理 (多实例隔离)                                    │
│    - 配置驱动 (注册后台应用)                                  │
│                                                              │
│  通信:                                                       │
│    - 前端 ↔ Broker: Named Pipe / Unix Domain Socket        │
│    - Broker ↔ 后台应用: JSON-RPC over stdin/stdout         │
└─────────────────────────────────────────────────────────────┘
                            ▼
┌─────────────────────────────────────────────────────────────┐
│  应用层: 后台 PipeMux 应用                                      │
├─────────────────────────────────────────────────────────────┤
│  PipeMux.TextEditor (基于 PieceTreeSharp)                     │
│    - Open / Goto / Select / Edit / Render                  │
│    - Undo/Redo 栈                                            │
│    - 装饰系统 (光标/选区/查找/诊断)                           │
│    - Markdown 渲染 (行号 + 内容 + 光标/选区标记)              │
│                                                              │
│  未来扩展:                                                   │
│    - PipeMux.Debugger (Step / Watch / Breakpoint)            │
│    - PipeMux.DiffViewer (Side-by-side Markdown)              │
│    - PipeMux.ProjectExplorer (文件树导航)                      │
└─────────────────────────────────────────────────────────────┘
```

### 关键设计决策

#### 1. 通信协议
- **前端 ↔ Broker**: 
  - Named Pipes (Windows) / Unix Domain Socket (Linux/Mac)
  - 避免端口占用，基于文件系统权限控制
- **Broker ↔ 后台应用**: 
  - JSON-RPC over stdin/stdout
  - 简单、跨平台、易调试
  - 后续可扩展流式输出 (SSE / gRPC stream)

#### 2. 会话管理
- **自动分配 Session ID**: 
  ```bash
  $ pipemux texteditor open hello.cs
  Session: te-a3f9b2 created
  [渲染的 Markdown 编辑器界面]
  
  $ pipemux texteditor:te-a3f9b2 goto-line 42
  [更新后的渲染]
  ```
- **隐式当前会话**: 同一 app 的最近活跃会话作为默认目标
- **显式指定**: `pipemux <app>:<session-id> <command>`

#### 3. 配置驱动
```toml
# ~/.config/pipemux/broker.toml

[broker]
socket_path = "~/.local/share/pipemux/broker.sock"  # Unix
# pipe_name = "PipeMuxBroker"  # Windows

[apps.texteditor]
command = "dotnet run --project ~/dev/PieceTreeSharp/src/PipeMux.TextEditor"
autostart = true
timeout = 30  # 秒，无活动自动关闭

[apps.debugger]
command = "~/dev/PipeMux.Debugger/bin/Debug/net9.0/PipeMux.Debugger"
autostart = false
```

#### 4. Markdown 渲染示例
```markdown
# TextEditor: hello.cs (Session: te-a3f9b2)

```csharp
  1 | using System;
  2 | 
  3 | namespace HelloWorld
  4 | {
  5 |     class Program
  6 |     {
  7 |         static void Main(string[] args)
  8 |         {
  9 |             Console.WriteLine("Hello, World!");
 10 |█            ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^ [Selection]
 11 |         }
 12 |     }
 13 | }
```

**Legend**:
- `█` Cursor
- `^^^` Selection range

**Stats**: 13 lines | Ln 10, Col 13 | Modified
```

---

## 实施路线图

### Phase 1: MVP Prototype
**目标**: 验证核心架构可行性

- [ ] **PipeMux.Broker** (控制台应用)
  - 进程注册表 (内存)
  - 命令路由 (app → command dispatcher)
  - 配置加载 (TOML)
  - Named Pipe / Unix Socket 服务器

- [ ] **PipeMux.CLI** (控制台应用)
  - 参数解析 (`<app> <command> [args...]`)
  - 连接 Broker (Named Pipe / Unix Socket)
  - 发送请求 + 接收响应
  - Markdown 输出到 stdout

- [ ] **PipeMux.TextEditor** (控制台应用 + PieceTreeSharp)
  - JSON-RPC 服务器 (stdin/stdout)
  - 命令处理器:
    - `open <path>` → 加载文件到 TextModel
    - `goto-line <line>` → 移动光标
    - `select <start> <end>` → 设置选区
    - `render` → 生成 Markdown
  - 基础 Markdown 渲染 (行号 + 内容 + 光标标记)

**验收标准**:
```bash
# Terminal 1: 启动 Broker
$ pipemux-broker start

# Terminal 2: 使用 TextEditor
$ pipemux texteditor open ./hello.cs
Session: te-a3f9b2 created
[Markdown 渲染]

$ pipemux texteditor goto-line 10
[更新后的渲染，光标在第 10 行]

$ pipemux texteditor render
[当前状态的完整渲染]
```

### Phase 2: 完善功能
- [ ] 流式输出支持 (大文件渲染)
- [ ] 多会话管理完善
- [ ] 装饰系统集成 (查找结果、诊断、高亮)
- [ ] Undo/Redo 栈
- [ ] Edit 命令族 (insert/delete/replace)
- [ ] 状态持久化 (可选，崩溃恢复)

### Phase 3: 自研 Agent 环境集成
- [ ] Tool Calling 适配器
- [ ] 直接上下文注入（跳过 CLI 中转）
- [ ] 性能优化（token 效率、渲染延迟）
- [ ] 安全沙箱（文件访问权限）

### Phase 4: 生态扩展
- [ ] PipeMux.Debugger
- [ ] PipeMux.DiffViewer
- [ ] PipeMux.ProjectExplorer
- [ ] 插件系统？

---

## 技术选型

### 开发环境
- **.NET 9.0**: 现代 C#、跨平台、高性能
- **xUnit**: 单元测试
- **System.IO.Pipelines**: 高效 I/O（后续流式输出）

### 依赖库
- **PieceTreeSharp**: 文本编辑核心（已完成移植）
- **Tomlyn**: TOML 配置解析
- **StreamJsonRpc**: JSON-RPC 实现（可选，或手写简单版本）
- **System.CommandLine**: CLI 参数解析（PipeMux.CLI）

### 跨平台策略
- **Named Pipes**: `System.IO.Pipes.NamedPipeServerStream` (Windows + Linux/Mac)
- **Unix Domain Socket**: `System.Net.Sockets.UnixDomainSocketEndPoint` (.NET 5+)
- 优先选择 **Named Pipes** (跨平台支持更好)

---

## 项目结构

```
PieceTreeSharp/
├── src/
│   ├── PipeMux.Broker/           # 中转服务器
│   │   ├── Program.cs
│   │   ├── ProcessRegistry.cs
│   │   ├── CommandRouter.cs
│   │   ├── ConfigLoader.cs
│   │   └── PipeMux.Broker.csproj
│   │
│   ├── PipeMux.CLI/              # 统一 CLI 前端
│   │   ├── Program.cs
│   │   ├── BrokerClient.cs
│   │   └── PipeMux.CLI.csproj
│   │
│   ├── PipeMux.Shared/           # 共享协议/模型
│   │   ├── Protocol/
│   │   │   ├── Request.cs
│   │   │   ├── Response.cs
│   │   │   └── JsonRpc.cs
│   │   └── PipeMux.Shared.csproj
│   │
│   └── PipeMux.TextEditor/       # 文本编辑器后台应用
│       ├── Program.cs
│       ├── TextEditorService.cs
│       ├── MarkdownRenderer.cs
│       ├── CommandHandlers/
│       │   ├── OpenCommand.cs
│       │   ├── GotoCommand.cs
│       │   ├── SelectCommand.cs
│       │   └── RenderCommand.cs
│       └── PipeMux.TextEditor.csproj
│
├── tests/
│   ├── PipeMux.Broker.Tests/
│   ├── PipeMux.CLI.Tests/
│   └── PipeMux.TextEditor.Tests/
│
└── docs/
    └── plans/
        └── pipemux-broker-architecture.md  # 本文档
```

---

## 与现有 PieceTreeSharp 的关系

### 依赖关系
```
PipeMux.TextEditor
    ├─> PieceTreeSharp.TextBuffer  (TextModel, PieceTree)
    ├─> PieceTreeSharp.Decorations (光标、选区、查找高亮)
    └─> PieceTreeSharp.Find        (查找/替换功能)
```

### 职责边界
- **PieceTreeSharp**: 纯文本编辑引擎（headless library）
  - 不关心渲染、不关心 UI、不关心通信协议
  - 提供 API: TextModel, Cursor, Decorations, Find, Diff 等

- **PipeMux.TextEditor**: 基于 PieceTreeSharp 的 PipeMux 应用
  - 处理命令请求
  - 调用 PieceTreeSharp API
  - 渲染 Markdown 输出
  - 管理会话状态

---

## 关键挑战与风险

### 1. 进程生命周期管理
- **挑战**: 后台应用崩溃时 Broker 如何感知？
- **方案**: 
  - 进程监控 (Process.HasExited)
  - 心跳机制 (定期 ping)
  - 自动重启策略

### 2. 并发控制
- **挑战**: 多个 CLI 实例同时操作同一会话？
- **方案**: 
  - 会话级锁（后台应用内部处理）
  - 乐观并发（版本号检查）

### 3. 错误传播
- **挑战**: 后台应用错误如何清晰传递给用户？
- **方案**: 
  - 结构化错误响应 (JSON-RPC error codes)
  - Markdown 渲染错误信息（红色标注）

### 4. Token 效率
- **挑战**: Markdown 渲染过于冗长？
- **方案**: 
  - 可配置渲染粒度（全屏 vs 视口 vs delta）
  - 压缩策略（行号范围、省略中间内容）
  - 后续 Phase 2 优化

---

## 成功指标

### MVP 阶段
- [ ] 能通过 CLI 打开文件、移动光标、渲染 Markdown
- [ ] Broker 能管理至少 2 个后台应用
- [ ] 配置文件驱动自动启动
- [ ] 基础错误处理（后台应用崩溃、命令不存在）

### 完善阶段
- [ ] 支持 Undo/Redo
- [ ] 装饰系统渲染（查找高亮、诊断）
- [ ] 多会话并发无冲突
- [ ] Token 效率验证（对比 read_file 模式）

### 集成阶段
- [ ] 自研 Agent 环境无缝对接
- [ ] Tool Calling 替代 CLI
- [ ] 实况信息单份显示（无历史堆积）
- [ ] 实际编程任务验证（修改 PieceTreeSharp 自身代码）

---

## 参考与灵感

### 类似项目
- **Language Server Protocol (LSP)**: 编辑器 ↔ 语言服务器的 JSON-RPC 通信
- **Debug Adapter Protocol (DAP)**: 调试器的标准化协议
- **Jupyter Kernel Protocol**: Notebook ↔ 计算后端

### 创新点
- **为 LLM 设计的 UI 范式**: Markdown 而非 2D 网格
- **CLI 作为过渡**: 开发阶段易用，生产阶段可替换
- **有状态交互**: 打破 CLI 无状态限制

---

## 下一步行动

1. **创建项目骨架** (本次会话)
   - PipeMux.Broker.csproj
   - PipeMux.CLI.csproj
   - PipeMux.Shared.csproj
   - PipeMux.TextEditor.csproj

2. **实现最小协议** (下次会话)
   - Request/Response 数据结构
   - JSON-RPC 基础实现

3. **Broker 核心功能** (后续)
   - 进程注册表
   - Named Pipe 服务器
   - 命令路由

4. **TextEditor MVP** (后续)
   - Open/Render 两个命令
   - 基础 Markdown 渲染

---

*创建时间: 2025-12-06*  
*作者: AI Team (刘德智 / SageWeaver)*
