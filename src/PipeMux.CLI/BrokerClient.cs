using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using PipeMux.Shared;
using PipeMux.Shared.Protocol;

namespace PipeMux.CLI;

/// <summary>
/// CLI 客户端 - 连接到 Broker 并发送请求
/// </summary>
public sealed class BrokerClient {
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

        return await SendRequestCoreAsync(request);
    }

    /// <summary>
    /// 发送管理命令到 Broker
    /// </summary>
    public async Task<Response> SendManagementCommandAsync(ManagementCommand command) {
        var request = new Request {
            App = null,
            ManagementCommand = command
        };

        return await SendRequestCoreAsync(request);
    }

    /// <summary>
    /// 核心请求发送逻辑
    /// </summary>
    private async Task<Response> SendRequestCoreAsync(Request request) {
        var endpoint = BrokerEndpointResolver.Resolve();

        try {
            return endpoint.Transport switch {
                BrokerTransportKind.NamedPipe => await SendViaNamedPipeAsync(request, endpoint.Value),
                BrokerTransportKind.UnixSocket => await SendViaUnixSocketAsync(request, endpoint.Value),
                _ => Response.Fail(request.RequestId, "Unsupported broker transport")
            };
        }
        catch (TimeoutException) {
            return CreateConnectionFailure(request.RequestId, endpoint, "Connection timeout: Broker not responding");
        }
        catch (IOException ex) {
            // Named Pipe 不存在或 Broker 未运行
            if (ex.Message.Contains("pipe") || ex.Message.Contains("does not exist")) {
                return CreateConnectionFailure(request.RequestId, endpoint, $"Broker not running: {ex.Message}");
            }
            return CreateConnectionFailure(request.RequestId, endpoint, $"Communication error: {ex.Message}");
        }
        catch (Exception ex) {
            return CreateConnectionFailure(request.RequestId, endpoint, $"Connection error: {ex.Message}");
        }
    }

    private async Task<Response> SendViaNamedPipeAsync(Request request, string pipeName) {
        using var pipeClient = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous
        );

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

        return await SendOverStreamAsync(pipeClient, request);
    }

    private async Task<Response> SendViaUnixSocketAsync(Request request, string socketPath) {
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(ConnectionTimeoutSeconds));

        try {
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), cts.Token);
        }
        catch (SocketException ex) when (ex.SocketErrorCode is SocketError.AddressNotAvailable or SocketError.HostNotFound or SocketError.ConnectionRefused or SocketError.NotConnected) {
            return CreateConnectionFailure(request.RequestId, new BrokerEndpoint(BrokerTransportKind.UnixSocket, socketPath), $"Broker not running: {ex.Message}");
        }
        catch (OperationCanceledException) {
            return CreateConnectionFailure(request.RequestId, new BrokerEndpoint(BrokerTransportKind.UnixSocket, socketPath), "Connection timeout: Broker not responding");
        }

        using var stream = new NetworkStream(socket, ownsSocket: false);
        return await SendOverStreamAsync(stream, request);
    }

    private static async Task<Response> SendOverStreamAsync(Stream stream, Request request) {
        using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
        var requestJson = JsonRpc.SerializeRequest(request);
        await writer.WriteLineAsync(requestJson);

        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        var responseJson = await reader.ReadLineAsync();

        if (string.IsNullOrEmpty(responseJson)) {
            return Response.Fail(request.RequestId, "Broker returned empty response");
        }

        var response = JsonRpc.DeserializeResponse(responseJson);
        return response ?? Response.Fail(request.RequestId, "Invalid response from broker");
    }

    private static Response CreateConnectionFailure(string requestId, BrokerEndpoint endpoint, string detail) {
        var endpointDescription = endpoint.Transport switch {
            BrokerTransportKind.UnixSocket => $"unix socket '{endpoint.Value}'",
            BrokerTransportKind.NamedPipe => $"named pipe '{endpoint.Value}'",
            _ => endpoint.Value
        };

        var configPath = BrokerConnectionDefaults.GetConfigPath();
        var hint = $"endpoint={endpointDescription}; config={configPath}; try 'systemctl --user restart pipemux-broker' or verify broker.toml";
        return Response.Fail(requestId, $"{detail} ({hint})");
    }
}
