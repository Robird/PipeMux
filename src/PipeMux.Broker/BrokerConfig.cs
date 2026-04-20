using PipeMux.Shared;

namespace PipeMux.Broker;

/// <summary>
/// Broker 配置模型 (对应 TOML 文件)
/// </summary>
public sealed class BrokerConfig {
    public BrokerConnectionSettings Broker { get; set; } = new();
    public Dictionary<string, AppSettings> Apps { get; set; } = new();
}

public sealed class AppSettings {
    public required string Command { get; set; }
    public bool AutoStart { get; set; }
    public int Timeout { get; set; } = 30; // 秒
}
