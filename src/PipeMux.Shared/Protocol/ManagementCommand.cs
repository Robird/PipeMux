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
    /// <summary>注册应用并写入配置</summary>
    Register,
    /// <summary>移除已注册应用并写入配置</summary>
    Unregister,
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
    /// 目标应用（用于 Stop/Restart/Unregister）
    /// </summary>
    public string? TargetApp { get; init; }

    /// <summary>
    /// register 命令的 PipeMux.Host 可执行路径（默认从 PATH 查找 pipemux-host）
    /// </summary>
    public string? HostPath { get; init; }

    /// <summary>
    /// register 命令的目标程序集路径
    /// </summary>
    public string? TargetAssemblyPath { get; init; }

    /// <summary>
    /// register 命令的目标入口方法
    /// </summary>
    public string? TargetMethodName { get; init; }

    /// <summary>
    /// 通用布尔开关（当前用于 unregister --stop）
    /// </summary>
    public bool Flag { get; init; }

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

        // 两遍法：先把 `--name value` 与 `--flag` 抽走，剩余 token 才是位置参数。
        // 这样无论 `--host-path /x counter ...` 还是 `counter ... --host-path /x`，
        // 解析结果都一致；位置参数也不会被 `--stop` 之类的 flag 误占。
        var optionSpecs = command switch {
            "register" => new (string[] Names, bool TakesValue)[] {
                (new[] { "--host-path", "--host" }, true),
            },
            "unregister" => new (string[] Names, bool TakesValue)[] {
                (new[] { "--stop" }, false),
            },
            _ => Array.Empty<(string[] Names, bool TakesValue)>(),
        };

        if (!TrySplitArgs(args, optionSpecs, out var positional, out var options)) {
            return null;
        }

        var targetApp = positional.Count > 0 ? positional[0] : null;

        if (command == "register") {
            if (positional.Count < 3) {
                return null;
            }

            options.TryGetValue("--host-path", out var hostPath);

            return new ManagementCommand {
                Kind = ManagementCommandKind.Register,
                TargetApp = positional[0],
                TargetAssemblyPath = positional[1],
                TargetMethodName = positional[2],
                HostPath = hostPath,
            };
        }

        if (command == "unregister") {
            if (string.IsNullOrWhiteSpace(targetApp)) {
                return null;
            }

            return new ManagementCommand {
                Kind = ManagementCommandKind.Unregister,
                TargetApp = targetApp,
                Flag = options.ContainsKey("--stop"),
            };
        }

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
    /// 把原始参数拆成 “位置参数列表” + “已识别选项字典”。
    /// - 已声明的 flag（TakesValue=false）：出现即记录，不消费下一个 token；
    /// - 已声明的带值选项：缺值或值以 "--" 开头一律视为非法，返回 false；
    /// - 未声明的 "--xxx" 一律视为非法，返回 false（避免无声吞掉错别字）。
    /// 选项字典 key 统一使用其 “主名称”（即 Names[0]）。
    /// </summary>
    private static bool TrySplitArgs(
        string[]? args,
        (string[] Names, bool TakesValue)[] optionSpecs,
        out List<string> positional,
        out Dictionary<string, string> options
    ) {
        positional = new List<string>();
        options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (args == null || args.Length == 0) {
            return true;
        }

        for (var i = 0; i < args.Length; i++) {
            var token = args[i];

            if (!token.StartsWith("--", StringComparison.Ordinal)) {
                positional.Add(token);
                continue;
            }

            var spec = optionSpecs.FirstOrDefault(s => s.Names.Any(n => string.Equals(n, token, StringComparison.OrdinalIgnoreCase)));
            if (spec.Names == null) {
                return false;
            }

            var primaryName = spec.Names[0];

            if (!spec.TakesValue) {
                options[primaryName] = string.Empty;
                continue;
            }

            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal)) {
                return false;
            }

            options[primaryName] = args[i + 1];
            i++;
        }

        return true;
    }

    /// <summary>
    /// 检查字符串是否为管理命令（以 : 开头）
    /// </summary>
    public static bool IsManagementCommand(string? input) {
        return !string.IsNullOrEmpty(input) && input.StartsWith(':');
    }
}
