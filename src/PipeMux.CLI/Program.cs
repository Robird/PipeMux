using System.CommandLine;
using PipeMux.CLI;

var rootCommand = new RootCommand("PipeMux CLI - Universal frontend for PipeMux applications");

var appArgument = new Argument<string>("app", "Target application name (e.g., calculator)");
var argsArgument = new Argument<string[]>("args", "Command and arguments (e.g., add 10 20)") { Arity = ArgumentArity.ZeroOrMore };

rootCommand.AddArgument(appArgument);
rootCommand.AddArgument(argsArgument);

rootCommand.SetHandler(async (app, args) => {
    var client = new BrokerClient();
    var result = await client.SendRequestAsync(app, args);
    
    if (result.Success) {
        Console.WriteLine(result.Data ?? "(no output)");
        if (result.SessionId != null) {
            Console.WriteLine($"\n[Session: {result.SessionId}]");
        }
    }
    else {
        Console.Error.WriteLine($"Error: {result.Error}");
        Environment.ExitCode = 1;
    }
}, appArgument, argsArgument);

return await rootCommand.InvokeAsync(args);
