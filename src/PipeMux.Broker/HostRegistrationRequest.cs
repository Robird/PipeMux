using System.Diagnostics.CodeAnalysis;
using PipeMux.Shared;
using PipeMux.Shared.Protocol;

namespace PipeMux.Broker;

/// <summary>
/// `:register` 的 broker 侧规范化结果。
/// 负责把管理命令里的 host/assembly/method 输入校验并收敛成可持久化的 AppSettings。
/// </summary>
public sealed class HostRegistrationRequest {
    private const string DefaultHostExecutable = "pipemux-host";

    public required string AppName { get; init; }
    public required AppSettings Settings { get; init; }

    public static bool TryCreate(ManagementCommand command, [NotNullWhen(true)] out HostRegistrationRequest? registration, out string error) {
        registration = null;

        if (string.IsNullOrWhiteSpace(command.TargetApp)
            || string.IsNullOrWhiteSpace(command.TargetAssemblyPath)
            || string.IsNullOrWhiteSpace(command.TargetMethodName)) {
            error = "Usage: pmux :register <app-name> <assembly-path> <namespace.type.method> [--host-path <pipemux-host-path>]";
            return false;
        }

        var normalizedAssemblyPath = NormalizeRequiredFile(command.TargetAssemblyPath, "Assembly not found", requirePathLikeInput: false, out error);
        if (normalizedAssemblyPath == null) {
            return false;
        }

        var normalizedHostPath = string.IsNullOrWhiteSpace(command.HostPath)
            ? DefaultHostExecutable
            : NormalizeRequiredFile(
                command.HostPath,
                "Host executable not found",
                requirePathLikeInput: true,
                out error
            );

        if (normalizedHostPath == null) {
            return false;
        }

        registration = new HostRegistrationRequest {
            AppName = command.TargetApp,
            Settings = new AppSettings {
                Command = BuildHostCommand(normalizedHostPath, normalizedAssemblyPath, command.TargetMethodName),
                AutoStart = false,
                Timeout = 30
            }
        };

        error = string.Empty;
        return true;
    }

    private static string? NormalizeRequiredFile(
        string input,
        string notFoundPrefix,
        bool requirePathLikeInput,
        out string error
    ) {
        if (requirePathLikeInput && !LooksLikeFilePath(input)) {
            error = "--host-path must be a path to the PipeMux.Host executable. For custom command lines, edit broker.toml directly.";
            return null;
        }

        var normalizedPath = Path.GetFullPath(PathHelper.ExpandPath(input));
        if (!File.Exists(normalizedPath)) {
            error = $"{notFoundPrefix}: {input}";
            return null;
        }

        error = string.Empty;
        return normalizedPath;
    }

    private static string BuildHostCommand(string hostPath, string assemblyPath, string methodName) {
        return string.Join(" ", [
            EscapeArgument(hostPath),
            EscapeArgument(assemblyPath),
            EscapeArgument(methodName)
        ]);
    }

    private static bool LooksLikeFilePath(string value) {
        return Path.IsPathRooted(value)
            || value.StartsWith(".", StringComparison.Ordinal)
            || value.StartsWith("~", StringComparison.Ordinal)
            || value.Contains(Path.DirectorySeparatorChar)
            || value.Contains(Path.AltDirectorySeparatorChar);
    }

    private static string EscapeArgument(string value) {
        return $"\"{value.Replace("\"", "\\\"")}\"";
    }
}
