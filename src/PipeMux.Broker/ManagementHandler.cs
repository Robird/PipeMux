using System.Text;
using PipeMux.Shared.Protocol;

namespace PipeMux.Broker;

/// <summary>
/// 处理管理命令的处理器
/// </summary>
public sealed class ManagementHandler {
    private readonly BrokerCoordinator _coordinator;

    public ManagementHandler(BrokerCoordinator coordinator) {
        _coordinator = coordinator;
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
            ManagementCommandKind.Register => HandleRegisterAsync(request, command),
            ManagementCommandKind.Unregister => HandleUnregisterAsync(request, command),
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

        var apps = _coordinator.SnapshotRegisteredApps();
        if (apps.Count == 0) {
            sb.AppendLine("  (no applications registered)");
        }
        else {
            foreach (var (name, settings) in apps) {
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
        var sb = new StringBuilder();
        sb.AppendLine("Running processes:");
        sb.AppendLine();

        var activeProcesses = _coordinator.SnapshotActiveProcesses();
        if (activeProcesses.Count == 0) {
            sb.AppendLine("  (no running processes)");
        }
        else {
            foreach (var process in activeProcesses) {
                var health = process.IsHealthy ? "healthy" : "unhealthy";
                sb.AppendLine($"  {process.Key}");
                sb.AppendLine($"    PID: {process.ProcessId}, Status: {health}");
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

        var result = _coordinator.StopApp(targetApp);
        return Task.FromResult(result.Success
            ? Response.Ok(request.RequestId, result.Message)
            : Response.Fail(request.RequestId, result.Message));
    }

    /// <summary>
    /// :restart - 重启指定应用 (P2 - 暂未实现)
    /// </summary>
    private Task<Response> HandleRestartAsync(Request request, string? targetApp) {
        return Task.FromResult(Response.Fail(request.RequestId, ":restart command is not yet implemented (P2)"));
    }

    /// <summary>
    /// :register - 注册一个由 PipeMux.Host 托管的 app
    /// </summary>
    private Task<Response> HandleRegisterAsync(Request request, ManagementCommand command) {
        var appName = command.TargetApp;
        var assemblyPath = command.TargetAssemblyPath;
        var methodName = command.TargetMethodName;
        var hostPath = command.HostPath;

        if (string.IsNullOrWhiteSpace(appName) || string.IsNullOrWhiteSpace(assemblyPath) || string.IsNullOrWhiteSpace(methodName)) {
            return Task.FromResult(Response.Fail(
                request.RequestId,
                "Usage: pmux :register <app-name> <assembly-path> <namespace.type.method> [--host-path <pipemux-host-path>]"
            ));
        }

        var expandedPath = PipeMux.Shared.PathHelper.ExpandPath(assemblyPath);
        if (!File.Exists(expandedPath) && !File.Exists(Path.GetFullPath(expandedPath))) {
            return Task.FromResult(Response.Fail(request.RequestId, $"Assembly not found: {assemblyPath}"));
        }

        if (!string.IsNullOrWhiteSpace(hostPath)) {
            if (!LooksLikeFilePath(hostPath)) {
                return Task.FromResult(Response.Fail(
                    request.RequestId,
                    "--host-path must be a path to the PipeMux.Host executable. For custom command lines, edit broker.toml directly."
                ));
            }

            hostPath = Path.GetFullPath(PipeMux.Shared.PathHelper.ExpandPath(hostPath));
            if (!File.Exists(hostPath)) {
                return Task.FromResult(Response.Fail(request.RequestId, $"Host executable not found: {command.HostPath}"));
            }
        }

        var result = _coordinator.RegisterHostApp(appName, assemblyPath, methodName, hostPath);
        return Task.FromResult(result.Success
            ? Response.Ok(request.RequestId, result.Message)
            : Response.Fail(request.RequestId, result.Message));
    }

    /// <summary>
    /// :unregister - 移除已注册 app
    /// </summary>
    private Task<Response> HandleUnregisterAsync(Request request, ManagementCommand command) {
        var appName = command.TargetApp;
        if (string.IsNullOrWhiteSpace(appName)) {
            return Task.FromResult(Response.Fail(request.RequestId, "Usage: pmux :unregister <app-name> [--stop]"));
        }

        var result = _coordinator.UnregisterApp(appName, command.Flag);
        return Task.FromResult(result.Success
            ? Response.Ok(request.RequestId, result.Message)
            : Response.Fail(request.RequestId, result.Message));
    }

    private static bool LooksLikeFilePath(string value) {
        return Path.IsPathRooted(value)
            || value.StartsWith(".", StringComparison.Ordinal)
            || value.StartsWith("~", StringComparison.Ordinal)
            || value.Contains(Path.DirectorySeparatorChar)
            || value.Contains(Path.AltDirectorySeparatorChar);
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
              :register <app> <assembly> <entry> [--host-path <pipemux-host-path>]
                             Register an app hosted by PipeMux.Host
              :unregister <app> [--stop]
                             Remove app from config (optionally stop running instances)
              :help          Show this help message

            Application Commands:

              pmux <app> <args...>    Call an application with arguments
              pmux calculator add 10 20
              pmux texteditor open file.txt

            Examples:

              pmux :list              # Show registered apps
              pmux :ps                # Show running processes
              pmux :stop calculator   # Stop calculator processes
              pmux :register counter ./samples/HostDemo/bin/Debug/net9.0/HostDemo.dll HostDemo.DebugEntries.BuildCounter
              pmux :unregister counter --stop
            """;

        return Task.FromResult(Response.Ok(request.RequestId, help.TrimEnd()));
    }
}
