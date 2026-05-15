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
    public static BrokerConfig Load(string? configPath = null) {
        configPath ??= BrokerConnectionDefaults.GetConfigPath();

        if (File.Exists(configPath)) {
            try {
                var toml = File.ReadAllText(configPath);
                return BrokerConfigTomlCodec.Deserialize(toml);
            }
            catch (TomlException ex) {
                throw new InvalidOperationException(
                    $"Failed to parse broker config '{configPath}': {ex.Message}{Environment.NewLine}" +
                    "TOML strings should look like assembly_path = \"/absolute/path/to/MyApp.dll\" and must not be written as \\\"...\\\".",
                    ex);
            }
            catch (Exception ex) {
                throw new InvalidOperationException($"Failed to load broker config '{configPath}': {ex.Message}", ex);
            }
        }

        // P0 Fix: Warn when config file is missing
        Console.Error.WriteLine($"[WARN] Config file not found: {configPath}");
        Console.Error.WriteLine("[INFO] Using default configuration");
        
        // 返回默认配置
        return BrokerConfigTomlCodec.CreateDefault();
    }
}
