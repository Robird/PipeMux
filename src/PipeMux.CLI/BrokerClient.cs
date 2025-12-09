using System.IO.Pipes;
using System.Text;
using PipeMux.Shared;
using PipeMux.Shared.Protocol;

namespace PipeMux.CLI;

/// <summary>
/// CLI 客户端 - 连接到 Broker 并发送请求
/// </summary>
public sealed class BrokerClient {
    private const string DefaultPipeName = "pipemux-broker";
    private const int ConnectionTimeoutSeconds = 5;

    /// <summary>
    /// 发送请求到 Broker
    /// </summary>
    public async Task<Response> SendRequestAsync(string app, string[] args) {
        // 获取终端标识符（用于多终端隔离）
        var terminalId = TerminalIdentifier.GetTerminalId();
        
        var request = new Request {
            App = app,
            Args = args,
            TerminalId = terminalId
        };

        try {
            var pipeName = GetPipeName();

            // 创建 Named Pipe 客户端
            using var pipeClient = new NamedPipeClientStream(
                ".",           // 服务器名（本地）
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous
            );

            // 连接到 Broker（带超时）
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(ConnectionTimeoutSeconds));
            try {
                await pipeClient.ConnectAsync(cts.Token);
            }
            catch (TimeoutException) {
                return Response.Fail(request.RequestId, "Connection timeout: Broker not responding");
            }
            catch (OperationCanceledException) {
                return Response.Fail(request.RequestId, "Connection timeout: Broker not responding");
            }

            // 发送请求
            using var writer = new StreamWriter(pipeClient, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
            var requestJson = JsonRpc.SerializeRequest(request);
            await writer.WriteLineAsync(requestJson);

            // 接收响应
            using var reader = new StreamReader(pipeClient, Encoding.UTF8, leaveOpen: true);
            var responseJson = await reader.ReadLineAsync();

            if (string.IsNullOrEmpty(responseJson)) {
                return Response.Fail(request.RequestId, "Broker returned empty response");
            }

            // 反序列化
            var response = JsonRpc.DeserializeResponse(responseJson);
            return response ?? Response.Fail(request.RequestId, "Invalid response from broker");
        }
        catch (TimeoutException) {
            return Response.Fail(request.RequestId, "Connection timeout: Broker not responding");
        }
        catch (IOException ex) {
            // Named Pipe 不存在或 Broker 未运行
            if (ex.Message.Contains("pipe") || ex.Message.Contains("does not exist")) {
                return Response.Fail(request.RequestId, $"Broker not running: {ex.Message}");
            }
            return Response.Fail(request.RequestId, $"Communication error: {ex.Message}");
        }
        catch (Exception ex) {
            return Response.Fail(request.RequestId, $"Connection error: {ex.Message}");
        }
    }

    /// <summary>
    /// 获取 Pipe 名称（支持环境变量覆盖）
    /// </summary>
    private static string GetPipeName() {
        return Environment.GetEnvironmentVariable("DOCUI_PIPE_NAME")
               ?? DefaultPipeName;
    }
}
