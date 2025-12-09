using System.Diagnostics;
using Nerdbank.Streams;
using StreamJsonRpc;

namespace PipeMux.Broker;

/// <summary>
/// 管理后台应用进程的注册表
/// </summary>
public sealed class ProcessRegistry {
    private readonly Dictionary<string, AppProcess> _processes = new();
    private readonly object _lock = new();

    /// <summary>
    /// 启动应用进程
    /// </summary>
    public AppProcess Start(string appName, string command) {
        lock (_lock) {
            if (_processes.TryGetValue(appName, out var existing) && !existing.HasExited) {
                return existing;
            }

            var process = new AppProcess(appName, command);
            process.Start();
            _processes[appName] = process;
            return process;
        }
    }

    /// <summary>
    /// 获取应用进程 (如果不存在或已退出则返回 null)
    /// </summary>
    public AppProcess? Get(string appName) {
        lock (_lock) {
            if (_processes.TryGetValue(appName, out var process)) {
                if (process.HasExited) {
                    // 进程已退出，从注册表移除并清理
                    _processes.Remove(appName);
                    process.Dispose();
                    return null;
                }
                return process;
            }
            return null;
        }
    }

    /// <summary>
    /// 关闭应用进程
    /// </summary>
    public bool Close(string appName) {
        lock (_lock) {
            if (_processes.Remove(appName, out var process)) {
                process.Dispose();
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// 列出所有活跃进程
    /// </summary>
    public IReadOnlyList<string> ListActive() {
        lock (_lock) {
            return _processes
                .Where(kv => !kv.Value.HasExited)
                .Select(kv => kv.Key)
                .ToList();
        }
    }
}

/// <summary>
/// 后台应用进程的封装 - 使用 StreamJsonRpc 进行通信
/// </summary>
public sealed class AppProcess : IDisposable {
    private readonly Process _process;
    private readonly JsonRpc _rpc;
    private volatile bool _isHealthy = true;
    
    public string AppName { get; }
    public bool HasExited => _process.HasExited;
    public int ProcessId => _process.Id;

    public AppProcess(string appName, string command) {
        AppName = appName;

        // 解析命令 (简单实现，不处理引号)
        var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var fileName = parts[0];
        var arguments = string.Join(' ', parts.Skip(1));

        _process = new Process {
            StartInfo = new ProcessStartInfo {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        
        // JsonRpc 将在 Start() 中初始化
        _rpc = null!;
    }

    public void Start() {
        _process.Start();
        Console.Error.WriteLine($"[INFO] Process started: {AppName}, PID: {_process.Id}");
        
        // 消费 StandardError 防止死锁
        _ = Task.Run(async () => {
            try {
                while (!_process.HasExited) {
                    var line = await _process.StandardError.ReadLineAsync();
                    if (line != null) {
                        Console.Error.WriteLine($"[{AppName}] {line}");
                    }
                    else {
                        break; // EOF
                    }
                }
            }
            catch (Exception ex) {
                Console.Error.WriteLine($"[WARN] Error reading stderr from {AppName}: {ex.Message}");
            }
        });
        
        // 使用 StreamJsonRpc 连接到进程的 stdin/stdout
        // 使用 NewLineDelimitedMessageHandler（简单的行分隔 JSON，而非 LSP 风格的 header）
        // 注意参数顺序: (writer, reader, formatter)
        // - 我们写入进程的 StandardInput
        // - 我们读取进程的 StandardOutput
        var formatter = new SystemTextJsonFormatter();
        var handler = new NewLineDelimitedMessageHandler(
            _process.StandardInput.BaseStream,  // writer: 写入进程 stdin
            _process.StandardOutput.BaseStream, // reader: 读取进程 stdout
            formatter);
        
        // 创建 JsonRpc 连接（不注册本地目标，只作为客户端调用远程方法）
        var rpc = new JsonRpc(handler);
        rpc.StartListening();
        
        // 替换 null 初始化的 _rpc
        System.Runtime.CompilerServices.Unsafe.AsRef(in _rpc) = rpc;
    }

    /// <summary>
    /// 调用远程方法并返回结果
    /// </summary>
    public async Task<object?> InvokeAsync(string method, object?[] args, TimeSpan timeout, CancellationToken ct = default) {
        if (!IsHealthy()) {
            throw new InvalidOperationException($"Process {AppName} is not healthy");
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        try {
            // StreamJsonRpc 会自动处理 JSON-RPC 协议
            return await _rpc.InvokeWithCancellationAsync<object?>(method, args, cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !ct.IsCancellationRequested) {
            _isHealthy = false;
            throw new TimeoutException($"Request to {AppName}.{method} timed out after {timeout.TotalSeconds}s");
        }
        catch (Exception ex) when (ex is StreamJsonRpc.ConnectionLostException or IOException) {
            _isHealthy = false;
            throw;
        }
    }

    /// <summary>
    /// 检查进程是否健康
    /// </summary>
    public bool IsHealthy() {
        if (!_isHealthy)
            return false;
        
        if (_process.HasExited) {
            _isHealthy = false;
            return false;
        }
        
        if (_rpc?.IsDisposed == true) {
            _isHealthy = false;
            return false;
        }
        
        return true;
    }

    public void Dispose() {
        _rpc?.Dispose();
        
        if (!_process.HasExited) {
            Console.Error.WriteLine($"[INFO] Killing process: {AppName}, PID: {_process.Id}");
            _process.Kill(entireProcessTree: true);
        }
        _process.Dispose();
    }
}
