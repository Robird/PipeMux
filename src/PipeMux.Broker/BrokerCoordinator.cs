using PipeMux.Shared.Protocol;

namespace PipeMux.Broker;

/// <summary>
/// 协调 Broker 配置、进程生命周期与管理命令，统一线性化边界。
/// </summary>
public sealed class BrokerCoordinator {
    private readonly ProcessRegistry _registry;
    private readonly BrokerConfigStore _configStore;
    private readonly object _brokerGate = new();

    public BrokerCoordinator(BrokerConfig config, ProcessRegistry registry, string? configPath = null) {
        _registry = registry;
        _configStore = new BrokerConfigStore(config, configPath);
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
                    results.Add(new ActiveProcessInfo(key, process.ProcessId, process.IsHealthy()));
                }
            }
            return results;
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
        lock (_brokerGate) {
            if (string.IsNullOrEmpty(request.App)) {
                return ProcessAcquisitionResult.Fail(Response.Fail(request.RequestId, "App name is required"));
            }

            if (!_configStore.Apps.TryGetValue(request.App, out var configuredSettings)) {
                return ProcessAcquisitionResult.Fail(Response.Fail(request.RequestId, $"Unknown app: {request.App}"));
            }

            var processKey = BuildProcessKey(request.App, request.TerminalId);
            var process = _registry.Get(processKey);
            var isNewProcess = false;

            if (process == null || process.HasExited || !process.IsHealthy()) {
                try {
                    Console.Error.WriteLine($"[INFO] Starting new process for {request.App} (key: {processKey})");
                    process = _registry.Start(processKey, configuredSettings.Command);
                    isNewProcess = true;
                }
                catch (Exception ex) {
                    Console.Error.WriteLine($"[ERROR] Failed to start {request.App}: {ex.Message}");
                    return ProcessAcquisitionResult.Fail(Response.Fail(request.RequestId, $"Failed to start app: {ex.Message}"));
                }
            }
            else {
                Console.Error.WriteLine($"[INFO] Reusing existing process for key: {processKey}, PID: {process.ProcessId}");
            }

            return ProcessAcquisitionResult.Ok(process, configuredSettings, processKey, isNewProcess);
        }
    }

    public BrokerOperationResult RegisterHostApp(string appName, string assemblyPath, string methodName, string? hostPath) {
        lock (_brokerGate) {
            return _configStore.TryRegisterHostApp(appName, assemblyPath, methodName, hostPath, out var message)
                ? BrokerOperationResult.Ok(message)
                : BrokerOperationResult.Fail(message);
        }
    }

    public BrokerOperationResult StopApp(string targetApp) {
        lock (_brokerGate) {
            var matchingKeys = FindMatchingKeys(targetApp);
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

    public BrokerOperationResult UnregisterApp(string appName, bool stopRunningProcesses) {
        lock (_brokerGate) {
            var activeKeys = FindMatchingKeys(appName);
            if (activeKeys.Count > 0 && !stopRunningProcesses) {
                return BrokerOperationResult.Fail(
                    $"App '{appName}' has {activeKeys.Count} running process(es). Use :unregister {appName} --stop or run :stop {appName} first"
                );
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

    private List<string> FindMatchingKeys(string appName) {
        return _registry.ListActive()
            .Where(key => key == appName || key.StartsWith($"{appName}:", StringComparison.Ordinal))
            .ToList();
    }

    private static string BuildProcessKey(string appName, string? terminalId) {
        return !string.IsNullOrEmpty(terminalId)
            ? $"{appName}:{terminalId}"
            : appName;
    }
}

public sealed record ActiveProcessInfo(string Key, int ProcessId, bool IsHealthy);

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
