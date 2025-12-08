using System.Text.Json.Serialization;

namespace PipeMux.Shared.Protocol;

/// <summary>
/// Standard JSON-RPC 2.0 Response
/// </summary>
public sealed class JsonRpcResponse
{
    /// <summary>
    /// JSON-RPC version (always "2.0")
    /// </summary>
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; init; } = "2.0";

    /// <summary>
    /// Request ID (must match the request)
    /// </summary>
    [JsonPropertyName("id")]
    public object? Id { get; init; }

    /// <summary>
    /// Result (present on success, mutually exclusive with Error)
    /// </summary>
    [JsonPropertyName("result")]
    public object? Result { get; init; }

    /// <summary>
    /// Error (present on failure, mutually exclusive with Result)
    /// </summary>
    [JsonPropertyName("error")]
    public JsonRpcError? Error { get; init; }

    /// <summary>
    /// Create a success response
    /// </summary>
    public static JsonRpcResponse Success(object? id, object? result)
    {
        return new JsonRpcResponse
        {
            Id = id,
            Result = result
        };
    }

    /// <summary>
    /// Create an error response
    /// </summary>
    public static JsonRpcResponse Failure(object? id, JsonRpcError error)
    {
        return new JsonRpcResponse
        {
            Id = id,
            Error = error
        };
    }
}
