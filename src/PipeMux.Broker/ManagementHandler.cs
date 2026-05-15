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
            foreach (var status in _coordinator.SnapshotRegisteredAppStatuses()) {
                var name = status.AppName;
                var settings = status.Settings;
                var tags = new List<string>();
                if (settings.AutoStart) tags.Add("auto-start");
                if (settings.AutoRestart) tags.Add("auto-restart");
                var tagStr = tags.Count > 0 ? $" [{string.Join(", ", tags)}]" : "";

                sb.AppendLine($"  {name}{tagStr}");
                sb.AppendLine($"    Command: {settings.Command}");
                sb.AppendLine($"    Timeout: {settings.Timeout}s");

                // 显示程序集文件信息
                var assemblyInfo = status.Assembly;
                if (assemblyInfo != null) {
                    if (assemblyInfo.Exists && assemblyInfo.LastWriteTimeUtc.HasValue) {
                        var age = FormatDuration(DateTime.UtcNow - assemblyInfo.LastWriteTimeUtc.Value);
                        sb.AppendLine($"    Assembly: {assemblyInfo.Path} (modified {age} ago)");
                    }
                    else if (assemblyInfo.Path != null) {
                        sb.AppendLine($"    Assembly: {assemblyInfo.Path} (file not found)");
                    }
                }

                // 显示运行状态与一致性检查
                if (status.Processes.Count > 0) {
                    var processes = status.Processes;
                    if (processes.Count == 1) {
                        var process = processes[0];
                        var uptime = FormatDuration(DateTime.UtcNow - process.ProcessStartTimeUtc);
                        var health = process.IsHealthy ? "healthy" : "unhealthy";
                        sb.Append($"    Status: running (PID: {process.ProcessId}, uptime: {uptime}, {health})");
                    }
                    else {
                        var oldestStart = processes.Min(p => p.ProcessStartTimeUtc);
                        var uptime = FormatDuration(DateTime.UtcNow - oldestStart);
                        var healthyCount = processes.Count(p => p.IsHealthy);
                        var unhealthyCount = processes.Count - healthyCount;
                        var healthSummary = unhealthyCount == 0
                            ? $"{healthyCount} healthy"
                            : $"{healthyCount} healthy, {unhealthyCount} unhealthy";
                        sb.Append($"    Status: running ({processes.Count} instances, oldest uptime: {uptime}, {healthSummary})");
                    }

                    if (status.AssemblyModifiedAfterStart) {
                        sb.AppendLine();
                        sb.AppendLine($"    *** Assembly was modified after one or more process instances started. Run `pmux :restart {name}` to load changes. ***");
                    }
                    else {
                        sb.AppendLine();
                    }
                }
                else {
                    sb.AppendLine("    Status: not running");
                }
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
            foreach (var status in _coordinator.SnapshotRunningProcessStatuses()) {
                var process = status.Process;
                var health = process.IsHealthy ? "healthy" : "unhealthy";
                var uptime = FormatDuration(DateTime.UtcNow - process.ProcessStartTimeUtc);
                sb.AppendLine($"  {process.Key}");
                sb.AppendLine($"    PID: {process.ProcessId}, Status: {health}, Uptime: {uptime}");

                // 获取对应 app 的程序集信息进行一致性检查
                var appName = process.AppName;
                var assemblyInfo = status.Assembly;
                if (assemblyInfo is { Exists: true, LastWriteTimeUtc: not null }) {
                    var age = FormatDuration(DateTime.UtcNow - assemblyInfo.LastWriteTimeUtc.Value);
                    sb.Append($"    Assembly: {assemblyInfo.Path} (modified {age} ago)");

                    if (status.AssemblyModifiedAfterStart) {
                        sb.AppendLine();
                        sb.AppendLine($"    *** Assembly was modified after process start. Run `pmux :restart {appName}` to load changes. ***");
                    }
                    else {
                        sb.AppendLine();
                    }
                }
            }
        }

        return Task.FromResult(Response.Ok(request.RequestId, sb.ToString().TrimEnd()));
    }

    /// <summary>
    /// 将 TimeSpan 格式化为人类可读的时长字符串。
    /// </summary>
    private static string FormatDuration(TimeSpan duration) {
        if (duration.TotalSeconds < 60) return $"{(int)duration.TotalSeconds}s";
        if (duration.TotalMinutes < 60) return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
        if (duration.TotalHours < 24) return $"{(int)duration.TotalHours}h {duration.Minutes}m";
        return $"{(int)duration.TotalDays}d {duration.Hours}h";
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
    /// :restart - 重启指定应用
    /// </summary>
    private Task<Response> HandleRestartAsync(Request request, string? targetApp) {
        if (string.IsNullOrEmpty(targetApp)) {
            return Task.FromResult(Response.Fail(
                request.RequestId,
                """
                Usage: pmux :restart <app-name>
                Example:
                  pmux :restart counter
                Tip:
                  Run 'pmux :list' to see registered apps first.
                """.TrimEnd()));
        }

        return Task.FromResult(CreateOperationResponse(request.RequestId, _coordinator.RestartRunningInstances(targetApp)));
    }

    /// <summary>
    /// :register - 注册一个由 PipeMux.Host 托管的 app
    /// </summary>
    private Task<Response> HandleRegisterAsync(Request request, ManagementCommand command) {
        if (!string.IsNullOrWhiteSpace(command.TargetApp)
            && !string.IsNullOrWhiteSpace(command.TargetAssemblyPath)
            && !string.IsNullOrWhiteSpace(command.TargetMethodName)
            && _coordinator.TryGetRegisterConflict(command.TargetApp, out var conflict)) {
            return Task.FromResult(CreateOperationResponse(request.RequestId, conflict));
        }

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
        sb.AppendLine("  :restart <app> Restart running instances for an application");
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
        var hostResolution = HostRegistrationRequest.ResolveHostExecutable();

        sb.AppendLine("First-time setup:");
        sb.AppendLine($"  1. Edit config: {configPath}");
        sb.AppendLine("     Example:");
        sb.AppendLine();
        sb.AppendLine("     [apps.counter]");
        sb.AppendLine($"     command = \"{GetConfigCommandExample(hostResolution.SuggestedConfigCommandHost)}\"");
        sb.AppendLine("     assembly_path = \"/absolute/path/to/MyApp.dll\"");
        sb.AppendLine("     auto_start = false");
        sb.AppendLine("     timeout = 30");
        sb.AppendLine();
        sb.AppendLine("  2. Or register an app now:");
        sb.AppendLine($"     {GetRegisterCommandExample(hostResolution.CanAutoResolveForRegister)}");
        if (hostResolution.CanAutoResolveForRegister) {
            sb.AppendLine("     Tip: --host-path is auto-resolved for :register; only pass it for a custom location.");
        }
        else {
            sb.AppendLine("     Tip: pass --host-path to point at your PipeMux.Host build.");
        }
        sb.AppendLine("  3. Run 'pmux :help' for the command index.");
    }

    private static string GetConfigCommandExample(string hostExecutable) {
        const string assemblyPlaceholder = "/absolute/path/to/MyApp.dll";
        const string entryPlaceholder = "MyNamespace.DebugEntries.BuildCounter";

        return $"{hostExecutable} {assemblyPlaceholder} {entryPlaceholder}";
    }

    private static string GetRegisterCommandExample(bool canRegisterWithoutHostPath) {
        const string appName = "counter";
        const string assemblyPlaceholder = "/absolute/path/to/MyApp.dll";
        const string entryPlaceholder = "MyNamespace.DebugEntries.BuildCounter";

        return canRegisterWithoutHostPath
            ? $"pmux :register {appName} {assemblyPlaceholder} {entryPlaceholder}"
            : $"pmux :register {appName} {assemblyPlaceholder} {entryPlaceholder} --host-path /absolute/path/to/pmux-host";
    }
}
