using PipeMux.Broker;
using PipeMux.Shared.Protocol;

Console.Error.WriteLine("[INFO] PipeMux.Broker starting...");

try {
    JsonRpc.VerifyRuntimeAvailable();
}
catch (Exception ex) {
    Console.Error.WriteLine($"[FATAL] JSON protocol initialization failed: {ex.Message}");
    return 1;
}

BrokerConfig config;
try {
    config = ConfigLoader.Load();
}
catch (Exception ex) {
    Console.Error.WriteLine($"[FATAL] {ex.Message}");
    Console.Error.WriteLine("[FATAL] Fix broker.toml and restart the broker service.");
    return 1;
}

Console.Error.WriteLine($"[INFO] Loaded config: {config.Apps.Count} apps registered");

var registry = new ProcessRegistry();
var coordinator = new BrokerCoordinator(config, registry);
var broker = new BrokerServer(config.Broker, coordinator);

Console.Error.WriteLine("[INFO] Press Ctrl+C to stop");

// 设置 Ctrl+C 处理
var cts = new CancellationTokenSource();
Console.CancelKeyPress += (s, e) => {
    Console.Error.WriteLine("[INFO] Received shutdown signal...");
    e.Cancel = true;
    cts.Cancel();
};

try {
    await broker.StartAsync(cts.Token);
}
catch (OperationCanceledException) {
    Console.Error.WriteLine("[INFO] Broker stopped gracefully");
}

return 0;
