using System.Text;
using PipeMux.Shared;
using Tomlyn;

namespace PipeMux.Broker;

/// <summary>
/// Broker 配置的内存视图与原子落盘。
/// 本类不负责加锁；调用方（<see cref="BrokerCoordinator"/>）持有 broker gate 后串行调用即可。
/// </summary>
public sealed class BrokerConfigStore {
    private readonly BrokerConfig _config;
    private readonly string _configPath;

    public BrokerConfigStore(BrokerConfig config, string? configPath = null) {
        _config = config;
        _configPath = configPath ?? BrokerConnectionDefaults.GetConfigPath();
    }

    /// <summary>当前已注册 app 的只读视图（不复制；调用方需在 gate 内访问）。</summary>
    public IReadOnlyDictionary<string, AppSettings> Apps => _config.Apps;

    public bool TryRegisterHostApp(
        string appName,
        string assemblyPath,
        string methodName,
        string? hostPath,
        out string message
    ) {
        if (_config.Apps.ContainsKey(appName)) {
            message = $"App already registered: {appName}";
            return false;
        }

        var effectiveHostPath = string.IsNullOrWhiteSpace(hostPath) ? "pipemux-host" : hostPath;
        var updatedApps = CloneApps(_config.Apps);
        updatedApps[appName] = new AppSettings {
            Command = BuildHostCommand(effectiveHostPath, assemblyPath, methodName),
            AutoStart = false,
            Timeout = 30
        };

        if (!TrySaveApps(updatedApps, out var error)) {
            message = $"Failed to save broker config: {error}";
            return false;
        }

        _config.Apps = updatedApps;
        message = $"Registered app '{appName}'";
        return true;
    }

    public bool TryUnregister(string appName, out string? removedCommand, out string message) {
        if (!_config.Apps.TryGetValue(appName, out var existing)) {
            removedCommand = null;
            message = $"App is not registered: {appName}";
            return false;
        }

        var updatedApps = CloneApps(_config.Apps);
        updatedApps.Remove(appName);

        if (!TrySaveApps(updatedApps, out var error)) {
            removedCommand = null;
            message = $"Failed to save broker config: {error}";
            return false;
        }

        _config.Apps = updatedApps;
        removedCommand = existing.Command;
        message = $"Unregistered app '{appName}'";
        return true;
    }

    internal static AppSettings CloneAppSettings(AppSettings settings) {
        return new AppSettings {
            Command = settings.Command,
            AutoStart = settings.AutoStart,
            Timeout = settings.Timeout
        };
    }

    private static Dictionary<string, AppSettings> CloneApps(IReadOnlyDictionary<string, AppSettings> apps) {
        return apps.ToDictionary(kv => kv.Key, kv => CloneAppSettings(kv.Value), StringComparer.Ordinal);
    }

    private bool TrySaveApps(Dictionary<string, AppSettings> apps, out string error) {
        try {
            SaveModel(new BrokerConfig {
                Broker = new BrokerConnectionSettings {
                    SocketPath = _config.Broker.SocketPath,
                    PipeName = _config.Broker.PipeName
                },
                Apps = apps
            });

            error = string.Empty;
            return true;
        }
        catch (Exception ex) {
            error = ex.Message;
            return false;
        }
    }

    private void SaveModel(BrokerConfig configModel) {
        var directory = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrWhiteSpace(directory)) {
            Directory.CreateDirectory(directory);
        }

        var toml = Toml.FromModel(configModel);
        var tempFile = Path.Combine(directory ?? ".", $".{Path.GetFileName(_configPath)}.{Guid.NewGuid():N}.tmp");

        try {
            File.WriteAllText(tempFile, toml, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            if (File.Exists(_configPath)) {
                File.Replace(tempFile, _configPath, destinationBackupFileName: null);
            }
            else {
                File.Move(tempFile, _configPath);
            }
        }
        finally {
            if (File.Exists(tempFile)) {
                File.Delete(tempFile);
            }
        }
    }

    private static string BuildHostCommand(string hostPath, string assemblyPath, string methodName) {
        var normalizedHostPath = NormalizeExecutable(hostPath);
        var expandedAssembly = PathHelper.ExpandPath(assemblyPath);
        var absoluteAssembly = Path.GetFullPath(expandedAssembly);

        return string.Join(" ", [
            EscapeArgument(normalizedHostPath),
            EscapeArgument(absoluteAssembly),
            EscapeArgument(methodName)
        ]);
    }

    private static string NormalizeExecutable(string executable) {
        var expandedExecutable = PathHelper.ExpandPath(executable);
        if (Path.IsPathRooted(expandedExecutable) || executable.Contains(Path.DirectorySeparatorChar) || executable.Contains(Path.AltDirectorySeparatorChar)) {
            return Path.GetFullPath(expandedExecutable);
        }

        return expandedExecutable;
    }

    private static string EscapeArgument(string value) {
        return $"\"{value.Replace("\"", "\\\"")}\"";
    }
}
