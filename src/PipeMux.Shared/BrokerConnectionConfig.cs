namespace PipeMux.Shared;

/// <summary>
/// Minimal broker connection config — the [broker] section of broker.toml.
/// Shared between Broker and CLI for TOML deserialization.
/// </summary>
public sealed class BrokerConnectionConfig {
    public BrokerConnectionSettings Broker { get; set; } = new();
    public Dictionary<string, BrokerConnectionAppSettings> Apps { get; set; } = new(StringComparer.Ordinal);
}

public sealed class BrokerConnectionSettings {
    public string? SocketPath { get; set; }
    public string? PipeName { get; set; }
}

/// <summary>
/// Minimal app settings model so the CLI can deserialize the full broker.toml
/// without silently falling back to default endpoints when [apps.*] sections exist.
/// </summary>
public sealed class BrokerConnectionAppSettings {
    public string? Command { get; set; }
    public bool AutoStart { get; set; }
    public int Timeout { get; set; } = 30;
}
