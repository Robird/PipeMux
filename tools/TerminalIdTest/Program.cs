// 终端标识符测试程序
// 用于验证跨平台终端标识的正确性
// 
// 测试方法：
// 1. 在同一终端中多次运行，应该输出相同的 Terminal ID
// 2. 在不同终端中运行，应该输出不同的 Terminal ID
// 3. 在 WSL 和 Windows 中分别运行，观察输出

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║           PipeMux Terminal Identifier Test                   ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
Console.WriteLine();

// 基础信息
Console.WriteLine($"▶ OS: {RuntimeInformation.OSDescription}");
Console.WriteLine($"▶ Platform: {(OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsLinux() ? "Linux" : OperatingSystem.IsMacOS() ? "macOS" : "Unknown")}");
Console.WriteLine($"▶ Process ID: {Environment.ProcessId}");
Console.WriteLine($"▶ Current Time: {DateTime.Now:HH:mm:ss.fff}");
Console.WriteLine();

// 环境变量检测
Console.WriteLine("═══ Environment Variables ═══");
var relevantEnvVars = new[] {
    "TERM", "TERM_PROGRAM", "TERM_SESSION_ID",
    "WT_SESSION", "WT_PROFILE_ID",
    "VSCODE_TERMINAL_ID", "VSCODE_PID", "VSCODE_INJECTION",
    "SSH_TTY", "SSH_CONNECTION",
    "TMUX", "TMUX_PANE",
    "STY", // screen session
    "WINDOWID", // X11
    "ConEmuPID", "ConEmuBuild" // ConEmu
};

foreach (var varName in relevantEnvVars) {
    var value = Environment.GetEnvironmentVariable(varName);
    if (!string.IsNullOrEmpty(value)) {
        Console.WriteLine($"  {varName} = {value}");
    }
}
Console.WriteLine();

// Unix TTY 检测
if (!OperatingSystem.IsWindows()) {
    Console.WriteLine("═══ Unix TTY Detection ═══");
    
    // 方法 1: /proc/self/fd/0
    try {
        var stdinLink = "/proc/self/fd/0";
        if (File.Exists(stdinLink) || Directory.Exists(Path.GetDirectoryName(stdinLink))) {
            var target = File.ResolveLinkTarget(stdinLink, true);
            Console.WriteLine($"  /proc/self/fd/0 → {target?.FullName ?? "(null)"}");
        }
    }
    catch (Exception ex) {
        Console.WriteLine($"  /proc/self/fd/0 → Error: {ex.Message}");
    }

    // 方法 2: tty 命令
    try {
        var psi = new ProcessStartInfo {
            FileName = "tty",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi);
        if (process != null) {
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            Console.WriteLine($"  tty command → {output} (exit: {process.ExitCode})");
        }
    }
    catch (Exception ex) {
        Console.WriteLine($"  tty command → Error: {ex.Message}");
    }

    // 方法 3: getsid
    try {
        [DllImport("libc", EntryPoint = "getsid")]
        static extern int getsid(int pid);
        
        var sid = getsid(0);
        Console.WriteLine($"  getsid(0) → {sid}");
    }
    catch (Exception ex) {
        Console.WriteLine($"  getsid(0) → Error: {ex.Message}");
    }
    
    Console.WriteLine();
}

// Windows 控制台检测
if (OperatingSystem.IsWindows()) {
    Console.WriteLine("═══ Windows Console Detection ═══");
    
    // 方法 1: GetConsoleWindow
    try {
        [DllImport("kernel32.dll")]
        static extern IntPtr GetConsoleWindow();
        
        var hwnd = GetConsoleWindow();
        Console.WriteLine($"  GetConsoleWindow() → 0x{hwnd.ToInt64():X} ({(hwnd == IntPtr.Zero ? "NULL - possibly Windows Terminal!" : "valid")})");
    }
    catch (Exception ex) {
        Console.WriteLine($"  GetConsoleWindow() → Error: {ex.Message}");
    }

    // 方法 2: 追溯父进程
    try {
        Console.WriteLine("  Parent process chain:");
        var current = Process.GetCurrentProcess();
        int depth = 0;
        var visited = new HashSet<int>();
        
        while (current != null && depth < 10 && !visited.Contains(current.Id)) {
            visited.Add(current.Id);
            var indent = new string(' ', depth * 2 + 4);
            Console.WriteLine($"{indent}[{current.Id}] {current.ProcessName}");
            
            try {
                var parentId = GetParentProcessId(current.Id);
                if (parentId <= 0) break;
                current = Process.GetProcessById(parentId);
                depth++;
            }
            catch {
                break;
            }
        }
    }
    catch (Exception ex) {
        Console.WriteLine($"  Parent chain → Error: {ex.Message}");
    }
    
    Console.WriteLine();
}

