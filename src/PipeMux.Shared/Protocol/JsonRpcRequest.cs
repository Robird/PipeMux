using System.Text.Json.Serialization;

namespace PipeMux.Shared.Protocol;

/// <summary>
/// Standard JSON-RPC 2.0 Request
/// </summary>
public sealed class JsonRpcRequest {
    /// <summary>
    /// JSON-RPC version (always "2.0")
    /// </summary>
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; init; } = "2.0";

    /// <summary>
    /// Request ID (can be string or number)
    /// </summary>
    [JsonPropertyName("id")]
    public object? Id { get; init; }

    /// <summary>
    /// Method name to invoke
    /// </summary>
    [JsonPropertyName("method")]
    public required string Method { get; init; }

    /// <summary>
    /// Method parameters (can be object or array)
    /// </summary>
    [JsonPropertyName("params")]
    public object? Params { get; init; }
}
