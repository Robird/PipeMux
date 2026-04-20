using System.Reflection;
using System.Runtime.Loader;

namespace PipeMux.Host;

/// <summary>
/// 隔离的程序集加载上下文，用于加载目标 DLL 及其依赖。
/// 通过 AssemblyDependencyResolver 解析目标的 *.deps.json，
/// 同时将 System.CommandLine 回退到宿主上下文以保证类型一致性。
/// </summary>
internal sealed class HostLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public HostLoadContext(string assemblyPath) : base(isCollectible: false)
    {
        _resolver = new AssemblyDependencyResolver(assemblyPath);
    }

    protected override Assembly? Load(AssemblyName name)
    {
        // System.CommandLine 必须与宿主共享，否则 RootCommand 会出现类型不一致
        if (string.Equals(name.Name, "System.CommandLine", StringComparison.OrdinalIgnoreCase))
            return null;

        var path = _resolver.ResolveAssemblyToPath(name);
        return path != null ? LoadFromAssemblyPath(path) : null;
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path != null ? LoadUnmanagedDllFromPath(path) : nint.Zero;
    }
}
