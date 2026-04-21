using System.Text;
using PipeMux.Shared;
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
        sb.AppendLine("Registered apps:");
        sb.AppendLine();

        var apps = _coordinator.SnapshotRegisteredApps();
        if (apps.Count == 0) {
            sb.AppendLine("  (no apps registered)");
            sb.AppendLine();
            AppendFirstTimeSetup(sb);
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
            sb.AppendLine();
            sb.AppendLine("Hint:");
            sb.AppendLine("  Run 'pmux :list' to inspect registered apps.");
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
            return Task.FromResult(Response.Fail(
                request.RequestId,
                """
                Usage: pmux :stop <app-name>
                Example:
                  pmux :stop calculator
                Tip:
                  Run 'pmux :ps' to see running processes first.
                """.TrimEnd()));
        }

        return Task.FromResult(CreateOperationResponse(request.RequestId, _coordinator.StopApp(targetApp)));
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
        if (!HostRegistrationRequest.TryCreate(command, out var registration, out var error)) {
            return Task.FromResult(Response.Fail(request.RequestId, error));
        }

        return Task.FromResult(CreateOperationResponse(request.RequestId, _coordinator.RegisterApp(registration.AppName, registration.Settings)));
    }

    /// <summary>
    /// :unregister - 移除已注册 app
    /// </summary>
    private Task<Response> HandleUnregisterAsync(Request request, ManagementCommand command) {
        var appName = command.TargetApp;
        if (string.IsNullOrWhiteSpace(appName)) {
            return Task.FromResult(Response.Fail(
                request.RequestId,
                """
                Usage: pmux :unregister <app-name> [--stop]
                Example:
                  pmux :unregister counter --stop
                Tip:
                  Add --stop if the app may still be running.
                """.TrimEnd()));
        }

        return Task.FromResult(CreateOperationResponse(request.RequestId, _coordinator.UnregisterApp(appName, command.Flag)));
    }

    private static Response CreateOperationResponse(string requestId, BrokerOperationResult result) {
        return result.Success
            ? Response.Ok(requestId, result.Message)
            : Response.Fail(requestId, result.Message);
    }

    /// <summary>
    /// :help - 显示帮助信息
    /// </summary>
    private Task<Response> HandleHelpAsync(Request request) {
        var sb = new StringBuilder();
        sb.AppendLine("PipeMux Management Commands:");
        sb.AppendLine();
        sb.AppendLine("  :list          List registered apps");
        sb.AppendLine("  :ps            List running processes");
        sb.AppendLine("  :stop <app>    Stop processes for an application");
        sb.AppendLine("  :register <app> <assembly> <entry> [--host-path <pmux-host-path>]");
        sb.AppendLine("                 Register an app hosted by PipeMux.Host");
        sb.AppendLine("  :unregister <app> [--stop]");
        sb.AppendLine("                 Remove app from config (optionally stop running instances)");
        sb.AppendLine("  :help          Show this help message");
        sb.AppendLine();
        AppendFirstTimeSetup(sb);
        sb.AppendLine();
        sb.AppendLine("Application Commands:");
        sb.AppendLine();
        sb.AppendLine("  pmux <app> <args...>    Call an application with arguments");
        sb.AppendLine("  pmux calculator push 10");
        sb.AppendLine("  pmux calculator add");
        sb.AppendLine("  pmux texteditor open file.txt");
        sb.AppendLine();
        sb.AppendLine("Run 'pmux :list' to see current registered apps.");

        return Task.FromResult(Response.Ok(request.RequestId, sb.ToString().TrimEnd()));
    }

    private static void AppendFirstTimeSetup(StringBuilder sb) {
        var configPath = BrokerConnectionDefaults.GetConfigPath();
        var hostExecutableOnPath = IsCommandOnPath("pmux-host");

        sb.AppendLine("First-time setup:");
        sb.AppendLine($"  1. Edit config: {configPath}");
        sb.AppendLine("     Example:");
        sb.AppendLine();
        sb.AppendLine("     [apps.counter]");
        sb.AppendLine($"     command = \"{GetConfigCommandExample(hostExecutableOnPath)}\"");
        sb.AppendLine("     auto_start = false");
        sb.AppendLine("     timeout = 30");
        sb.AppendLine();
        sb.AppendLine("  2. Or register an app now:");
        sb.AppendLine($"     {GetRegisterCommandExample(hostExecutableOnPath)}");
        if (hostExecutableOnPath) {
            sb.AppendLine("     Tip: omit --host-path when pmux-host is already on PATH.");
        }
        else {
            sb.AppendLine("     Tip: add --host-path when pmux-host is not on PATH.");
        }
        sb.AppendLine("  3. Run 'pmux :help' for the command index.");
    }

    private static string GetConfigCommandExample(bool hostExecutableOnPath) {
        const string assemblyPlaceholder = "/absolute/path/to/MyApp.dll";
        const string entryPlaceholder = "MyNamespace.DebugEntries.BuildCounter";

        return hostExecutableOnPath
            ? $"pmux-host {assemblyPlaceholder} {entryPlaceholder}"
            : $"/absolute/path/to/pmux-host {assemblyPlaceholder} {entryPlaceholder}";
    }

    private static string GetRegisterCommandExample(bool hostExecutableOnPath) {
        const string appName = "counter";
        const string assemblyPlaceholder = "/absolute/path/to/MyApp.dll";
        const string entryPlaceholder = "MyNamespace.DebugEntries.BuildCounter";

        return hostExecutableOnPath
            ? $"pmux :register {appName} {assemblyPlaceholder} {entryPlaceholder}"
            : $"pmux :register {appName} {assemblyPlaceholder} {entryPlaceholder} --host-path /absolute/path/to/pmux-host";
    }

    private static bool IsCommandOnPath(string commandName) {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue)) {
            return false;
        }

        string[] candidateFileNames = OperatingSystem.IsWindows()
            ? [commandName, $"{commandName}.exe", $"{commandName}.cmd", $"{commandName}.bat"]
            : [commandName];

        foreach (var segment in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            foreach (var fileName in candidateFileNames) {
                var candidatePath = Path.Combine(segment, fileName);
                if (File.Exists(candidatePath)) {
                    return true;
                }
            }
        }

        return false;
    }
}
