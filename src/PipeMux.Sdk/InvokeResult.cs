namespace PipeMux.Sdk;

/// <summary>
/// 命令调用的结果
/// </summary>
public class InvokeResult {
    /// <summary>
    /// 命令退出码
    /// </summary>
    public int ExitCode { get; set; }

    /// <summary>
    /// 标准输出内容
    /// </summary>
    public string Output { get; set; } = "";

    /// <summary>
    /// 标准错误输出内容
    /// </summary>
    public string Error { get; set; } = "";
}
