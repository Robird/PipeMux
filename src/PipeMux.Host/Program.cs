using PipeMux.Host;
using PipeMux.Sdk;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: pipemux-host <assemblyPath> <Namespace.Type.Method>");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Arguments:");
    Console.Error.WriteLine("  assemblyPath            Path to the target .NET assembly (.dll)");
    Console.Error.WriteLine("  Namespace.Type.Method   Fully qualified static method that returns RootCommand");
    Console.Error.WriteLine();
    Console.Error.WriteLine("The target method must be:");
    Console.Error.WriteLine("  - static");
    Console.Error.WriteLine("  - parameterless");
    Console.Error.WriteLine("  - return System.CommandLine.RootCommand or Task<RootCommand>");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Example:");
    Console.Error.WriteLine("  pipemux-host ./MyLib.dll MyLib.DebugEntries.BuildCalculator");
    return 1;
}

var assemblyPath = Path.GetFullPath(args[0]);
var entryPath = args[1];

if (!File.Exists(assemblyPath))
{
    Console.Error.WriteLine($"Error: Assembly not found: {assemblyPath}");
    return 1;
}

try
{
    // 1. 在隔离上下文中加载目标程序集
    var loadContext = new HostLoadContext(assemblyPath);
    var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
    Console.Error.WriteLine($"[host] Loaded assembly: {assembly.GetName().Name} from {assemblyPath}");

    // 2. 解析入口方法
    var (method, appName) = EntryPointResolver.Resolve(assembly, entryPath);
    Console.Error.WriteLine($"[host] Resolved entry: {entryPath} -> app '{appName}'");

    // 3. 调用入口方法获取 RootCommand
    var rootCommand = await EntryPointResolver.InvokeEntry(method);
    Console.Error.WriteLine($"[host] RootCommand created: '{rootCommand.Description ?? "(no description)"}'");

    // 4. 作为 PipeMux 应用运行（进入 JSON-RPC 监听循环）
    var app = new PipeMuxApp(appName);
    await app.RunAsync(rootCommand);

    return 0;
}
catch (Exception ex) when (ex is TypeLoadException or MissingMethodException
                               or InvalidOperationException or ArgumentException)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Fatal error: {ex}");
    return 2;
}
