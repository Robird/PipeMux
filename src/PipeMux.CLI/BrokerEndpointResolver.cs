using PipeMux.Shared;
using Tomlyn;

namespace PipeMux.CLI;

internal static class BrokerEndpointResolver {
    public static BrokerEndpoint Resolve() {
        return BrokerConnectionResolver.ResolveClientEndpoint(TryLoadConfig());
    }

    private static BrokerConnectionConfig? TryLoadConfig() {
        try {
            var configPath = BrokerConnectionDefaults.GetConfigPath();
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
}
