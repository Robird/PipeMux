using PipeMux.Shared;
using Tomlyn;

namespace PipeMux.Broker;

/// <summary>
/// BrokerConfig 的默认化与 TOML 编解码。
/// 仅负责模型和文本之间的转换，不处理文件 I/O。
/// </summary>
internal static class BrokerConfigTomlCodec {
    public static BrokerConfig CreateDefault() {
        return CreateSnapshot(config: null);
    }

    public static BrokerConfig Deserialize(string toml) {
        var model = Toml.ToModel<BrokerConfig>(toml);
        return CreateSnapshot(model);
    }

    public static string Serialize(BrokerConfig config) {
        return Toml.FromModel(CreateSnapshot(config));
    }

    public static BrokerConfig CreateWithApps(BrokerConnectionSettings broker, IReadOnlyDictionary<string, AppSettings> apps) {
        return new BrokerConfig {
            Broker = CloneBrokerSettings(broker),
            Apps = CloneApps(apps)
        };
    }

    private static BrokerConfig CreateSnapshot(BrokerConfig? config) {
        return new BrokerConfig {
            Broker = CloneBrokerSettings(config?.Broker),
            Apps = CloneApps(config?.Apps)
        };
    }

    private static BrokerConnectionSettings CloneBrokerSettings(BrokerConnectionSettings? settings) {
        return new BrokerConnectionSettings {
            SocketPath = settings?.SocketPath,
            PipeName = settings?.PipeName
        };
    }

    private static Dictionary<string, AppSettings> CloneApps(IReadOnlyDictionary<string, AppSettings>? apps) {
        var snapshot = new Dictionary<string, AppSettings>(StringComparer.Ordinal);

        if (apps == null) {
            return snapshot;
        }

        foreach (var (name, settings) in apps) {
            snapshot[name] = new AppSettings {
                Command = settings.Command,
                AutoStart = settings.AutoStart,
                Timeout = settings.Timeout
            };
        }

        return snapshot;
    }
}
