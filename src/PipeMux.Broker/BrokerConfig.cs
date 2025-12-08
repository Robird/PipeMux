namespace PipeMux.Broker;

/// <summary>
/// Broker 配置模型 (对应 TOML 文件)
/// </summary>
public sealed class BrokerConfig
{
    public BrokerSettings Broker { get; set; } = new();
    public Dictionary<string, AppSettings> Apps { get; set; } = new();

    /// <summary>
    /// 获取 Socket/Pipe 路径 (跨平台)
    /// </summary>
    public string GetSocketPath()
    {
        if (!string.IsNullOrEmpty(Broker.SocketPath))
        {
            return Environment.ExpandEnvironmentVariables(Broker.SocketPath);
        }

        // 默认路径
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(baseDir, "pipemux", "broker.sock");
    }
}

public sealed class BrokerSettings
{
    public string? SocketPath { get; set; }
    public string? PipeName { get; set; }
}

public sealed class AppSettings
{
    public required string Command { get; set; }
    public bool AutoStart { get; set; }
    public int Timeout { get; set; } = 30; // 秒
}
