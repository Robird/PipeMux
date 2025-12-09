# PipeMux 管理命令设计 RFC

> 日期: 2025-12-09
> 状态: **已批准并实施 ✅**
> 作者: TeamLeader
> 决策方法: 多模型采样 (9 次, 3 模型)

## 最终决策

**采用方案 A' + 单字符前缀 `:`**

```bash
# 日常使用（不变）
pmux calculator push 10

# 管理命令（`:` 前缀）
pmux :list                 # 列出注册的 App
pmux :ps                   # 列出运行中的实例
pmux :stop calculator      # 停止指定 App
pmux :restart calculator   # 重启指定 App (P2)
pmux :register name cmd    # 注册新 App (P2)
pmux :reload               # 重新加载配置 (P2)
pmux :help                 # 显示帮助
```

## 决策依据

### 多模型采样结果 (2025-12-09)

**第一轮：方案选择** (pmux vs pmux+pmuxctl)

| 采样 | 选择 | 理由 |
|------|------|------|
| 1-10 | A' | 单入口，减少认知负担 |

结果：A' 获得 10/10 票

**第二轮：前缀格式选择**

| 模型 | 采样 1 | 采样 2 | 采样 3 |
|------|--------|--------|--------|
| Codex (GPT) | C | B | B |
| Claude | B | B | B |
| Gemini | B | B | B |

| 选项 | 票数 | 占比 |
|------|------|------|
| A (无前缀) | 0 | 0% |
| **B (单字符 `:`)** | **8** | **89%** |
| C (命名空间 `ctl:`) | 1 | 11% |

### LLM 选择 `:` 前缀的理由

1. 从语法层面彻底消除管理命令与 App 名的歧义
2. 比 `ctl:` 更简洁，输入成本低
3. 视觉上清晰区分
4. 无需维护保留字列表

## 实施状态

### P1 (已完成 ✅)

| 命令 | 功能 | 状态 |
|------|------|------|
| `pmux :list` | 列出已注册的 App | ✅ |
| `pmux :ps` | 列出运行中的实例 | ✅ |
| `pmux :stop <app>` | 停止指定 App | ✅ |
| `pmux :help` | 显示帮助 | ✅ |

### P2 (待实现)

| 命令 | 功能 | 状态 |
|------|------|------|
| `pmux :restart <app>` | 重启指定 App | 🔄 |
| `pmux :register <name> <cmd>` | 运行时注册新 App | 🔄 |
| `pmux :reload` | 重新加载配置 | 🔄 |
| `pmux :status` | Broker 状态 | 🔄 |

## 技术实现

### 协议扩展

```csharp
// PipeMux.Shared/Protocol/ManagementCommand.cs
public enum ManagementCommandKind { List, Ps, Stop, Restart, Help }

public record ManagementCommand(
    ManagementCommandKind Kind,
    string? TargetApp = null
);
```

### CLI 解析

```csharp
// PipeMux.CLI/Program.cs
if (args[0].StartsWith(':'))
{
    // 管理命令
    var mgmtCmd = ManagementCommand.Parse(args);
    return await client.SendManagementAsync(mgmtCmd);
}
else
{
    // App 调用
    return await client.SendAppCallAsync(app, args);
}
```

### Broker 路由

```csharp
// PipeMux.Broker/BrokerServer.cs
if (request.Management != null)
{
    return await _managementHandler.HandleAsync(request.Management);
}
return await HandleAppRequestAsync(request);
```

## 相关文件

- 实施计划: `PipeMux-Management-Implementation-Plan.md`
- QA 报告: `QA-PipeMux-Management-Commands-Report.md`
- Wiki: `wiki/PipeMux/README.md`

---

*RFC 完成: 2025-12-09*
