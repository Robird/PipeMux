namespace PipeMux.Shared;

/// <summary>
/// Minimal broker connection config — the [broker] section of broker.toml.
/// Shared between Broker and CLI for TOML deserialization.
/// </summary>
public sealed class BrokerConnectionConfig {
    public BrokerConnectionSettings Broker { get; set; } = new();
}

public sealed class BrokerConnectionSettings {
    public string? SocketPath { get; set; }
    public string? PipeName { get; set; }
}
