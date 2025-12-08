# PipeMux Broker & CLI

为 LLM Agent 设计的有状态、交互式编辑器系统。

## 愿景

从"CLI 时代"到"PipeMux 时代"的转变：

- **输入**: CLI / Tool Calling (LLM Agent 普遍擅长)
- **输出**: Markdown (直接注入 LLM 上下文)
- **状态**: 持久化进程 (光标、选区、undo 栈)
- **目标**: 实况信息单份显示，避免 `read_file` 历史堆积

## 项目结构

```
src/
├── PipeMux.Shared/       # 共享协议 (Request/Response/JsonRpc)
├── PipeMux.Broker/       # 中转服务器 (进程管理 + 路由)
├── PipeMux.CLI/          # 统一 CLI 前端
└── PipeMux.TextEditor/   # 文本编辑器后台应用 (基于 PieceTreeSharp)
```

## 当前状态

⚠️ **这是初始骨架实现** (2025-12-06)

✅ **已完成**:
- 项目结构和依赖关系
- 协议定义 (Request/Response)
- 基础 Markdown 渲染 (行号 + 光标)
- 配置文件加载 (TOML)

🚧 **待实现**:
- Named Pipe / Unix Socket 通信
- JSON-RPC 完整循环
- Edit 命令族 (insert/delete/replace)
- Undo/Redo + 装饰系统集成

## 快速开始

参考 [`docs/pipemux-quickstart.md`](../../docs/pipemux-quickstart.md)

## 架构设计

参考 [`docs/plans/pipemux-broker-architecture.md`](../../docs/plans/pipemux-broker-architecture.md)

## 最终目标

集成到自研 Agent 环境，通过 **tool calling** 替代 CLI，实现真正的 LLM-Native 编辑器。
