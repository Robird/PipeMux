using PipeMux.Shared;

namespace PipeMux.Broker;

/// <summary>
/// 配置文件加载器
/// </summary>
public static class ConfigLoader {
    /// <summary>
    /// 加载配置 (从 ~/.config/pipemux/broker.toml 或默认配置)
    /// </summary>
    public static BrokerConfig Load() {
        var configPath = BrokerConnectionDefaults.GetConfigPath();

        if (File.Exists(configPath)) {
            var toml = File.ReadAllText(configPath);
            return BrokerConfigTomlCodec.Deserialize(toml);
        }

        // P0 Fix: Warn when config file is missing
        Console.Error.WriteLine($"[WARN] Config file not found: {configPath}");
        Console.Error.WriteLine("[INFO] Using default configuration");
        
        // 返回默认配置
        return BrokerConfigTomlCodec.CreateDefault();
    }
}
