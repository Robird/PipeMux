using System.Text.Json.Serialization;

namespace PipeMux.Shared.Protocol;

/// <summary>
/// Standard JSON-RPC 2.0 Error
/// </summary>
public sealed class JsonRpcError {
    /// <summary>
    /// Error code
    /// </summary>
    [JsonPropertyName("code")]
    public int Code { get; init; }

    /// <summary>
    /// Error message
    /// </summary>
    [JsonPropertyName("message")]
    public required string Message { get; init; }

    /// <summary>
    /// Additional error data (optional)
    /// </summary>
    [JsonPropertyName("data")]
    public object? Data { get; init; }

    // Standard error codes
    public const int ParseError = -32700;
    public const int InvalidRequest = -32600;
    public const int MethodNotFound = -32601;
    public const int InvalidParams = -32602;
    public const int InternalError = -32603;
    public const int ServerError = -32000;

    /// <summary>
    /// Create a parse error
    /// </summary>
    public static JsonRpcError CreateParseError(string? details = null) {
        return new JsonRpcError {
            Code = ParseError,
            Message = "Parse error",
            Data = details
        };
    }

    /// <summary>
    /// Create an invalid request error
    /// </summary>
    public static JsonRpcError CreateInvalidRequest(string? details = null) {
        return new JsonRpcError {
            Code = InvalidRequest,
            Message = "Invalid Request",
            Data = details
        };
    }

    /// <summary>
    /// Create a method not found error
    /// </summary>
    public static JsonRpcError CreateMethodNotFound(string method) {
        return new JsonRpcError {
            Code = MethodNotFound,
            Message = "Method not found",
            Data = method
        };
    }

    /// <summary>
    /// Create an invalid params error
    /// </summary>
    public static JsonRpcError CreateInvalidParams(string? details = null) {
        return new JsonRpcError {
            Code = InvalidParams,
            Message = "Invalid params",
            Data = details
        };
    }

    /// <summary>
    /// Create a server error
    /// </summary>
    public static JsonRpcError CreateServerError(string message, object? data = null) {
        return new JsonRpcError {
            Code = ServerError,
            Message = message,
            Data = data
        };
    }
}
