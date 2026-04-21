using System.CommandLine;

namespace HostDemo;

/// <summary>
/// 演示在一个程序集中提供多个调试入口。
/// 每个静态方法返回一个 RootCommand，可被 PipeMux.Host 加载。
/// 注意：本项目不依赖 PipeMux.Sdk，只需 System.CommandLine。
/// </summary>
public static class DebugEntries
{
    // ===== 入口 1：有状态计数器 =====

    private static int _count = 0;

    /// <summary>
    /// 有状态计数器——演示跨调用的状态保持。
    /// </summary>
    public static RootCommand BuildCounter()
    {
        var root = new RootCommand("Stateful counter demo");

        var incCmd = new Command("inc", "Increment counter");
        incCmd.SetAction(ctx =>
        {
            _count++;
            ctx.InvocationConfiguration.Output.WriteLine($"Counter: {_count}");
        });

        var decCmd = new Command("dec", "Decrement counter");
        decCmd.SetAction(ctx =>
        {
            _count--;
            ctx.InvocationConfiguration.Output.WriteLine($"Counter: {_count}");
        });

        var getCmd = new Command("get", "Get current value");
        getCmd.SetAction(ctx =>
        {
            ctx.InvocationConfiguration.Output.WriteLine($"Counter: {_count}");
        });

        var resetCmd = new Command("reset", "Reset counter to 0");
        resetCmd.SetAction(ctx =>
        {
            _count = 0;
            ctx.InvocationConfiguration.Output.WriteLine($"Counter: {_count}");
        });

        var addArg = new Argument<int>("value") { Description = "Value to add" };
        var addCmd = new Command("add", "Add value to counter") { addArg };
        addCmd.SetAction(ctx =>
        {
            _count += ctx.GetValue(addArg);
            ctx.InvocationConfiguration.Output.WriteLine($"Counter: {_count}");
        });

        root.Add(incCmd);
        root.Add(decCmd);
        root.Add(getCmd);
        root.Add(resetCmd);
        root.Add(addCmd);

        return root;
    }

    // ===== 入口 2：问候服务 =====

    /// <summary>
    /// 问候服务——演示同一程序集中的不同入口。
    /// </summary>
    public static RootCommand BuildGreeter()
    {
        var root = new RootCommand("Greeting service demo");
        var greetings = new List<string>();

        var nameArg = new Argument<string>("name") { Description = "Name to greet" };
        var helloCmd = new Command("hello", "Greet someone") { nameArg };
        helloCmd.SetAction(ctx =>
        {
            var name = ctx.GetValue(nameArg);
            var greeting = $"Hello, {name}!";
            greetings.Add(greeting);
            ctx.InvocationConfiguration.Output.WriteLine(greeting);
        });

        var historyCmd = new Command("history", "Show greeting history");
        historyCmd.SetAction(ctx =>
        {
            if (greetings.Count == 0)
            {
                ctx.InvocationConfiguration.Output.WriteLine("No greetings yet.");
                return;
            }
            for (var i = 0; i < greetings.Count; i++)
                ctx.InvocationConfiguration.Output.WriteLine($"  {i + 1}. {greetings[i]}");
        });

        root.Add(helloCmd);
        root.Add(historyCmd);

        return root;
    }
}
