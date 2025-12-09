using System;
using System.Runtime.InteropServices;
using PipeMux.Shared;

namespace TerminalIdTest;

class Program {
    static void Main(string[] args) {
        Console.WriteLine("=== Terminal Identifier Test ===");
        Console.WriteLine();
        
        Console.WriteLine($"Platform: {RuntimeInformation.OSDescription}");
        Console.WriteLine($"Process ID: {Environment.ProcessId}");
        Console.WriteLine();

        // 获取终端标识符
        var terminalId = TerminalIdentifier.GetTerminalId();
        Console.WriteLine($"Terminal ID: {terminalId ?? "(null)"}");
        
        var terminalIdOrFallback = TerminalIdentifier.GetTerminalIdOrFallback();
        Console.WriteLine($"Terminal ID (with fallback): {terminalIdOrFallback}");
        Console.WriteLine();

        // 解析详情
        if (terminalId != null) {
            var info = TerminalIdInfo.Parse(terminalId);
            Console.WriteLine($"  Type: {info.Type}");
            Console.WriteLine($"  Value: {info.Value}");
        }

        Console.WriteLine();
        Console.WriteLine("=== Environment Variables ===");
        PrintEnvVar("TERM_PROGRAM");
        PrintEnvVar("WT_SESSION");
        PrintEnvVar("VSCODE_TERMINAL_ID");
        PrintEnvVar("VSCODE_PID");
        PrintEnvVar("SSH_TTY");
        PrintEnvVar("TTY");
        PrintEnvVar("TERM");
        PrintEnvVar("WINDOWID");
    }

    static void PrintEnvVar(string name) {
        var value = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrEmpty(value)) {
            Console.WriteLine($"  {name} = {value}");
        }
    }
}
