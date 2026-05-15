namespace PipeMux.Broker;

internal static class ProcessInstanceKey {
    public static string Build(string appName, string? terminalId) {
        return !string.IsNullOrEmpty(terminalId)
            ? $"{appName}:{terminalId}"
            : appName;
    }

    public static ProcessInstanceKeyParts Parse(string processKey) {
        var separatorIndex = processKey.IndexOf(':');
        if (separatorIndex < 0) {
            return new ProcessInstanceKeyParts(processKey, null);
        }

        return new ProcessInstanceKeyParts(
            processKey[..separatorIndex],
            processKey[(separatorIndex + 1)..]);
    }
}

internal readonly record struct ProcessInstanceKeyParts(string AppName, string? TerminalId);