// 最终结果
Console.WriteLine("═══ RESULT ═══");
var terminalId = GetTerminalId();
Console.WriteLine($"  ★ Terminal ID: {terminalId ?? "(unable to detect)"}");
Console.WriteLine();

Console.WriteLine("═══ Test Instructions ═══");
Console.WriteLine("  1. Run this program multiple times in the SAME terminal");
Console.WriteLine("     → Terminal ID should be IDENTICAL");
Console.WriteLine("  2. Run this program in a DIFFERENT terminal");
Console.WriteLine("     → Terminal ID should be DIFFERENT");
Console.WriteLine();

// ============ Helper Functions ============

static string? GetTerminalId() {
    if (OperatingSystem.IsWindows()) {
        var winId = GetWindowsTerminalId();
        if (!string.IsNullOrEmpty(winId))
            return winId;
    }
    else {
        // Linux/macOS: 读取 TTY
        try {
            var target = File.ResolveLinkTarget("/proc/self/fd/0", true);
            if (target != null && (target.FullName.StartsWith("/dev/pts/") || target.FullName.StartsWith("/dev/tty")))
                return $"tty:{target.FullName}";
        }
        catch { }

        // Session ID fallback
        try {
            [DllImport("libc", EntryPoint = "getsid")]
            static extern int getsid(int pid);
            
            var sid = getsid(0);
            if (sid > 0)
                return $"sid:{sid}";
        }
        catch { }
    }
    
    return null;
}

static string? GetWindowsTerminalId() {
    // Windows Terminal (WT) provides a stable session token
    var wtSession = Environment.GetEnvironmentVariable("WT_SESSION");
    if (!string.IsNullOrEmpty(wtSession))
        return $"wt:{wtSession}";

    // VS Code 内置终端的专用环境变量
    if (Environment.GetEnvironmentVariable("TERM_PROGRAM") == "vscode") {
        var vscodeId = Environment.GetEnvironmentVariable("VSCODE_TERMINAL_ID");
        if (!string.IsNullOrEmpty(vscodeId))
            return $"vscode:{vscodeId}";
    }

    // 优先尝试用标准句柄的文件指纹（卷序列号 + 文件索引）作为稳定 ID
    var pipeId = GetAnyStdHandleFileId();
    if (!string.IsNullOrEmpty(pipeId))
        return $"pipe:{pipeId}";

    // 其次用父进程链中的终端宿主进程，适配 ConPTY/WSL 集成场景
    var host = FindTerminalHostProcess();
    if (host != null)
        return $"host:{host.ProcessName}:{host.Id}";

    // 最后兜底使用窗口句柄（仅在传统控制台稳定）
    try {
        [DllImport("kernel32.dll")]
        static extern IntPtr GetConsoleWindow();

        var hwnd = GetConsoleWindow();
        if (hwnd != IntPtr.Zero)
            return $"hwnd:{hwnd.ToInt64():X}";
    }
    catch { }

    return null;
}

static Process? FindTerminalHostProcess() {
    try {
        var current = Process.GetCurrentProcess();
        var visited = new HashSet<int>();

        while (current != null && visited.Add(current.Id) && visited.Count < 20) {
            var name = current.ProcessName.ToLowerInvariant();

            if (IsTerminalHostName(name)) {
                return current;
            }

            var parentId = GetParentProcessId(current.Id);
            if (parentId <= 0) break;

            try {
                current = Process.GetProcessById(parentId);
            }
            catch {
                break;
            }
        }
    }
    catch { }

    return null;
}

