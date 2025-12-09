using System.Text;
using PipeMux.Shared.Protocol;

namespace PipeMux.Broker;

/// <summary>
/// 处理管理命令的处理器
/// </summary>
public sealed class ManagementHandler {
    private readonly BrokerConfig _config;
    private readonly ProcessRegistry _registry;

    public ManagementHandler(BrokerConfig config, ProcessRegistry registry) {
        _config = config;
        _registry = registry;
    }

    /// <summary>
    /// 处理管理命令
    /// </summary>
    public Task<Response> HandleAsync(Request request) {
        var command = request.ManagementCommand;
        if (command == null) {
            return Task.FromResult(Response.Fail(request.RequestId, "Invalid management command"));
        }

        return command.Kind switch {
            ManagementCommandKind.List => HandleListAsync(request),
            ManagementCommandKind.Ps => HandlePsAsync(request),
            ManagementCommandKind.Stop => HandleStopAsync(request, command.TargetApp),
            ManagementCommandKind.Restart => HandleRestartAsync(request, command.TargetApp),
            ManagementCommandKind.Help => HandleHelpAsync(request),
            _ => Task.FromResult(Response.Fail(request.RequestId, $"Unknown command: {command.Kind}"))
        };
    }

    /// <summary>
    /// :list - 列出已注册的应用
    /// </summary>
    private Task<Response> HandleListAsync(Request request) {
        var sb = new StringBuilder();
        sb.AppendLine("Registered applications:");
        sb.AppendLine();
        
        if (_config.Apps.Count == 0) {
            sb.AppendLine("  (no applications registered)");
        }
        else {
            foreach (var (name, settings) in _config.Apps.OrderBy(kv => kv.Key)) {
                var autoStart = settings.AutoStart ? " [auto-start]" : "";
                sb.AppendLine($"  {name}{autoStart}");
                sb.AppendLine($"    Command: {settings.Command}");
                sb.AppendLine($"    Timeout: {settings.Timeout}s");
            }
        }

        return Task.FromResult(Response.Ok(request.RequestId, sb.ToString().TrimEnd()));
    }

    /// <summary>
    /// :ps - 列出运行中的进程
    /// </summary>
    private Task<Response> HandlePsAsync(Request request) {
        var activeProcesses = _registry.ListActive();
        
        var sb = new StringBuilder();
        sb.AppendLine("Running processes:");
        sb.AppendLine();

        if (activeProcesses.Count == 0) {
            sb.AppendLine("  (no running processes)");
        }
        else {
            foreach (var processKey in activeProcesses.OrderBy(p => p)) {
                var process = _registry.Get(processKey);
                if (process != null) {
                    var health = process.IsHealthy() ? "healthy" : "unhealthy";
                    sb.AppendLine($"  {processKey}");
                    sb.AppendLine($"    PID: {process.ProcessId}, Status: {health}");
                }
            }
        }

        return Task.FromResult(Response.Ok(request.RequestId, sb.ToString().TrimEnd()));
    }

    /// <summary>
    /// :stop - 停止指定应用
    /// </summary>
    private Task<Response> HandleStopAsync(Request request, string? targetApp) {
        if (string.IsNullOrEmpty(targetApp)) {
            return Task.FromResult(Response.Fail(request.RequestId, "Usage: pmux :stop <app-name>"));
        }

        // 查找匹配的进程键（支持精确匹配和前缀匹配）
        var activeProcesses = _registry.ListActive();
        var matchingKeys = activeProcesses
            .Where(key => key == targetApp || key.StartsWith($"{targetApp}:"))
            .ToList();

        if (matchingKeys.Count == 0) {
            return Task.FromResult(Response.Fail(request.RequestId, $"No running process found for: {targetApp}"));
        }

        var stoppedCount = 0;
        foreach (var key in matchingKeys) {
            if (_registry.Close(key)) {
                stoppedCount++;
                Console.Error.WriteLine($"[INFO] Stopped process: {key}");
            }
        }

        var message = stoppedCount == 1
            ? $"Stopped: {matchingKeys[0]}"
            : $"Stopped {stoppedCount} processes for: {targetApp}";
        
        return Task.FromResult(Response.Ok(request.RequestId, message));
    }

    /// <summary>
    /// :restart - 重启指定应用 (P2 - 暂未实现)
    /// </summary>
    private Task<Response> HandleRestartAsync(Request request, string? targetApp) {
        return Task.FromResult(Response.Fail(request.RequestId, ":restart command is not yet implemented (P2)"));
    }

    /// <summary>
    /// :help - 显示帮助信息
    /// </summary>
    private Task<Response> HandleHelpAsync(Request request) {
        var help = """
            PipeMux Management Commands:

              :list          List registered applications
              :ps            List running processes
              :stop <app>    Stop processes for an application
              :help          Show this help message

            Application Commands:

              pmux <app> <args...>    Call an application with arguments
              pmux calculator add 10 20
              pmux texteditor open file.txt

            Examples:

              pmux :list              # Show registered apps
              pmux :ps                # Show running processes
              pmux :stop calculator   # Stop calculator processes
            """;

        return Task.FromResult(Response.Ok(request.RequestId, help.TrimEnd()));
    }
}
