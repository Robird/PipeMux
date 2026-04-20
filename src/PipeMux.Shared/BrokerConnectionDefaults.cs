namespace PipeMux.Shared;

public static class BrokerConnectionDefaults {
    public const string DefaultPipeName = "pipemux-broker";
    public const string PipeNameEnvVar = "PIPEMUX_PIPE_NAME";
    public const string LegacyPipeNameEnvVar = "DOCUI_PIPE_NAME";
    public const string SocketPathEnvVar = "PIPEMUX_SOCKET_PATH";

    public static string GetConfigPath() {
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(homeDir, ".config", "pipemux", "broker.toml");
    }

    public static string GetDefaultSocketPath() {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(baseDir, "pipemux", "broker.sock");
    }
}
