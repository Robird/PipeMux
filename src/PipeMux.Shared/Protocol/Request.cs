namespace PipeMux.Shared.Protocol;

/// <summary>
/// 统一请求格式: CLI → Broker → Backend App
/// </summary>
public sealed class Request {
    /// <summary>
    /// 目标应用名称 (如 "calculator", "texteditor")
    /// 对于管理命令，此字段为 null 或空
    /// </summary>
    public string? App { get; init; }

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
    /// 请求 ID (用于追踪和调试)
    /// </summary>
    public string RequestId { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// 管理命令（当请求为管理命令时设置）
    /// </summary>
    public ManagementCommand? ManagementCommand { get; init; }

    /// <summary>
    /// 判断此请求是否为管理命令
    /// </summary>
    public bool IsManagementRequest => ManagementCommand != null;
}
