using PipeMux.Shared;
using PipeMux.Shared.Protocol;

namespace PipeMux.Broker;

/// <summary>
/// 协调 Broker 配置、进程生命周期与管理命令，统一线性化边界。
/// </summary>
public sealed class BrokerCoordinator {
    private readonly ProcessRegistry _registry;
    private readonly BrokerConfigStore _configStore;
    private readonly BrokerServiceEnvironmentStore _serviceEnvironmentStore;
    private readonly object _brokerGate = new();
    private readonly Dictionary<string, AssemblyWatcher> _watchers = new(StringComparer.Ordinal);

    public BrokerCoordinator(BrokerConfig config, ProcessRegistry registry, string? configPath = null) {
        _registry = registry;
        _configStore = new BrokerConfigStore(config, configPath);
        _serviceEnvironmentStore = new BrokerServiceEnvironmentStore();
    }

    /// <summary>
    /// 为所有 auto_restart 启用的 app 启动文件监视。
    /// 在 Broker 启动后调用。
    /// </summary>
    public void StartAutoRestartWatchers() {
        lock (_brokerGate) {
            foreach (var (appName, settings) in _configStore.Apps
                         .Where(kv => kv.Value.AutoRestart)
                         .OrderBy(kv => kv.Key, StringComparer.Ordinal)) {
                StartWatcherForApp_NoLock(appName, settings);
            }
        }
    }

    /// <summary>
    /// 停止所有文件监视器。
    /// </summary>
    public void StopAllWatchers() {
        lock (_brokerGate) {
            StopAllWatchers_NoLock();
        }
    }

    private void StartWatcherForApp(string appName, AppSettings settings) {
        lock (_brokerGate) {
            StartWatcherForApp_NoLock(appName, settings);
        }
    }

    private async Task HandleAutoRestart(string appName, AppSettings settings) {
        Console.Error.WriteLine($"[INFO] Auto-restart triggered for: {appName}");

        // 停止当前 watcher（进程停止/启动时会重建）
        AssemblyWatcher? oldWatcher;
        lock (_brokerGate) {
            _watchers.Remove(appName, out oldWatcher);
        }
        oldWatcher?.Dispose();

        try {
            BrokerOperationResult restartResult;
            lock (_brokerGate) {
                var matchingKeys = FindMatchingKeys_NoLock(appName);
                if (matchingKeys.Count > 0) {
                    restartResult = RestartProcessKeys_NoLock(appName, matchingKeys);
                }
                else if (settings.AutoStart) {
                    restartResult = EnsureDefaultInstanceStarted_NoLock(appName);
                }
                else {
                    restartResult = BrokerOperationResult.Ok($"Skipped auto-restart for {appName}: no running instances");
                }
            }

            if (restartResult.Success) {
                Console.Error.WriteLine($"[INFO] Auto-restart handled for {appName}: {restartResult.Message}");
            }
            else {
                Console.Error.WriteLine($"[WARN] Auto-restart failed for {appName}: {restartResult.Message}");
            }
        }
        catch (Exception ex) {
            Console.Error.WriteLine($"[ERROR] Auto-restart error for {appName}: {ex.Message}");
        }

        // 重建 watcher
        StartWatcherForApp(appName, settings);
    }

