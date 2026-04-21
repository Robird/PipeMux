namespace PipeMux.Shared;

/// <summary>
/// 路径展开工具：~ → 用户主目录，环境变量展开
/// </summary>
public static class PathHelper {
    public static string ExpandPath(string path) {
        var expanded = Environment.ExpandEnvironmentVariables(path);
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (expanded == "~") {
            return homeDir;
        }

        if (expanded.StartsWith("~/", StringComparison.Ordinal) ||
            expanded.StartsWith("~\\", StringComparison.Ordinal)) {
            var relativePath = expanded[2..]
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(homeDir, relativePath);
        }

        return expanded;
    }

    /// <summary>
    /// 在系统 PATH 中查找可执行文件；找到则返回绝对路径，否则返回 null。
    /// Windows 下会附带尝试常见可执行扩展名（.exe/.cmd/.bat）。
    /// </summary>
    public static string? TryFindOnPath(string commandName) {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue)) {
            return null;
        }

        string[] candidateFileNames = OperatingSystem.IsWindows()
            ? [commandName, $"{commandName}.exe", $"{commandName}.cmd", $"{commandName}.bat"]
            : [commandName];

        foreach (var segment in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            foreach (var fileName in candidateFileNames) {
                var candidatePath = Path.Combine(segment, fileName);
                if (File.Exists(candidatePath)) {
                    return Path.GetFullPath(candidatePath);
                }
            }
        }

        return null;
    }
}
