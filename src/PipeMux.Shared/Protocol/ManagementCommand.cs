namespace PipeMux.Shared.Protocol;

/// <summary>
/// 管理命令类型
/// </summary>
public enum ManagementCommandKind {
    /// <summary>列出已注册的应用</summary>
    List,
    /// <summary>列出运行中的进程</summary>
    Ps,
    /// <summary>停止指定应用</summary>
    Stop,
    /// <summary>重启指定应用</summary>
    Restart,
    /// <summary>显示帮助信息</summary>
    Help
}

/// <summary>
/// 管理命令请求
/// </summary>
public sealed class ManagementCommand {
    /// <summary>
    /// 命令类型
    /// </summary>
    public required ManagementCommandKind Kind { get; init; }

    /// <summary>
    /// 目标应用（用于 Stop/Restart）
    /// </summary>
    public string? TargetApp { get; init; }

    /// <summary>
    /// 从命令字符串解析管理命令
    /// </summary>
    /// <param name="input">命令字符串，如 ":list", ":stop", ":ps"</param>
    /// <param name="args">附加参数，如目标应用名</param>
    /// <returns>解析后的管理命令，如果无效则返回 null</returns>
    public static ManagementCommand? Parse(string input, string[]? args = null) {
        if (string.IsNullOrEmpty(input) || !input.StartsWith(':')) {
            return null;
        }

        var command = input[1..].ToLowerInvariant();
        var targetApp = args?.FirstOrDefault();

        return command switch {
            "list" => new ManagementCommand { Kind = ManagementCommandKind.List },
            "ps" => new ManagementCommand { Kind = ManagementCommandKind.Ps },
            "stop" => new ManagementCommand { Kind = ManagementCommandKind.Stop, TargetApp = targetApp },
            "restart" => new ManagementCommand { Kind = ManagementCommandKind.Restart, TargetApp = targetApp },
            "help" or "h" or "?" => new ManagementCommand { Kind = ManagementCommandKind.Help },
            _ => null
        };
    }

    /// <summary>
    /// 检查字符串是否为管理命令（以 : 开头）
    /// </summary>
    public static bool IsManagementCommand(string? input) {
        return !string.IsNullOrEmpty(input) && input.StartsWith(':');
    }
}
