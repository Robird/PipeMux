using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace PipeMux.Shared;

/// <summary>
/// 跨平台终端会话标识器
/// 用于在同一终端窗口的多次 CLI 调用中返回相同的标识符
/// </summary>
public static class TerminalIdentifier
{
    /// <summary>
    /// 环境变量名：允许显式覆盖终端标识符
    /// 用于测试或特殊场景（如在同一终端模拟多个会话）
    /// </summary>
    public const string EnvVarName = "PIPEMUX_TERMINAL_ID";

    /// <summary>
    /// 获取当前终端会话的唯一标识符
    /// </summary>
    /// <returns>终端标识符，如果无法识别则返回 null</returns>
    public static string? GetTerminalId()
    {
        // 优先使用环境变量覆盖（用于测试或显式指定）
        var envOverride = Environment.GetEnvironmentVariable(EnvVarName);
        if (!string.IsNullOrEmpty(envOverride))
        {
            return $"env:{envOverride}";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return GetWindowsTerminalId();
        }
        else
        {
            return GetUnixTerminalId();
        }
    }

    /// <summary>
    /// 获取终端标识符，如果无法识别则返回一个基于进程的 fallback
    /// </summary>
    public static string GetTerminalIdOrFallback()
    {
        return GetTerminalId() ?? $"process-{Environment.ProcessId}";
    }

    #region Linux/macOS Implementation

    private static string? GetUnixTerminalId()
    {
        // 方案 1: 优先检查 VS Code 终端（使用 VSCODE_IPC_HOOK_CLI 的 UUID）
        // 这是最可靠的方案，因为 UUID 在 VS Code 窗口级别唯一
        var vscodeTerminalId = GetVSCodeTerminalId();
        if (!string.IsNullOrEmpty(vscodeTerminalId))
        {
            return vscodeTerminalId;
        }

        // 方案 2: 通过 /proc/self/fd/0 获取 TTY 设备路径
        // 注意: 在 VS Code 中这可能每次运行都不同，所以放在 VS Code 检测之后
        var ttyPath = GetTtyFromProc();
        if (!string.IsNullOrEmpty(ttyPath))
        {
            return $"tty:{ttyPath}";
        }

        // 方案 3: 使用 Session ID
        var sessionId = GetSessionId();
        if (sessionId > 0)
        {
            return $"sid:{sessionId}";
        }

        return null;
    }

    /// <summary>
    /// Linux: 通过读取 /proc/self/fd/0 符号链接获取 TTY 路径
    /// </summary>
    private static string? GetTtyFromProc()
    {
        try
        {
            const string stdinLink = "/proc/self/fd/0";
            if (File.Exists(stdinLink) || Directory.Exists(Path.GetDirectoryName(stdinLink)))
            {
                var target = ReadSymbolicLink(stdinLink);
                // 验证是否是有效的 TTY 路径
                if (!string.IsNullOrEmpty(target) && 
                    (target.StartsWith("/dev/pts/") || target.StartsWith("/dev/tty")))
                {
                    return target;
                }
            }
        }
        catch
        {
            // 忽略错误，使用 fallback
        }
        return null;
    }

