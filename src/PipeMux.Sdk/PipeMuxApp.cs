using System.CommandLine;
using Nerdbank.Streams;
using StreamJsonRpc;

namespace PipeMux.Sdk;

/// <summary>
/// PipeMux 应用框架的核心类
/// 接管 stdin/stdout 通信，接收 JSON-RPC 请求并将 args 传递给 System.CommandLine
/// </summary>
public class PipeMuxApp {
    private readonly string _name;
    private RootCommand? _rootCommand;

    public PipeMuxApp(string name) {
        _name = name;
    }

    /// <summary>
    /// 运行 App，使用 System.CommandLine 解析命令
    /// </summary>
    /// <param name="rootCommand">System.CommandLine 的根命令</param>
    /// <param name="ct">取消令牌</param>
    public async Task RunAsync(RootCommand rootCommand, CancellationToken ct = default) {
        _rootCommand = rootCommand;

        // 1. 设置 stdin/stdout 流
        using var stdin = Console.OpenStandardInput();
        using var stdout = Console.OpenStandardOutput();

        // 2. 使用 NewLineDelimitedMessageHandler（简单的行分隔 JSON，而非 LSP 风格的 header）
        // 注意参数顺序: (writer, reader, formatter)
        var formatter = new SystemTextJsonFormatter();
        var handler = new NewLineDelimitedMessageHandler(stdout, stdin, formatter);
        
        // 3. 创建 StreamJsonRpc 连接
        using var rpc = new JsonRpc(handler);

        // 4. 注册 RPC 方法 "invoke"
        rpc.AddLocalRpcTarget(new RpcTarget(this), new JsonRpcTargetOptions {
            MethodNameTransform = CommonMethodNameTransforms.CamelCase
        });

        // 5. 启动监听
        rpc.StartListening();
        Console.Error.WriteLine($"[{_name}] Service started, listening for JSON-RPC requests...");

        // 6. 等待取消或连接关闭
        try {
            // 使用 WhenAny 监听取消或连接完成
            if (ct.CanBeCanceled) {
                var cancellationTcs = new TaskCompletionSource<bool>();
                using var registration = ct.Register(() => cancellationTcs.TrySetResult(true));
                await Task.WhenAny(rpc.Completion, cancellationTcs.Task);
            }
            else {
                // 没有取消令牌时，直接等待连接完成
                await rpc.Completion;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            // 正常取消
        }
        catch (Exception ex) {
            Console.Error.WriteLine($"[{_name}] Error: {ex.Message}");
        }

        Console.Error.WriteLine($"[{_name}] Service stopped.");
    }

    /// <summary>
    /// 内部方法：执行命令
    /// </summary>
    internal async Task<InvokeResult> InvokeCommandAsync(string[] args) {
        if (_rootCommand == null) {
            return new InvokeResult {
                ExitCode = -1,
                Error = "RootCommand not initialized"
            };
        }

        var stdoutWriter = new StringWriter();
        var stderrWriter = new StringWriter();

        try {
            // 创建配置，重定向输出
            var config = new CommandLineConfiguration(_rootCommand) {
                Output = stdoutWriter,
                Error = stderrWriter
            };

            // 使用配置执行命令（自动解析并调用）
            var exitCode = await config.InvokeAsync(args);

            return new InvokeResult {
                ExitCode = exitCode,
                Output = stdoutWriter.ToString(),
                Error = stderrWriter.ToString()
            };
        }
        catch (Exception ex) {
            return new InvokeResult {
                ExitCode = -1,
                Output = stdoutWriter.ToString(),
                Error = $"{stderrWriter}{ex.Message}"
            };
        }
    }

    /// <summary>
    /// JSON-RPC 目标类，暴露 invoke 方法
    /// </summary>
    private class RpcTarget {
        private readonly PipeMuxApp _app;

        public RpcTarget(PipeMuxApp app) {
            _app = app;
        }

        /// <summary>
        /// RPC 方法：invoke
        /// 接收 args 数组并调用 System.CommandLine 处理
        /// </summary>
        public Task<InvokeResult> Invoke(string[] args) {
            return _app.InvokeCommandAsync(args);
        }
    }
}
