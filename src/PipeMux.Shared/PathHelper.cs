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
}
