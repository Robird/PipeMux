using PipeMux.Shared;

namespace PipeMux.Broker;

/// <summary>
/// Broker 配置模型 (对应 TOML 文件)
/// </summary>
public sealed class BrokerConfig {
    public BrokerConnectionSettings Broker { get; set; } = new();
    public Dictionary<string, AppSettings> Apps { get; set; } = new();

    /// <summary>
    /// 获取 Socket/Pipe 路径 (跨平台)
    /// </summary>
    public string GetSocketPath() {
        if (!string.IsNullOrEmpty(Broker.SocketPath)) {
            return PathHelper.ExpandPath(Broker.SocketPath);
        }

        // 默认路径
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(baseDir, "pipemux", "broker.sock");
    }

    /// <summary>
    /// 获取 Pipe 名称
    /// </summary>
    public string GetPipeName() {
        return string.IsNullOrWhiteSpace(Broker.PipeName)
            ? "pipemux-broker"
            : Broker.PipeName;
    }

}

public sealed class AppSettings {
    public required string Command { get; set; }
    public bool AutoStart { get; set; }
    public int Timeout { get; set; } = 30; // 秒
}
