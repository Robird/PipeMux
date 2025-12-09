namespace PipeMux.Shared.Protocol;

/// <summary>
/// 统一请求格式: CLI → Broker → Backend App
/// </summary>
public sealed class Request {
    /// <summary>
    /// 目标应用名称 (如 "calculator", "texteditor")
    /// </summary>
    public required string App { get; init; }

    /// <summary>
    /// 原始命令行参数（完整传递给后端，由后端的 System.CommandLine 解析）
    /// 例如: ["add", "10", "20"] 或 ["--help"]
    /// </summary>
    public string[] Args { get; init; } = [];

    /// <summary>
    /// 终端标识符（自动检测，用于多终端隔离）
    /// 同一终端的请求路由到同一 App 实例
    /// </summary>
    public string? TerminalId { get; init; }

    /// <summary>
    /// 会话 ID (可选，显式指定时可跨终端访问同一实例)
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// 请求 ID (用于追踪和调试)
    /// </summary>
    public string RequestId { get; init; } = Guid.NewGuid().ToString();
}