    public IReadOnlyList<KeyValuePair<string, AppSettings>> SnapshotRegisteredApps() {
        lock (_brokerGate) {
            return _configStore.Apps
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => new KeyValuePair<string, AppSettings>(kv.Key, BrokerConfigStore.CloneAppSettings(kv.Value)))
                .ToList();
        }
    }

    public IReadOnlyList<KeyValuePair<string, AppSettings>> SnapshotAutoStartApps() {
        return SnapshotRegisteredApps()
            .Where(kv => kv.Value.AutoStart)
            .ToList();
    }

    public IReadOnlyList<ActiveProcessInfo> SnapshotActiveProcesses() {
        lock (_brokerGate) {
            var results = new List<ActiveProcessInfo>();
            foreach (var key in _registry.ListActive().OrderBy(k => k, StringComparer.Ordinal)) {
                var process = _registry.Get(key);
                if (process != null) {
                    var keyParts = ProcessInstanceKey.Parse(key);
                    results.Add(new ActiveProcessInfo(
                        key,
                        keyParts.AppName,
                        keyParts.TerminalId,
                        process.ProcessId,
                        process.IsHealthy(),
                        process.StartTime.ToUniversalTime()));
                }
            }
            return results;
        }
    }

    public IReadOnlyList<RegisteredAppStatusInfo> SnapshotRegisteredAppStatuses() {
        lock (_brokerGate) {
            var activeProcesses = SnapshotActiveProcesses_NoLock();
            var processesByApp = activeProcesses
                .GroupBy(process => process.AppName)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

            return _configStore.Apps
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => CreateRegisteredAppStatus_NoLock(kv.Key, kv.Value, processesByApp))
                .ToList();
        }
    }

    public IReadOnlyList<RunningProcessStatusInfo> SnapshotRunningProcessStatuses() {
        lock (_brokerGate) {
            return SnapshotActiveProcesses_NoLock()
                .Select(process => {
                    var assemblyInfo = GetAssemblyFileInfo_NoLock(process.AppName);
                    var assemblyModifiedAfterStart = assemblyInfo is { Exists: true, LastWriteTimeUtc: not null }
                        && assemblyInfo.LastWriteTimeUtc.Value > process.ProcessStartTimeUtc;

                    return new RunningProcessStatusInfo {
                        Process = process,
                        Assembly = assemblyInfo,
                        AssemblyModifiedAfterStart = assemblyModifiedAfterStart
                    };
                })
                .ToList();
        }
    }

    /// <summary>
    /// 关闭单个 process key 对应的进程；用于请求级错误回收。
    /// 与面向 app 名的 <see cref="StopApp"/> 不同，这里只精确匹配一个 key。
    /// </summary>
    public bool CloseProcess(string processKey) {
        lock (_brokerGate) {
            if (_registry.Close(processKey)) {
                Console.Error.WriteLine($"[INFO] Closed process: {processKey}");
                return true;
            }
            return false;
        }
    }

    public ProcessAcquisitionResult AcquireProcess(Request request) {
        return AcquireProcess(request.App, request.TerminalId, request.RequestId);
    }

    public ProcessAcquisitionResult AcquireProcess(string? appName, string? terminalId = null) {
        return AcquireProcess(appName, terminalId, requestId: string.Empty);
    }

    private ProcessAcquisitionResult AcquireProcess(string? appName, string? terminalId, string requestId) {
        lock (_brokerGate) {
            return AcquireProcess_NoLock(appName, terminalId, requestId);
        }
    }

    private ProcessAcquisitionResult AcquireProcess_NoLock(string? appName, string? terminalId, string requestId) {
        if (string.IsNullOrEmpty(appName)) {
            return ProcessAcquisitionResult.Fail(Response.Fail(requestId, "App name is required"));
        }

        if (!_configStore.Apps.TryGetValue(appName, out var configuredSettings)) {
            return ProcessAcquisitionResult.Fail(Response.Fail(
                requestId,
                $"Unknown app: {appName}\nRun `pmux :list` to see registered apps."));
        }

        var processKey = ProcessInstanceKey.Build(appName, terminalId);
        var process = _registry.Get(processKey);
        var isNewProcess = false;

        if (process == null || process.HasExited || !process.IsHealthy()) {
            try {
                Console.Error.WriteLine($"[INFO] Starting new process for {appName} (key: {processKey})");
                process = _registry.Start(processKey, configuredSettings.Command);
                isNewProcess = true;
            }
            catch (Exception ex) {
                Console.Error.WriteLine($"[ERROR] Failed to start {appName}: {ex.Message}");
                return ProcessAcquisitionResult.Fail(Response.Fail(requestId, $"Failed to start app: {ex.Message}"));
            }
        }
        else {
            Console.Error.WriteLine($"[INFO] Reusing existing process for key: {processKey}, PID: {process.ProcessId}");
        }

        return ProcessAcquisitionResult.Ok(process, configuredSettings, processKey, isNewProcess);
    }

    public BrokerOperationResult RegisterApp(string appName, AppSettings settings) {
        lock (_brokerGate) {
            if (TryGetRegisterConflict_NoLock(appName, out var conflict)) {
                return conflict;
            }

            if (!_configStore.TryRegisterApp(appName, settings, out var message)) {
                return BrokerOperationResult.Fail(message);
            }

            if (settings.AutoRestart) {
                StartWatcherForApp(appName, settings);
            }

            return BrokerOperationResult.Ok(message);
        }
    }

    public BrokerOperationResult CopyEnvironmentToBroker(
        IReadOnlyList<string> requestedNames,
        IReadOnlyDictionary<string, string>? copiedValues
    ) {
        lock (_brokerGate) {
            try {
                const string brokerServiceCommandName = "pipemux-broker";
                var result = _serviceEnvironmentStore.CopyFromCliEnvironment(requestedNames, copiedValues);
                var messageLines = new List<string> {
                    $"Copied {result.CopiedNames.Count} environment variable(s) to broker environment: {string.Join(", ", result.CopiedNames)}",
                    $"Environment file: {result.EnvironmentFilePath}"
                };

                if (result.MissingNames.Count > 0) {
                    messageLines.Add($"Missing in current CLI environment: {string.Join(", ", result.MissingNames)}");
                }

                if (result.DropInCreated) {
                    messageLines.Add($"Systemd drop-in created: {result.DropInFilePath}");
                    messageLines.Add($"Run `systemctl --user daemon-reload && systemctl --user restart {brokerServiceCommandName}` to apply changes.");
                }
                else {
                    messageLines.Add($"Run `systemctl --user restart {brokerServiceCommandName}` to apply changes.");
                }

                return BrokerOperationResult.Ok(string.Join(Environment.NewLine, messageLines));
            }
            catch (Exception ex) {
                return BrokerOperationResult.Fail($"Failed to update broker environment: {ex.Message}");
            }
        }
    }

    public BrokerOperationResult ReloadConfig() {
        lock (_brokerGate) {
            if (!_configStore.TryReadConfigFromDisk(out var reloadedConfig, out var error)) {
                return BrokerOperationResult.Fail($"Failed to reload broker config: {error}");
            }

            var previousBroker = BrokerConfigStore.CloneBrokerSettings(_configStore.Broker);
            var previousApps = _configStore.Apps
                .ToDictionary(kv => kv.Key, kv => BrokerConfigStore.CloneAppSettings(kv.Value), StringComparer.Ordinal);
            var runningProcessesBeforeReload = SnapshotActiveProcesses_NoLock();

            StopAllWatchers_NoLock();
            _configStore.ApplyReloadedConfig(reloadedConfig);

            foreach (var (appName, settings) in _configStore.Apps
                         .Where(kv => kv.Value.AutoRestart)
                         .OrderBy(kv => kv.Key, StringComparer.Ordinal)) {
                StartWatcherForApp_NoLock(appName, settings);
            }

            var autoStartedApps = new List<string>();
            var autoStartFailures = new List<string>();

            foreach (var (appName, settings) in _configStore.Apps
                         .Where(kv => kv.Value.AutoStart)
                         .OrderBy(kv => kv.Key, StringComparer.Ordinal)) {
                if (FindMatchingKeys_NoLock(appName).Count > 0) {
                    continue;
                }

                var result = EnsureDefaultInstanceStarted_NoLock(appName);
                if (result.Success) {
                    autoStartedApps.Add(appName);
                }
                else {
                    autoStartFailures.Add($"{appName}: {result.Message}");
                }
            }

            var removedAppsWithRunningProcesses = previousApps.Keys
                .Except(_configStore.Apps.Keys, StringComparer.Ordinal)
                .Where(appName => runningProcessesBeforeReload.Any(process => process.AppName == appName))
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToList();

            var changedAppsWithRunningProcesses = _configStore.Apps
                .Where(kv => previousApps.TryGetValue(kv.Key, out var previous)
                    && !AppSettingsEqual(previous, kv.Value)
                    && runningProcessesBeforeReload.Any(process => process.AppName == kv.Key))
                .Select(kv => kv.Key)
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToList();

            var messageLines = new List<string> {
                $"Reloaded broker config: {_configStore.Apps.Count} app(s) registered."
            };

            if (autoStartedApps.Count > 0) {
                messageLines.Add($"Auto-started after reload: {string.Join(", ", autoStartedApps)}");
            }

            if (autoStartFailures.Count > 0) {
                messageLines.Add($"Auto-start failures after reload: {string.Join("; ", autoStartFailures)}");
            }

            if (removedAppsWithRunningProcesses.Count > 0) {
                messageLines.Add(
                    $"Removed from config but still running: {string.Join(", ", removedAppsWithRunningProcesses)}. Run `pmux :stop <app>` if you want to terminate them.");
            }

            if (changedAppsWithRunningProcesses.Count > 0) {
                messageLines.Add(
                    $"Updated config detected for running apps: {string.Join(", ", changedAppsWithRunningProcesses)}. Existing processes were kept; run `pmux :restart <app>` to apply launcher/assembly changes.");
            }

            if (!BrokerSettingsEqual(previousBroker, _configStore.Broker)) {
                messageLines.Add(
                    "Broker endpoint settings changed in memory, but the current listening socket/pipe will not move until the broker process is fully restarted.");
            }

            return BrokerOperationResult.Ok(string.Join("\n", messageLines));
        }
    }

    public bool TryGetRegisterConflict(string appName, out BrokerOperationResult conflict) {
        lock (_brokerGate) {
            return TryGetRegisterConflict_NoLock(appName, out conflict);
        }
    }

    public BrokerOperationResult StopApp(string targetApp) {
        lock (_brokerGate) {
            var matchingKeys = FindMatchingKeys_NoLock(targetApp);
            if (matchingKeys.Count == 0) {
                return BrokerOperationResult.Fail($"No running process found for: {targetApp}");
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

            return BrokerOperationResult.Ok(message);
        }
    }

    public BrokerOperationResult RestartRunningInstances(string appName) {
        lock (_brokerGate) {
            if (string.IsNullOrEmpty(appName)) {
                return BrokerOperationResult.Fail("App name is required");
            }

            if (!_configStore.Apps.ContainsKey(appName)) {
                return BrokerOperationResult.Fail($"Unknown app: {appName}");
            }

            var matchingKeys = FindMatchingKeys_NoLock(appName);
            if (matchingKeys.Count == 0) {
                return BrokerOperationResult.Fail($"No running process found for: {appName}");
            }

            return RestartProcessKeys_NoLock(appName, matchingKeys);
        }
    }

    public BrokerOperationResult UnregisterApp(string appName, bool stopRunningProcesses) {
        lock (_brokerGate) {
            var activeKeys = FindMatchingKeys_NoLock(appName);
            if (activeKeys.Count > 0 && !stopRunningProcesses) {
                return BrokerOperationResult.Fail(
                    $"App '{appName}' has {activeKeys.Count} running process(es). Use :unregister {appName} --stop or run :stop {appName} first"
                );
            }

            // 停止文件监视
            if (_watchers.Remove(appName, out var watcher)) {
                watcher.Dispose();
            }

            if (stopRunningProcesses) {
                foreach (var key in activeKeys) {
                    _registry.Close(key);
                    Console.Error.WriteLine($"[INFO] Stopped process during unregister: {key}");
                }
            }

            if (!_configStore.TryUnregister(appName, out var removedCommand, out var message)) {
                return BrokerOperationResult.Fail(message);
            }

            var details = stopRunningProcesses && activeKeys.Count > 0
                ? $"{message} (stopped {activeKeys.Count} process(es))"
                : message;

            if (!string.IsNullOrWhiteSpace(removedCommand)) {
                details += $"\nRemoved command: {removedCommand}";
            }

            return BrokerOperationResult.Ok(details);
        }
    }

    private IReadOnlyList<ActiveProcessInfo> SnapshotActiveProcesses_NoLock() {
        var results = new List<ActiveProcessInfo>();
        foreach (var key in _registry.ListActive().OrderBy(k => k, StringComparer.Ordinal)) {
            var process = _registry.Get(key);
            if (process == null) {
                continue;
            }

            var keyParts = ProcessInstanceKey.Parse(key);
            results.Add(new ActiveProcessInfo(
                key,
                keyParts.AppName,
                keyParts.TerminalId,
                process.ProcessId,
                process.IsHealthy(),
                process.StartTime.ToUniversalTime()));
        }

        return results;
    }

    private AssemblyFileInfo? GetAssemblyFileInfo_NoLock(string appName) {
        if (!_configStore.Apps.TryGetValue(appName, out var settings)) {
            return null;
        }

        if (string.IsNullOrWhiteSpace(settings.AssemblyPath) || !File.Exists(settings.AssemblyPath)) {
            return new AssemblyFileInfo { Path = settings.AssemblyPath, Exists = false };
        }

        return new AssemblyFileInfo {
            Path = settings.AssemblyPath,
            Exists = true,
            LastWriteTimeUtc = File.GetLastWriteTimeUtc(settings.AssemblyPath)
        };
    }

    private RegisteredAppStatusInfo CreateRegisteredAppStatus_NoLock(
        string appName,
        AppSettings settings,
        IReadOnlyDictionary<string, List<ActiveProcessInfo>> processesByApp
    ) {
        processesByApp.TryGetValue(appName, out var processes);
        processes ??= [];

        var assemblyInfo = GetAssemblyFileInfo_NoLock(appName);
        var assemblyModifiedAfterStart = assemblyInfo is { Exists: true, LastWriteTimeUtc: not null }
            && processes.Any(process => assemblyInfo.LastWriteTimeUtc.Value > process.ProcessStartTimeUtc);

        return new RegisteredAppStatusInfo {
            AppName = appName,
            Settings = BrokerConfigStore.CloneAppSettings(settings),
            Assembly = assemblyInfo,
            Processes = processes,
            AssemblyModifiedAfterStart = assemblyModifiedAfterStart
        };
    }

    private List<string> FindMatchingKeys_NoLock(string appName) {
        return _registry.ListActive()
            .Where(key => key == appName || key.StartsWith($"{appName}:", StringComparison.Ordinal))
            .ToList();
    }

    private void StartWatcherForApp_NoLock(string appName, AppSettings settings) {
        if (string.IsNullOrWhiteSpace(settings.AssemblyPath)) {
            Console.Error.WriteLine($"[WARN] Cannot determine assembly path for auto_restart app '{appName}', skipping watch");
            return;
        }

        if (_watchers.Remove(appName, out var existingWatcher)) {
            existingWatcher.Dispose();
        }

        var watcher = new AssemblyWatcher(settings.AssemblyPath, async () => {
            await HandleAutoRestart(appName, settings);
        });

        _watchers[appName] = watcher;
        watcher.Start();
    }

    private void StopAllWatchers_NoLock() {
        foreach (var (_, watcher) in _watchers) {
            watcher.Dispose();
        }
        _watchers.Clear();
    }

    private BrokerOperationResult RestartProcessKeys_NoLock(string appName, IReadOnlyList<string> matchingKeys) {
        foreach (var key in matchingKeys) {
            if (_registry.Close(key)) {
                Console.Error.WriteLine($"[INFO] Stopped process during restart: {key}");
            }
        }

        var restartedProcesses = new List<AppProcess>(matchingKeys.Count);
        foreach (var key in matchingKeys) {
            var terminalId = ProcessInstanceKey.Parse(key).TerminalId;
            var acquisition = AcquireProcess_NoLock(appName, terminalId, requestId: string.Empty);
            if (!acquisition.Success) {
                var detail = acquisition.Error?.Error ?? $"Failed to restart {key}";
                return BrokerOperationResult.Fail(
                    $"Stopped {matchingKeys.Count} process(es) for {appName}, but failed to restart '{key}': {detail}");
            }

            restartedProcesses.Add(acquisition.Process!);
        }

        if (restartedProcesses.Count == 1) {
            return BrokerOperationResult.Ok($"Restarted {matchingKeys[0]} (PID: {restartedProcesses[0].ProcessId})");
        }

        return BrokerOperationResult.Ok($"Restarted {restartedProcesses.Count} processes for: {appName}");
    }

    private BrokerOperationResult EnsureDefaultInstanceStarted_NoLock(string appName) {
        var acquisition = AcquireProcess_NoLock(appName, terminalId: null, requestId: string.Empty);
        if (!acquisition.Success) {
            return BrokerOperationResult.Fail(acquisition.Error?.Error ?? $"Failed to start {appName}");
        }

        return BrokerOperationResult.Ok($"Started {appName} (PID: {acquisition.Process!.ProcessId})");
    }

    private bool TryGetRegisterConflict_NoLock(string appName, out BrokerOperationResult conflict) {
        if (!_configStore.Apps.ContainsKey(appName)) {
            conflict = null!;
            return false;
        }

        var runningKeys = FindMatchingKeys_NoLock(appName);
        var hint = "App already registered: " + appName;
        if (runningKeys.Count > 0) {
            hint += $"\n- If you rebuilt the DLL and want the new code loaded, run: pmux :stop {appName}";
            hint += $"\n- To change registration (different assembly/entry/host), first run: pmux :unregister {appName} --stop";
        }
        else {
            hint += $"\n- To change registration, first run: pmux :unregister {appName}";
        }

        conflict = BrokerOperationResult.Fail(hint);
        return true;
    }

    private static bool AppSettingsEqual(AppSettings left, AppSettings right) {
        return string.Equals(left.Command, right.Command, StringComparison.Ordinal)
            && string.Equals(left.AssemblyPath, right.AssemblyPath, StringComparison.Ordinal)
            && left.AutoStart == right.AutoStart
            && left.AutoRestart == right.AutoRestart
            && left.Timeout == right.Timeout;
    }

    private static bool BrokerSettingsEqual(BrokerConnectionSettings left, BrokerConnectionSettings right) {
        return string.Equals(left.SocketPath, right.SocketPath, StringComparison.Ordinal)
            && string.Equals(left.PipeName, right.PipeName, StringComparison.Ordinal);
    }
}

