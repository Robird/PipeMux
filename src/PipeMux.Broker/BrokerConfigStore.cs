using System.Text;
using PipeMux.Shared;

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

    public bool TryRegisterApp(string appName, AppSettings settings, out string message) {
        if (_config.Apps.ContainsKey(appName)) {
            message = $"App already registered: {appName}";
            return false;
        }

        var updatedApps = CloneApps(_config.Apps);
        updatedApps[appName] = CloneAppSettings(settings);

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
            SaveModel(BrokerConfigTomlCodec.CreateWithApps(_config.Broker, apps));

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

        var toml = BrokerConfigTomlCodec.Serialize(configModel);
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
}
