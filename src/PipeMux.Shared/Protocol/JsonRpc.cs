using System.Text.Json;
using System.Text.Json.Serialization;

namespace PipeMux.Shared.Protocol;

/// <summary>
/// 简化的 JSON-RPC 2.0 实现 (只包含核心功能)
/// </summary>
public static class JsonRpc {
    private static readonly JsonSerializerOptions SerializerOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    /// <summary>
    /// 序列化请求为 JSON
    /// </summary>
    public static string SerializeRequest(Request request) {
        return JsonSerializer.Serialize(request, SerializerOptions);
    }

    /// <summary>
    /// 反序列化请求
    /// </summary>
    public static Request? DeserializeRequest(string json) {
        return JsonSerializer.Deserialize<Request>(json, SerializerOptions);
    }

    /// <summary>
    /// 序列化响应为 JSON
    /// </summary>
    public static string SerializeResponse(Response response) {
        return JsonSerializer.Serialize(response, SerializerOptions);
    }

    /// <summary>
    /// 反序列化响应
    /// </summary>
    public static Response? DeserializeResponse(string json) {
        return JsonSerializer.Deserialize<Response>(json, SerializerOptions);
    }

    /// <summary>
    /// 序列化 JSON-RPC 请求
    /// </summary>
    public static string SerializeJsonRpcRequest(JsonRpcRequest request) {
        return JsonSerializer.Serialize(request, SerializerOptions);
    }

    /// <summary>
    /// 反序列化 JSON-RPC 响应
    /// </summary>
    public static JsonRpcResponse? DeserializeJsonRpcResponse(string json) {
        return JsonSerializer.Deserialize<JsonRpcResponse>(json, SerializerOptions);
    }
}
