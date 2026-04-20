using System.Net.Sockets;
using System.Runtime.InteropServices;
using PipeMux.Shared;
using Tomlyn;

namespace PipeMux.CLI;

internal enum BrokerTransportKind {
    NamedPipe,
    UnixSocket
}

internal readonly record struct BrokerEndpoint(BrokerTransportKind Transport, string Value);

internal static class BrokerEndpointResolver {
    private const string DefaultPipeName = "pipemux-broker";
    private const string PipeNameEnvVar = "PIPEMUX_PIPE_NAME";
    private const string LegacyPipeNameEnvVar = "DOCUI_PIPE_NAME";
    private const string SocketPathEnvVar = "PIPEMUX_SOCKET_PATH";

    public static BrokerEndpoint Resolve() {
        var explicitSocketPath = Environment.GetEnvironmentVariable(SocketPathEnvVar);
        if (!string.IsNullOrWhiteSpace(explicitSocketPath)) {
            return new BrokerEndpoint(BrokerTransportKind.UnixSocket, PathHelper.ExpandPath(explicitSocketPath));
        }

        var explicitPipeName = Environment.GetEnvironmentVariable(PipeNameEnvVar)
                               ?? Environment.GetEnvironmentVariable(LegacyPipeNameEnvVar);
        if (!string.IsNullOrWhiteSpace(explicitPipeName)) {
            return new BrokerEndpoint(BrokerTransportKind.NamedPipe, explicitPipeName);
        }

        var config = TryLoadConfig();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            return new BrokerEndpoint(
                BrokerTransportKind.NamedPipe,
                string.IsNullOrWhiteSpace(config?.Broker.PipeName) ? DefaultPipeName : config!.Broker.PipeName!);
        }

        if (!string.IsNullOrWhiteSpace(config?.Broker.SocketPath)) {
            return new BrokerEndpoint(BrokerTransportKind.UnixSocket, PathHelper.ExpandPath(config!.Broker.SocketPath!));
        }

        if (!string.IsNullOrWhiteSpace(config?.Broker.PipeName)) {
            return new BrokerEndpoint(BrokerTransportKind.NamedPipe, config!.Broker.PipeName!);
        }

        return new BrokerEndpoint(BrokerTransportKind.UnixSocket, GetDefaultSocketPath());
    }

    private static BrokerConnectionConfig? TryLoadConfig() {
        try {
            var configPath = GetConfigPath();
            if (!File.Exists(configPath)) {
                return null;
            }

            var toml = File.ReadAllText(configPath);
            return Toml.ToModel<BrokerConnectionConfig>(toml);
        }
        catch {
            return null;
        }
    }

    private static string GetConfigPath() {
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(homeDir, ".config", "pipemux", "broker.toml");
    }

    private static string GetDefaultSocketPath() {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(baseDir, "pipemux", "broker.sock");
    }

}
