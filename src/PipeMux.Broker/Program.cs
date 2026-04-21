using PipeMux.Broker;

Console.Error.WriteLine("[INFO] PipeMux.Broker starting...");

var config = ConfigLoader.Load();
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
