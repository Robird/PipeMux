using System.IO.Pipes;
using System.Text;
using PipeMux.Shared.Protocol;
using StreamJsonRpc;

namespace PipeMux.Broker;

/// <summary>
/// Broker 服务器主逻辑
/// </summary>
public sealed class BrokerServer
{
    private readonly BrokerConfig _config;
    private readonly ProcessRegistry _registry;
    private CancellationTokenSource? _cts;
    private readonly List<Task> _clientTasks = new(); // P0 Fix: Track background tasks
    private readonly object _taskLock = new();

    public BrokerServer(BrokerConfig config, ProcessRegistry registry)
    {
        _config = config;
        _registry = registry;
    }

    /// <summary>
    /// 启动 Broker (监听 Named Pipe / Unix Socket)
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        
        // 自动启动配置的应用
        foreach (var (name, settings) in _config.Apps.Where(kv => kv.Value.AutoStart))
        {
            try
            {
                _registry.Start(name, settings.Command);
                Console.Error.WriteLine($"[INFO] Auto-started: {name}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[WARN] Failed to auto-start {name}: {ex.Message}");
            }
        }

        // 启动 Named Pipe 服务器循环
        var pipeName = GetPipeName();
        Console.Error.WriteLine($"[INFO] Broker started, listening on pipe: {pipeName}");
        
        try
        {
            await RunNamedPipeServerAsync(pipeName, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("[INFO] Broker shutting down...");
        }
        finally
        {
            // P0 Fix: Wait for all client tasks to complete before shutdown
            Console.Error.WriteLine("[INFO] Waiting for client tasks to complete...");
            Task[] tasksToWait;
            lock (_taskLock)
            {
                tasksToWait = _clientTasks.ToArray();
            }
            await Task.WhenAll(tasksToWait).ConfigureAwait(false);
            Console.Error.WriteLine("[INFO] All client tasks completed");
        }
    }

