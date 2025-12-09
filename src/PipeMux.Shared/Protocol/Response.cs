namespace PipeMux.Shared.Protocol;

/// <summary>
/// 统一响应格式: Backend App → Broker → CLI
/// </summary>
public sealed class Response {
    /// <summary>
    /// 对应的请求 ID
    /// </summary>
    public required string RequestId { get; init; }

    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// 结果数据 (通常是 Markdown 渲染的文本)
    /// </summary>
    public string? Data { get; init; }

    /// <summary>
    /// 错误信息 (Success = false 时使用)
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// 会话 ID (可能是新创建的)
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// 额外的元数据 (如性能统计、调试信息)
    /// </summary>
    public Dictionary<string, string>? Metadata { get; init; }

    /// <summary>
    /// 创建成功响应
    /// </summary>
    public static Response Ok(string requestId, string? data = null, string? sessionId = null) {
        return new Response {
            RequestId = requestId,
            Success = true,
            Data = data,
            SessionId = sessionId
        };
    }

    /// <summary>
    /// 创建错误响应
    /// </summary>
    public static Response Fail(string requestId, string error) {
        return new Response {
            RequestId = requestId,
            Success = false,
            Error = error
        };
    }
}
