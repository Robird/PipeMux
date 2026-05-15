namespace PipeMux.Shared;

public static class BrokerConnectionDefaults {
    public const string DefaultPipeName = "pipemux-broker";
    public const string PipeNameEnvVar = "PIPEMUX_PIPE_NAME";
    public const string LegacyPipeNameEnvVar = "DOCUI_PIPE_NAME";
    public const string SocketPathEnvVar = "PIPEMUX_SOCKET_PATH";
    public const string BrokerServiceName = "pipemux-broker.service";
    public const string BrokerEnvironmentDropInFileName = "10-environment.conf";

    public static string GetConfigPath() {
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(homeDir, ".config", "pipemux", "broker.toml");
    }

    public static string GetBrokerEnvironmentFilePath() {
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(homeDir, ".config", "pipemux", "broker.env");
    }

    public static string GetSystemdUserDirectory() {
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(homeDir, ".config", "systemd", "user");
    }

    public static string GetBrokerEnvironmentDropInDirectory() {
        return Path.Combine(GetSystemdUserDirectory(), $"{BrokerServiceName}.d");
    }

    public static string GetBrokerEnvironmentDropInPath() {
        return Path.Combine(GetBrokerEnvironmentDropInDirectory(), BrokerEnvironmentDropInFileName);
    }

    public static string GetDefaultSocketPath() {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(baseDir, "pipemux", "broker.sock");
    }
}
