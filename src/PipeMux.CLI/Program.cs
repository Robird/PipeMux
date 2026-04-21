using System.CommandLine;
using PipeMux.CLI;
using PipeMux.Shared.Protocol;

// 检查是否为管理命令（以 : 开头）
if (args.Length > 0 && ManagementCommand.IsManagementCommand(args[0])) {
    var managementArgs = args.Length > 1 ? args[1..] : Array.Empty<string>();
    var command = ManagementCommand.Parse(args[0], managementArgs);
    
    if (command == null) {
        Console.Error.WriteLine($"Unknown management command: {args[0]}");
        Console.Error.WriteLine("Use 'pmux :help' for available commands");
        return 1;
    }
    
    var client = new BrokerClient();
    var result = await client.SendManagementCommandAsync(command);
    
    if (result.Success) {
        Console.WriteLine(result.Data ?? "(no output)");
    }
    else {
        Console.Error.WriteLine($"Error: {result.Error}");
        return 1;
    }
    return 0;
}

// 原有的应用调用逻辑
var rootCommand = new RootCommand("PipeMux CLI - Universal frontend for PipeMux applications");

var appArgument = new Argument<string>("app") { Description = "Target application name (e.g., calculator)" };
var argsArgument = new Argument<string[]>("args") {
    Description = "Command and arguments (e.g., add 10 20)",
    Arity = ArgumentArity.ZeroOrMore
};

rootCommand.Arguments.Add(appArgument);
rootCommand.Arguments.Add(argsArgument);

rootCommand.SetAction(async (ParseResult parseResult, CancellationToken ct) => {
    var app = parseResult.GetValue(appArgument)!;
    var appArgs = parseResult.GetValue(argsArgument) ?? [];
    var client = new BrokerClient();
    var result = await client.SendRequestAsync(app, appArgs);

    if (result.Success) {
        Console.WriteLine(result.Data ?? "(no output)");
        return 0;
    }

    Console.Error.WriteLine($"Error: {result.Error}");
    return 1;
});

return await rootCommand.Parse(args).InvokeAsync();
