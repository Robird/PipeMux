using PipeMux.Shared.Protocol;

var tests = new (string Name, Action Run)[] {
    ("register accepts --host-path before positional args", RegisterParsesWithLeadingHostPath),
    ("register accepts --host-path after positional args", RegisterParsesWithTrailingHostPath),
    ("register keeps --host alias compatible", RegisterParsesWithLegacyHostAlias),
    ("unregister accepts --stop before target app", UnregisterParsesWithLeadingStopFlag),
    ("unregister accepts --stop after target app", UnregisterParsesWithTrailingStopFlag),
    ("register rejects missing host-path value", RegisterRejectsMissingHostPathValue),
    ("register rejects unknown option", RegisterRejectsUnknownOption),
    ("unregister rejects unknown option", UnregisterRejectsUnknownOption),
    ("register rejects missing positional args", RegisterRejectsMissingPositionals),
};

var failures = new List<string>();

foreach (var (name, run) in tests) {
    try {
        run();
        Console.WriteLine($"[PASS] {name}");
    }
    catch (Exception ex) {
        failures.Add($"{name}: {ex.Message}");
        Console.Error.WriteLine($"[FAIL] {name}: {ex.Message}");
    }
}

if (failures.Count > 0) {
    Console.Error.WriteLine();
    Console.Error.WriteLine($"{failures.Count} test(s) failed.");
    Environment.Exit(1);
}

Console.WriteLine();
Console.WriteLine($"{tests.Length} parser test(s) passed.");

static void RegisterParsesWithLeadingHostPath() {
    var command = RequireParsed(":register", "--host-path", "/tmp/pipemux-host", "counter", "Counter.dll", "Demo.Build");
    AssertEqual(ManagementCommandKind.Register, command.Kind, "Kind");
    AssertEqual("counter", command.TargetApp, "TargetApp");
    AssertEqual("Counter.dll", command.TargetAssemblyPath, "TargetAssemblyPath");
    AssertEqual("Demo.Build", command.TargetMethodName, "TargetMethodName");
    AssertEqual("/tmp/pipemux-host", command.HostPath, "HostPath");
}

static void RegisterParsesWithTrailingHostPath() {
    var command = RequireParsed(":register", "counter", "Counter.dll", "Demo.Build", "--host-path", "/tmp/pipemux-host");
    AssertEqual(ManagementCommandKind.Register, command.Kind, "Kind");
    AssertEqual("counter", command.TargetApp, "TargetApp");
    AssertEqual("Counter.dll", command.TargetAssemblyPath, "TargetAssemblyPath");
    AssertEqual("Demo.Build", command.TargetMethodName, "TargetMethodName");
    AssertEqual("/tmp/pipemux-host", command.HostPath, "HostPath");
}

static void RegisterParsesWithLegacyHostAlias() {
    var command = RequireParsed(":register", "counter", "Counter.dll", "Demo.Build", "--host", "/tmp/pipemux-host");
    AssertEqual(ManagementCommandKind.Register, command.Kind, "Kind");
    AssertEqual("/tmp/pipemux-host", command.HostPath, "HostPath");
}

static void UnregisterParsesWithLeadingStopFlag() {
    var command = RequireParsed(":unregister", "--stop", "counter");
    AssertEqual(ManagementCommandKind.Unregister, command.Kind, "Kind");
    AssertEqual("counter", command.TargetApp, "TargetApp");
    AssertTrue(command.Flag, "Flag");
}

static void UnregisterParsesWithTrailingStopFlag() {
    var command = RequireParsed(":unregister", "counter", "--stop");
    AssertEqual(ManagementCommandKind.Unregister, command.Kind, "Kind");
    AssertEqual("counter", command.TargetApp, "TargetApp");
    AssertTrue(command.Flag, "Flag");
}

static void RegisterRejectsMissingHostPathValue() {
    AssertNull(Parse(":register", "counter", "Counter.dll", "Demo.Build", "--host-path"));
}

static void RegisterRejectsUnknownOption() {
    AssertNull(Parse(":register", "counter", "Counter.dll", "Demo.Build", "--host-pth", "/tmp/pipemux-host"));
}

static void UnregisterRejectsUnknownOption() {
    AssertNull(Parse(":unregister", "counter", "--force"));
}

static void RegisterRejectsMissingPositionals() {
    AssertNull(Parse(":register", "counter", "Counter.dll"));
}

static ManagementCommand? Parse(string input, params string[] args) {
    return ManagementCommand.Parse(input, args);
}

static ManagementCommand RequireParsed(string input, params string[] args) {
    return Parse(input, args) ?? throw new InvalidOperationException("Expected command to parse successfully, but got null.");
}

static void AssertNull(ManagementCommand? command) {
    if (command is not null) {
        throw new InvalidOperationException($"Expected parse failure, but got {command.Kind}.");
    }
}

static void AssertTrue(bool value, string fieldName) {
    if (!value) {
        throw new InvalidOperationException($"{fieldName}: expected true, got false.");
    }
}

static void AssertEqual<T>(T expected, T actual, string fieldName) {
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) {
        throw new InvalidOperationException($"{fieldName}: expected '{expected}', got '{actual}'.");
    }
}
