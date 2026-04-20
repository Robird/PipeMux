namespace PipeMux.Shared;

public static class BrokerConnectionResolver {
    public static BrokerEndpoint ResolveClientEndpoint(BrokerConnectionConfig? config) {
        var explicitSocketPath = Environment.GetEnvironmentVariable(BrokerConnectionDefaults.SocketPathEnvVar);
        if (!string.IsNullOrWhiteSpace(explicitSocketPath)) {
            return new BrokerEndpoint(BrokerTransportKind.UnixSocket, PathHelper.ExpandPath(explicitSocketPath));
        }

        var explicitPipeName = Environment.GetEnvironmentVariable(BrokerConnectionDefaults.PipeNameEnvVar)
                               ?? Environment.GetEnvironmentVariable(BrokerConnectionDefaults.LegacyPipeNameEnvVar);
        if (!string.IsNullOrWhiteSpace(explicitPipeName)) {
            return new BrokerEndpoint(BrokerTransportKind.NamedPipe, explicitPipeName);
        }

        return ResolveServerEndpoint(config?.Broker ?? new BrokerConnectionSettings());
    }

    public static BrokerEndpoint ResolveServerEndpoint(BrokerConnectionSettings settings) {
        if (OperatingSystem.IsWindows()) {
            return new BrokerEndpoint(
                BrokerTransportKind.NamedPipe,
                string.IsNullOrWhiteSpace(settings.PipeName)
                    ? BrokerConnectionDefaults.DefaultPipeName
                    : settings.PipeName);
        }

        if (!string.IsNullOrWhiteSpace(settings.SocketPath)) {
            return new BrokerEndpoint(BrokerTransportKind.UnixSocket, PathHelper.ExpandPath(settings.SocketPath));
        }

        if (!string.IsNullOrWhiteSpace(settings.PipeName)) {
            return new BrokerEndpoint(BrokerTransportKind.NamedPipe, settings.PipeName);
        }

        return new BrokerEndpoint(BrokerTransportKind.UnixSocket, BrokerConnectionDefaults.GetDefaultSocketPath());
    }
}
