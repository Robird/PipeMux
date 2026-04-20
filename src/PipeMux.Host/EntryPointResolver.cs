using System.CommandLine;
using System.Reflection;

namespace PipeMux.Host;

/// <summary>
/// 通过反射解析目标程序集中的入口方法。
/// 入口方法必须是无参静态方法，返回 RootCommand 或 Task&lt;RootCommand&gt;。
/// </summary>
internal static class EntryPointResolver
{
    /// <summary>
    /// 解析入口方法。entryPath 格式为 "Namespace.Type.Method"。
    /// </summary>
    public static (MethodInfo method, string appName) Resolve(Assembly assembly, string entryPath)
    {
        // 按最后一个 '.' 分割：前半段是类型全名，后半段是方法名
        var lastDot = entryPath.LastIndexOf('.');
        if (lastDot <= 0)
            throw new ArgumentException(
                $"Invalid entry path '{entryPath}'. Expected format: Namespace.Type.Method " +
                $"(e.g., MyLib.DebugEntries.BuildCalculator)");

        var typeName = entryPath[..lastDot];
        var methodName = entryPath[(lastDot + 1)..];

        // 查找类型
        var type = assembly.GetType(typeName)
            ?? throw new TypeLoadException(
                $"Type '{typeName}' not found in assembly '{assembly.GetName().Name}'.\n" +
                $"Available types: {string.Join(", ", assembly.GetExportedTypes().Select(t => t.FullName))}");

        // 查找静态方法
        var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(
                $"Static method '{methodName}' not found on type '{typeName}'.\n" +
                $"Available static methods: {string.Join(", ", type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Select(m => m.Name))}");

        if (!method.IsStatic)
            throw new InvalidOperationException($"Method '{entryPath}' must be static.");

        if (method.GetParameters().Length > 0)
            throw new InvalidOperationException($"Method '{entryPath}' must take no parameters.");

        var returnType = method.ReturnType;
        if (returnType != typeof(RootCommand) && !IsTaskOfRootCommand(returnType))
            throw new InvalidOperationException(
                $"Method '{entryPath}' must return RootCommand or Task<RootCommand>, " +
                $"but returns {returnType.FullName}.");

        var appName = DeriveAppName(methodName);
        return (method, appName);
    }

    /// <summary>
    /// 调用入口方法，返回 RootCommand。
    /// </summary>
    public static async Task<RootCommand> InvokeEntry(MethodInfo method)
    {
        var result = method.Invoke(null, null);

        if (result is RootCommand rootCommand)
            return rootCommand;

        if (result is Task<RootCommand> task)
            return await task;

        throw new InvalidOperationException(
            $"Method returned unexpected type: {result?.GetType().FullName ?? "null"}");
    }

    private static bool IsTaskOfRootCommand(Type type)
    {
        return type.IsGenericType
            && type.GetGenericTypeDefinition() == typeof(Task<>)
            && type.GetGenericArguments()[0] == typeof(RootCommand);
    }

    /// <summary>
    /// 从方法名推导应用名。去掉常见前缀后转小写。
    /// BuildCalculator -> calculator, GetInspector -> inspector
    /// </summary>
    private static string DeriveAppName(string methodName)
    {
        var name = methodName;
        foreach (var prefix in new[] { "Build", "Get", "Create", "Make" })
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal) && name.Length > prefix.Length)
            {
                name = name[prefix.Length..];
                break;
            }
        }
        return name.ToLowerInvariant();
    }
}
