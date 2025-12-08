# PipeMux Broker + CLI 快速开始

## 项目结构

```
src/
├── PipeMux.Shared/       # 共享协议定义 (Request/Response/JsonRpc)
├── PipeMux.Broker/       # 中转服务器 (进程管理 + 路由)
├── PipeMux.CLI/          # 统一 CLI 前端
└── PipeMux.TextEditor/   # 文本编辑器后台应用 (基于 PieceTreeSharp)
```

## 快速开始

### 1. 构建项目

```bash
cd /repos/PieceTreeSharp
dotnet build src/PipeMux.Broker
dotnet build src/PipeMux.CLI
dotnet build src/PipeMux.TextEditor
```

### 2. 配置 Broker (可选)

复制示例配置到用户目录:

```bash
# Linux/Mac
mkdir -p ~/.config/pipemux
cp docs/examples/broker.toml ~/.config/pipemux/

# Windows
# 将 broker.toml 复制到 %APPDATA%\pipemux\
```

编辑 `broker.toml` 中的路径以匹配你的环境。

### 3. 启动 Broker

```bash
# Terminal 1: 启动 Broker 服务器
dotnet run --project src/PipeMux.Broker
```

### 4. 使用 CLI

```bash
# Terminal 2: 使用 TextEditor

# 打开文件
dotnet run --project src/PipeMux.CLI texteditor open ./Program.cs

# 移动光标到第 10 行
dotnet run --project src/PipeMux.CLI texteditor goto-line 10

# 重新渲染当前状态
dotnet run --project src/PipeMux.CLI texteditor render
```

## 当前状态 (Skeleton)

⚠️ **这是初始骨架实现**，以下功能尚未完成:

- [ ] Named Pipe / Unix Socket 通信 (Broker ↔ CLI)
- [ ] 完整的 JSON-RPC 循环 (Broker ↔ TextEditor)
- [ ] 会话持久化和超时管理
- [ ] 错误处理和日志
- [ ] Edit 命令族 (insert/delete/replace)
- [ ] 选区和装饰系统

✅ **已完成的骨架**:

- [x] 项目结构和依赖关系
- [x] 协议定义 (Request/Response)
- [x] 进程注册表基础实现
- [x] Markdown 渲染器 (基础行号 + 光标)
- [x] 配置文件加载 (TOML)

## 下一步开发

参考 [`docs/plans/pipemux-broker-architecture.md`](../plans/pipemux-broker-architecture.md) 的 Phase 1 路线图。

优先实现:
1. Named Pipe 服务器循环 (Broker)
2. Named Pipe 客户端 (CLI)
3. JSON-RPC 完整循环测试

## 设计理念

从"CLI 时代"到"PipeMux 时代"的转变:
- **输入**: CLI (LLM Agent 普遍擅长)
- **输出**: Markdown (直接注入 LLM 上下文)
- **状态**: 持久化进程 (光标/选区/undo 栈)
- **目标**: 实况信息单份显示，避免 `read_file` 历史堆积

最终会集成到自研 Agent 环境，通过 **tool calling** 替代 CLI。
