namespace PipeMux.Shared.Protocol;

/// <summary>
/// 后端应用对 invoke 请求的返回值。
/// </summary>
public sealed class InvokeResult {
    public int ExitCode { get; init; }
    public string Output { get; init; } = "";
    public string Error { get; init; } = "";
}
