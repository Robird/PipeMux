# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Common commands

- Build: `dotnet build PipeMux.sln --nologo`
- Management command parser tests: `bash tests/test-management-command-parse.sh`
- Broker E2E tests: `bash tests/test-management-commands-e2e.sh`
- Quick smoke test: `bash test-e2e.sh`

No dedicated lint command — rely on `dotnet build` (warnings as errors is not enabled by default).

## Architecture overview

PipeMux is a local process orchestration framework for LLM Agents. A Broker hosts long-lived, stateful CLI apps; a frontend CLI (`pmux`) routes requests to them via Named Pipe or Unix Domain Socket.

**Three layers:**
- **PipeMux.CLI** — `pmux <app> <command> [args...]` frontend
- **PipeMux.Broker** — daemon that manages app processes, routes requests, persists config
- **PipeMux Apps** — stateful apps built with `PipeMux.Sdk`, or DLLs loaded via `PipeMux.Host` reflection

**Communication:** CLI ↔ Broker via Named Pipe / Unix Domain Socket (JSON). Broker ↔ App via JSON-RPC over stdin/stdout (StreamJsonRpc with `NewLineDelimitedMessageHandler`).

AGENTS.md has the full architecture contracts (config persistence chain, management command chain, endpoint resolution, launcher constraints). Read it before modifying core flows.

## Critical API patterns

### System.CommandLine 2.0.6

This API version has specific patterns that differ from both earlier betas and .NET docs:

```csharp
// Adding items — use .Add, not AddArgument/AddOption/AddCommand
cmd.Arguments.Add(new Argument<string>("name", () => "default") { Description = "..." });
cmd.Options.Add(new Option<bool>("--flag") { Description = "..." });
cmd.Subcommands.Add(subCmd);

// SetAction with async handler: (ParseResult, CancellationToken) => Task<int>
cmd.SetAction(async (parseResult, ct) => {
    var name = parseResult.GetValue<string>("name");
    parseResult.InvocationConfiguration.Output.WriteLine("hello");
    return 0;
});

// Invocation — do NOT call rootCommand.InvokeAsync(args)
await rootCommand.Parse(args).InvokeAsync(invocationConfig);

// I/O redirects use InvocationConfiguration (not CommandLineConfiguration)
var config = new InvocationConfiguration { Output = writer, Error = writer };
await rootCommand.Parse(args).InvokeAsync(config);

// Access output in handlers via parseResult.InvocationConfiguration.Output
// parseResult.Configuration is ParserConfiguration — it has NO Output/Error
```

### StreamJsonRpc

Apps use `NewLineDelimitedMessageHandler` (not the LSP header-prefixed style). `PipeMuxApp.cs` in PipeMux.Sdk is the canonical setup.

### Tomlyn 0.17.x

Field mapping defaults to PascalCase → snake_case (e.g., C# `AutoStart` ↔ TOML `auto_start`). `BrokerConfigTomlCodec` is the sole codec.

## Key invariants when modifying code

- **`BrokerCoordinator` is the only lock holder** for config lookup, process start/stop, and management commands.
- **`BrokerConfigStore`** is a memory view + atomic disk persistence layer — it holds no locks. Writers go to disk first, then commit to memory.
- **Endpoint resolution** via `BrokerConnectionResolver` must remain symmetric for server and client. Env vars `PIPEMUX_SOCKET_PATH` / `PIPEMUX_PIPE_NAME` take priority.
- **Default values** go in `BrokerConnectionDefaults`, not duplicated in Broker/CLI.
- **`ManagementCommand.Parse`** does token-level parsing only (two-pass: options first, then positionals). Semantic validation belongs in `HostRegistrationRequest`.