    /// <summary>
    /// 读取符号链接的目标
    /// </summary>
    private static string? ReadSymbolicLink(string path)
    {
        try
        {
            // .NET 6+ 支持
            var fileInfo = new FileInfo(path);
            if (fileInfo.LinkTarget != null)
            {
                return fileInfo.LinkTarget;
            }

            // Fallback: 使用 File.ResolveLinkTarget
            var resolved = File.ResolveLinkTarget(path, returnFinalTarget: true);
            return resolved?.FullName;
        }
        catch
        {
            // 最后尝试：使用 readlink 命令
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "readlink",
                    Arguments = $"-f \"{path}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                if (process != null)
                {
                    var output = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit();
                    if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                    {
                        return output;
                    }
                }
            }
            catch { }
        }
        return null;
    }

    /// <summary>
    /// 获取 Session ID (Unix)
    /// </summary>
    private static int GetSessionId()
    {
        try
        {
            // 尝试 P/Invoke getsid
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return NativeMethods.getsid(0);
            }
        }
        catch
        {
            // P/Invoke 失败，使用进程命令
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "ps",
                    Arguments = $"-o sid= -p {Environment.ProcessId}",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                if (process != null)
                {
                    var output = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit();
                    if (int.TryParse(output, out var sid))
                    {
                        return sid;
                    }
                }
            }
            catch { }
        }
        return -1;
    }

    #endregion

    #region Windows Implementation

    private static string? GetWindowsTerminalId()
    {
        // 方案 1: Windows Terminal 的 WT_SESSION 环境变量
        var wtSession = Environment.GetEnvironmentVariable("WT_SESSION");
        if (!string.IsNullOrEmpty(wtSession))
        {
            return $"wt:{wtSession}";
        }

        // 方案 2: VS Code 终端
        var vscodeTerminalId = GetVSCodeTerminalId();
        if (!string.IsNullOrEmpty(vscodeTerminalId))
        {
            return vscodeTerminalId;
        }

        // 方案 3: 传统控制台 - GetConsoleWindow
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                var consoleWindow = NativeMethods.GetConsoleWindow();
                if (consoleWindow != IntPtr.Zero)
                {
                    return $"hwnd:{consoleWindow.ToInt64():X}";
                }
            }
            catch { }
        }

        // 方案 4: 追溯父进程找到控制台宿主
        var consoleHostPid = FindConsoleHostProcess();
        if (consoleHostPid > 0)
        {
            return $"conhost:{consoleHostPid}";
        }

        return null;
    }

    /// <summary>
    /// 查找控制台宿主进程 (conhost.exe 或 WindowsTerminal.exe)
    /// </summary>
    private static int FindConsoleHostProcess()
    {
        try
        {
            var current = Process.GetCurrentProcess();
            var visited = new HashSet<int>();
            
            while (current != null && !visited.Contains(current.Id))
            {
                visited.Add(current.Id);
                
                var name = current.ProcessName.ToLowerInvariant();
                if (name == "conhost" || name == "windowsterminal" || 
                    name == "cmd" || name == "powershell" || name == "pwsh")
                {
                    return current.Id;
                }

                try
                {
                    var parentId = GetParentProcessId(current.Id);
                    if (parentId <= 0) break;
                    current = Process.GetProcessById(parentId);
                }
                catch
                {
                    break;
                }
            }
        }
        catch { }
        return -1;
    }

    /// <summary>
    /// 获取父进程 ID (Windows)
    /// </summary>
    private static int GetParentProcessId(int processId)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return NativeMethods.GetParentProcessIdWindows(processId);
            }
            else
            {
                // Linux/macOS: 读取 /proc/{pid}/stat
                var statPath = $"/proc/{processId}/stat";
                if (File.Exists(statPath))
                {
                    var content = File.ReadAllText(statPath);
                    // 格式: pid (comm) state ppid ...
                    var parts = content.Split(' ');
                    if (parts.Length > 3 && int.TryParse(parts[3], out var ppid))
                    {
                        return ppid;
                    }
                }
            }
        }
        catch { }
        return -1;
    }

    #endregion

    #region VS Code Terminal Detection

    /// <summary>
    /// 检测 VS Code 终端并生成标识符
    /// 
    /// VS Code 环境变量研究结果 (2024-12):
    /// - VSCODE_IPC_HOOK_CLI: 包含 UUID，每个 VS Code 窗口唯一
    ///   格式: /run/user/0/vscode-ipc-{uuid}.sock 或 类似路径
    /// - TERM_PROGRAM: 值为 "vscode"
    /// - VSCODE_GIT_IPC_HANDLE: Git 集成用，不太稳定
    /// 
    /// 注意: VSCODE_TERMINAL_ID 变量不存在！
    /// </summary>
    private static string? GetVSCodeTerminalId()
    {
        // VS Code 设置的环境变量
        var termProgram = Environment.GetEnvironmentVariable("TERM_PROGRAM");
        if (termProgram == "vscode")
        {
            // 方案 1: 从 VSCODE_IPC_HOOK_CLI 提取 UUID（推荐）
            // 这个 UUID 对每个 VS Code 窗口是唯一的
            var ipcHook = Environment.GetEnvironmentVariable("VSCODE_IPC_HOOK_CLI");
            if (!string.IsNullOrEmpty(ipcHook))
            {
                // 格式: /run/user/0/vscode-ipc-6e1ab7f7-9b21-4f10-bfc6-5f696017ecc4.sock
                // 或 Windows: \\.\pipe\vscode-ipc-{uuid}
                var match = System.Text.RegularExpressions.Regex.Match(
                    ipcHook, 
                    @"vscode-ipc-([a-f0-9-]{36})", 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    return $"vscode-window:{match.Groups[1].Value}";
                }
            }

            // 方案 2: WSL 特殊处理 - WSL_INTEROP 包含会话进程 PID
            var wslInterop = Environment.GetEnvironmentVariable("WSL_INTEROP");
            if (!string.IsNullOrEmpty(wslInterop))
            {
                // 格式: /run/WSL/76736_interop
                var wslMatch = System.Text.RegularExpressions.Regex.Match(wslInterop, @"/(\d+)_interop$");
                if (wslMatch.Success)
                {
                    return $"vscode-wsl:{wslMatch.Groups[1].Value}";
                }
            }

            // 方案 3: Fallback - 使用 PTY 设备路径
            // 注意: VS Code 每次运行程序可能分配不同的 PTY，这不是理想方案
            var tty = GetTtyFromProc();
            if (!string.IsNullOrEmpty(tty))
            {
                return $"vscode-tty:{tty}";
            }

            // 方案 4: 最后 fallback - 使用当前 shell 的 PPID
            // 这需要配合文件存储才能在多次调用间保持一致
            try
            {
                var ppid = GetParentProcessId(Environment.ProcessId);
                if (ppid > 0)
                {
                    return $"vscode-shell:{ppid}";
                }
            }
            catch { }
        }
        return null;
    }

    #endregion

    #region Native Methods

    private static class NativeMethods
    {
        // Linux: getsid from libc
        [DllImport("libc", EntryPoint = "getsid", SetLastError = true)]
        public static extern int getsid(int pid);

        // Windows: GetConsoleWindow from kernel32
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GetConsoleWindow();

        // Windows: 获取父进程 ID
        public static int GetParentProcessIdWindows(int processId)
        {
            try
            {
                var handle = OpenProcess(0x0400 /* PROCESS_QUERY_INFORMATION */, false, processId);
                if (handle == IntPtr.Zero) return -1;

                try
                {
                    var pbi = new PROCESS_BASIC_INFORMATION();
                    int returnLength;
                    var status = NtQueryInformationProcess(handle, 0, ref pbi, 
                        Marshal.SizeOf(pbi), out returnLength);
                    
                    if (status == 0)
                    {
                        return pbi.InheritedFromUniqueProcessId.ToInt32();
                    }
                }
                finally
                {
                    CloseHandle(handle);
                }
            }
            catch { }
            return -1;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("ntdll.dll")]
        private static extern int NtQueryInformationProcess(IntPtr processHandle, int processInformationClass,
            ref PROCESS_BASIC_INFORMATION processInformation, int processInformationLength, out int returnLength);

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_BASIC_INFORMATION
        {
            public IntPtr Reserved1;
            public IntPtr PebBaseAddress;
            public IntPtr Reserved2_0;
            public IntPtr Reserved2_1;
            public IntPtr UniqueProcessId;
            public IntPtr InheritedFromUniqueProcessId;
        }
    }

    #endregion
}

/// <summary>
/// 终端标识符的解析结果
/// </summary>
public record TerminalIdInfo
{
    public required string Type { get; init; }
    public required string Value { get; init; }
    public required string RawId { get; init; }

    public static TerminalIdInfo Parse(string terminalId)
    {
        var colonIndex = terminalId.IndexOf(':');
        if (colonIndex > 0)
        {
            return new TerminalIdInfo
            {
                Type = terminalId[..colonIndex],
                Value = terminalId[(colonIndex + 1)..],
                RawId = terminalId
            };
        }
        return new TerminalIdInfo
        {
            Type = "unknown",
            Value = terminalId,
            RawId = terminalId
        };
    }
}