public sealed record ActiveProcessInfo(
    string Key,
    string AppName,
    string? TerminalId,
    int ProcessId,
    bool IsHealthy,
    DateTime ProcessStartTimeUtc);

public sealed class AssemblyFileInfo {
    public required string? Path { get; init; }
    public required bool Exists { get; init; }
    public DateTime? LastWriteTimeUtc { get; init; }
}

public sealed class RegisteredAppStatusInfo {
    public required string AppName { get; init; }
    public required AppSettings Settings { get; init; }
    public required AssemblyFileInfo? Assembly { get; init; }
    public required IReadOnlyList<ActiveProcessInfo> Processes { get; init; }
    public required bool AssemblyModifiedAfterStart { get; init; }
}

public sealed class RunningProcessStatusInfo {
    public required ActiveProcessInfo Process { get; init; }
    public required AssemblyFileInfo? Assembly { get; init; }
    public required bool AssemblyModifiedAfterStart { get; init; }
}

public sealed class BrokerOperationResult {
    public required bool Success { get; init; }
    public required string Message { get; init; }

    public static BrokerOperationResult Ok(string message) {
        return new BrokerOperationResult {
            Success = true,
            Message = message
        };
    }

    public static BrokerOperationResult Fail(string message) {
        return new BrokerOperationResult {
            Success = false,
            Message = message
        };
    }
}

public sealed class ProcessAcquisitionResult {
    public required bool Success { get; init; }
    public Response? Error { get; init; }
    public AppProcess? Process { get; init; }
    public AppSettings? Settings { get; init; }
    public string? ProcessKey { get; init; }
    public bool IsNewProcess { get; init; }

    public static ProcessAcquisitionResult Ok(AppProcess process, AppSettings settings, string processKey, bool isNewProcess) {
        return new ProcessAcquisitionResult {
            Success = true,
            Process = process,
            Settings = settings,
            ProcessKey = processKey,
            IsNewProcess = isNewProcess
        };
    }

    public static ProcessAcquisitionResult Fail(Response error) {
        return new ProcessAcquisitionResult {
            Success = false,
            Error = error
        };
    }
}
