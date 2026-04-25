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

    public static void VerifyRuntimeAvailable() {
        _ = SerializeRequest(new Request {
            App = "__warmup__",
            Args = []
        });

        _ = DeserializeRequest("""{"app":"__warmup__","args":[],"requestId":"warmup"}""");
        _ = SerializeResponse(Response.Ok("warmup", "ok"));
        _ = DeserializeResponse("""{"requestId":"warmup","success":true,"data":"ok"}""");
    }

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
}