    /// <summary>
    /// Named Pipe 服务器循环
    /// </summary>
    private async Task RunNamedPipeServerAsync(string pipeName, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipeServer = null;
            try
            {
                // 创建 Named Pipe 服务器实例
                pipeServer = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous
                );

                // 等待客户端连接
                await pipeServer.WaitForConnectionAsync(cancellationToken);
                Console.Error.WriteLine("[INFO] Client connected");

                // P0 Fix: Track client task and handle exceptions
                var clientPipe = pipeServer;
                var clientTask = Task.Run(async () =>
                {
                    try
                    {
                        await HandleClientAsync(clientPipe);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[ERROR] Unhandled client task exception: {ex.Message}");
                    }
                    finally
                    {
                        clientPipe.Dispose();
                    }
                }, cancellationToken);
                
                // Track task and auto-remove when complete
                lock (_taskLock)
                {
                    _clientTasks.Add(clientTask);
                }
                _ = clientTask.ContinueWith(t =>
                {
                    lock (_taskLock)
                    {
                        _clientTasks.Remove(t);
                    }
                }, TaskScheduler.Default);
                
                // 重要: 不在这里 dispose，由 Task 负责
                pipeServer = null;
            }
            catch (OperationCanceledException)
            {
                pipeServer?.Dispose();
                throw;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] Server loop error: {ex.Message}");
                pipeServer?.Dispose();
                await Task.Delay(100, cancellationToken); // 避免快速失败循环
            }
        }
    }

    /// <summary>
    /// 处理单个客户端连接
    /// </summary>
    private async Task HandleClientAsync(NamedPipeServerStream pipeServer)
    {
        try
        {
            using var reader = new StreamReader(pipeServer, Encoding.UTF8, leaveOpen: true);
            using var writer = new StreamWriter(pipeServer, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

            // 读取客户端请求（JSON）
            var requestJson = await reader.ReadLineAsync();
            if (string.IsNullOrEmpty(requestJson))
            {
                Console.Error.WriteLine("[WARN] Client sent empty request");
                return;
            }

            // 反序列化请求 (使用我们的协议类)
            var request = PipeMux.Shared.Protocol.JsonRpc.DeserializeRequest(requestJson);
            if (request == null)
            {
                Console.Error.WriteLine($"[ERROR] Failed to deserialize request: {requestJson}");
                var errorResponse = Response.Fail(Guid.NewGuid().ToString(), "Invalid request format");
                await writer.WriteLineAsync(PipeMux.Shared.Protocol.JsonRpc.SerializeResponse(errorResponse));
                return;
            }

            Console.Error.WriteLine($"[INFO] Processing request: {request.App} {string.Join(" ", request.Args)}");

            // 调用现有的 HandleRequestAsync 处理业务逻辑
            var response = await HandleRequestAsync(request);

            // 返回响应（JSON）
            var responseJson = PipeMux.Shared.Protocol.JsonRpc.SerializeResponse(response);
            Console.Error.WriteLine($"[DEBUG] Sending response: {responseJson}");
            await writer.WriteLineAsync(responseJson);
            
            Console.Error.WriteLine($"[INFO] Response sent: {(response.Success ? "SUCCESS" : "FAIL")}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] Client handler error: {ex.Message}");
        }
        finally
        {
            Console.Error.WriteLine("[INFO] Client disconnected");
        }
    }

    /// <summary>
    /// 获取 Pipe 名称
    /// </summary>
    private string GetPipeName()
    {
        // 从配置读取，否则使用默认值
        return _config.Broker.PipeName ?? "pipemux-broker";
    }

    /// <summary>
    /// 处理单个请求
    /// </summary>
    private async Task<Response> HandleRequestAsync(Request request)
    {
        // 检查应用是否注册
        if (!_config.Apps.TryGetValue(request.App, out var appSettings))
        {
            return Response.Fail(request.RequestId, $"Unknown app: {request.App}");
        }

        // 生成进程键：使用 App:TerminalId 实现多终端隔离
        // 如果没有 TerminalId，则回退到仅用 App 名（所有终端共享同一实例）
        var processKey = !string.IsNullOrEmpty(request.TerminalId)
            ? $"{request.App}:{request.TerminalId}"
            : request.App;

        // 获取或启动进程
        var process = _registry.Get(processKey);
        bool isNewProcess = false;
        
        // P0 Fix: Check process health and restart if unhealthy
        if (process == null || process.HasExited || !process.IsHealthy())
        {
            try
            {
                Console.Error.WriteLine($"[INFO] Starting new process for {request.App} (key: {processKey})");
                process = _registry.Start(processKey, appSettings.Command);
                isNewProcess = true;
                
                // 新启动的进程可能需要初始化时间
                await Task.Delay(100);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] Failed to start {request.App}: {ex.Message}");
                return Response.Fail(request.RequestId, $"Failed to start app: {ex.Message}");
            }
        }
        else
        {
            Console.Error.WriteLine($"[INFO] Reusing existing process for key: {processKey}, PID: {process.ProcessId}");
        }

        // 新协议：调用 invoke 方法，传递原始 args 数组
        try
        {
            var timeout = TimeSpan.FromSeconds(appSettings.Timeout);
            
            // 调用后端的 invoke 方法，传递 args 数组
            // InvokeResult 包含 { ExitCode, Output, Error }
            var result = await process.InvokeAsync("invoke", new object?[] { request.Args }, timeout);
            
            // 解析 InvokeResult
            if (result is System.Text.Json.JsonElement jsonElement)
            {
                // Note: 属性名可能是 PascalCase（ExitCode）或 camelCase（exitCode），取决于 formatter 配置
                var exitCode = jsonElement.TryGetProperty("ExitCode", out var ec) ? ec.GetInt32() 
                             : jsonElement.TryGetProperty("exitCode", out ec) ? ec.GetInt32() : 0;
                var output = jsonElement.TryGetProperty("Output", out var ov) ? ov.GetString() ?? ""
                           : jsonElement.TryGetProperty("output", out ov) ? ov.GetString() ?? "" : "";
                var error = jsonElement.TryGetProperty("Error", out var ev) ? ev.GetString() ?? ""
                          : jsonElement.TryGetProperty("error", out ev) ? ev.GetString() ?? "" : "";
                
                // 去除尾部换行
                output = output.TrimEnd('\n', '\r');
                error = error.TrimEnd('\n', '\r');
                
                if (exitCode != 0 || !string.IsNullOrEmpty(error))
                {
                    // 命令执行失败
                    var errorMsg = !string.IsNullOrEmpty(error) ? error : $"Command failed with exit code {exitCode}";
                    return Response.Fail(request.RequestId, errorMsg);
                }
                
                return Response.Ok(request.RequestId, output);
            }
            
            // 兼容旧格式或其他返回类型
            return Response.Ok(request.RequestId, result?.ToString());
        }
        catch (TimeoutException ex)
        {
            Console.Error.WriteLine($"[ERROR] Request timeout for {request.App}: {ex.Message}");
            return Response.Fail(request.RequestId, $"Request timeout: {ex.Message}");
        }
        catch (StreamJsonRpc.RemoteInvocationException ex)
        {
            Console.Error.WriteLine($"[ERROR] Remote error from {request.App}: {ex.Message}");
            return Response.Fail(request.RequestId, ex.Message);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] Communication error with {request.App}: {ex.Message}");
            if (isNewProcess)
            {
                _registry.Close(processKey);
            }
            return Response.Fail(request.RequestId, $"Communication error: {ex.Message}");
        }
    }
}
