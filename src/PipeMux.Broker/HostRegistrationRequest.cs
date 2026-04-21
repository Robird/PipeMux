using System.Diagnostics.CodeAnalysis;
using PipeMux.Shared;
using PipeMux.Shared.Protocol;

namespace PipeMux.Broker;

/// <summary>
/// `:register` 的 broker 侧规范化结果。
/// 负责把管理命令里的 host/assembly/method 输入校验并收敛成可持久化的 AppSettings。
/// </summary>
public sealed class HostRegistrationRequest {
    private const string DefaultHostExecutable = "pmux-host";
    private const string ConfigCommandFallbackHost = "/absolute/path/to/PipeMux.Host";

    public required string AppName { get; init; }
    public required AppSettings Settings { get; init; }

    /// <summary>
    /// 统一描述 broker 当前环境下对 PipeMux.Host 的发现结果。
    /// 同时服务于 :register 默认解析与 :help / :list 的 first-time setup 文案。
    /// </summary>
    public static HostExecutableResolution ResolveHostExecutable() {
        var bundledPath = GetExpectedBundledHostPath();
        if (bundledPath != null && File.Exists(bundledPath)) {
            return new HostExecutableResolution {
                Source = HostExecutableSource.Bundled,
                ResolvedPath = bundledPath,
                SuggestedConfigCommandHost = bundledPath,
                Error = string.Empty
            };
        }

        var pathExecutable = PathHelper.TryFindOnPath(DefaultHostExecutable);
        if (pathExecutable != null) {
            return new HostExecutableResolution {
                Source = HostExecutableSource.Path,
                ResolvedPath = pathExecutable,
                SuggestedConfigCommandHost = DefaultHostExecutable,
                Error = string.Empty
            };
        }

        var attempted = string.Join(
            "; ",
            new[] { bundledPath, $"PATH lookup for '{DefaultHostExecutable}'" }
                .Where(static value => !string.IsNullOrEmpty(value)));

        return new HostExecutableResolution {
            Source = HostExecutableSource.NotFound,
            ResolvedPath = null,
            SuggestedConfigCommandHost = ConfigCommandFallbackHost,
            Error = $"Could not locate {DefaultHostExecutable}. Tried: {attempted}. "
                  + "Pass --host-path </absolute/path/to/PipeMux.Host> to :register, "
                  + "or install via scripts/install-ubuntu-user.sh."
        };
    }

    public static bool TryCreate(ManagementCommand command, [NotNullWhen(true)] out HostRegistrationRequest? registration, out string error) {
        registration = null;

        if (string.IsNullOrWhiteSpace(command.TargetApp)
            || string.IsNullOrWhiteSpace(command.TargetAssemblyPath)
            || string.IsNullOrWhiteSpace(command.TargetMethodName)) {
            error = "Usage: pmux :register <app-name> <assembly-path> <namespace.type.method> [--host-path <pmux-host-path>]";
            return false;
        }

        var normalizedAssemblyPath = NormalizeRequiredFile(command.TargetAssemblyPath, "Assembly not found", requirePathLikeInput: false, out error);
        if (normalizedAssemblyPath == null) {
            return false;
        }

        string? normalizedHostPath;
        if (!string.IsNullOrWhiteSpace(command.HostPath)) {
            normalizedHostPath = NormalizeRequiredFile(
                command.HostPath,
                "Host executable not found",
                requirePathLikeInput: true,
                out error
            );
        }
        else {
            var resolution = ResolveHostExecutable();
            normalizedHostPath = resolution.ResolvedPath;
            error = resolution.Error;
        }

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

    private static string? GetExpectedBundledHostPath() {
        // broker 自身位于 .../bin/broker/PipeMux.Broker；同布局下 host 在 .../bin/host/PipeMux.Host
        var brokerDir = AppContext.BaseDirectory;
        if (string.IsNullOrEmpty(brokerDir)) {
            return null;
        }

        var bundleRoot = Path.GetDirectoryName(brokerDir.TrimEnd(Path.DirectorySeparatorChar));
        if (string.IsNullOrEmpty(bundleRoot)) {
            return null;
        }

        var executableName = OperatingSystem.IsWindows() ? "PipeMux.Host.exe" : "PipeMux.Host";
        return Path.Combine(bundleRoot, "host", executableName);
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

public enum HostExecutableSource {
    NotFound,
    Bundled,
    Path
}

public sealed class HostExecutableResolution {
    public required HostExecutableSource Source { get; init; }
    public required string? ResolvedPath { get; init; }
    public required string SuggestedConfigCommandHost { get; init; }
    public required string Error { get; init; }

    public bool CanAutoResolveForRegister => ResolvedPath != null;
}