static bool IsTerminalHostName(string name) {
    // 常见终端/宿主进程名称，统一用小写比较
    return name is "windowsterminal" or "conhost" or "openconsole"
        or "wsl" or "wt" or "wezterm" or "alacritty" or "mintty"
        or "ttermpro" or "xshell" or "mobaxterm" or "putty"
        or "powershell" or "pwsh" or "cmd"
        or "bash" or "zsh" or "fish"
        or "code" or "code - insiders";
}

static string? GetAnyStdHandleFileId() {
    const int STD_INPUT_HANDLE = -10;
    const int STD_OUTPUT_HANDLE = -11;
    const int STD_ERROR_HANDLE = -12;
    const int FileIdInfo = 18; // FILE_INFO_BY_HANDLE_CLASS.FileIdInfo

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetFileInformationByHandleEx(IntPtr hFile, int fileInfoClass, out FILE_ID_INFO fileInformation, int dwBufferSize);

    static string? TryHandle(int kind) {
        var handle = GetStdHandle(kind);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            return null;

        if (GetFileInformationByHandleEx(handle, FileIdInfo, out var info, Marshal.SizeOf<FILE_ID_INFO>())) {
            return $"{info.VolumeSerialNumber:X}-{info.FileId.HighPart:X}-{info.FileId.LowPart:X}";
        }

        return null;
    }

    return TryHandle(STD_INPUT_HANDLE)
        ?? TryHandle(STD_OUTPUT_HANDLE)
        ?? TryHandle(STD_ERROR_HANDLE);
}

static int FindShellProcess() {
    try {
        var current = Process.GetCurrentProcess();
        var visited = new HashSet<int>();
        
        while (current != null && !visited.Contains(current.Id)) {
            visited.Add(current.Id);
            var name = current.ProcessName.ToLowerInvariant();
            
            // 找到 shell 或终端宿主进程
            if (name is "cmd" or "powershell" or "pwsh" or "bash" or "zsh" or "fish" 
                or "conhost" or "windowsterminal") {
                return current.Id;
            }

            var parentId = GetParentProcessId(current.Id);
            if (parentId <= 0) break;
            current = Process.GetProcessById(parentId);
        }
    }
    catch { }
    return -1;
}

static int GetParentProcessId(int processId) {
    if (OperatingSystem.IsWindows()) {
        try {
            [DllImport("kernel32.dll")]
            static extern IntPtr OpenProcess(int access, bool inherit, int pid);
            
            [DllImport("kernel32.dll")]
            static extern bool CloseHandle(IntPtr handle);
            
            [DllImport("ntdll.dll")]
            static extern int NtQueryInformationProcess(IntPtr handle, int infoClass, 
                ref PROCESS_BASIC_INFORMATION info, int size, out int returnLength);
            
            var handle = OpenProcess(0x0400, false, processId);
            if (handle == IntPtr.Zero) return -1;
            
            try {
                var pbi = new PROCESS_BASIC_INFORMATION();
                var status = NtQueryInformationProcess(handle, 0, ref pbi, 
                    Marshal.SizeOf<PROCESS_BASIC_INFORMATION>(), out _);
                if (status == 0)
                    return pbi.InheritedFromUniqueProcessId.ToInt32();
            }
            finally {
                CloseHandle(handle);
            }
        }
        catch { }
    }
    else {
        try {
            var statPath = $"/proc/{processId}/stat";
            if (File.Exists(statPath)) {
                var content = File.ReadAllText(statPath);
                var parts = content.Split(' ');
                if (parts.Length > 3 && int.TryParse(parts[3], out var ppid))
                    return ppid;
            }
        }
        catch { }
    }
    return -1;
}

[StructLayout(LayoutKind.Sequential)]
struct PROCESS_BASIC_INFORMATION {
    public IntPtr Reserved1;
    public IntPtr PebBaseAddress;
    public IntPtr Reserved2_0;
    public IntPtr Reserved2_1;
    public IntPtr UniqueProcessId;
    public IntPtr InheritedFromUniqueProcessId;
}

[StructLayout(LayoutKind.Sequential)]
struct FILE_ID_INFO {
    public ulong VolumeSerialNumber;
    public FILE_ID_128 FileId;
}

[StructLayout(LayoutKind.Sequential)]
struct FILE_ID_128 {
    public ulong LowPart;
    public ulong HighPart;
}
