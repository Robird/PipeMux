using PipeMux.Shared;
using Tomlyn;

namespace PipeMux.Broker;

/// <summary>
/// 配置文件加载器
/// </summary>
public static class ConfigLoader {
    /// <summary>
    /// 加载配置 (从 ~/.config/pipemux/broker.toml 或默认配置)
    /// </summary>
    public static BrokerConfig Load() {
        var configPath = GetConfigPath();

        if (File.Exists(configPath)) {
            var toml = File.ReadAllText(configPath);
            return Toml.ToModel<BrokerConfig>(toml);
        }

        // P0 Fix: Warn when config file is missing
        Console.Error.WriteLine($"[WARN] Config file not found: {configPath}");
        Console.Error.WriteLine("[INFO] Using default configuration");
        
        // 返回默认配置
        return CreateDefaultConfig();
    }

    private static string GetConfigPath() {
        // P0 Fix: Use correct cross-platform path ~/.config/pipemux/broker.toml
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(homeDir, ".config", "pipemux", "broker.toml");
    }

    private static BrokerConfig CreateDefaultConfig() {
        return new BrokerConfig {
            Broker = new BrokerConnectionSettings {
                SocketPath = null // 使用默认
            },
            Apps = new Dictionary<string, AppSettings>()
        };
    }
}
