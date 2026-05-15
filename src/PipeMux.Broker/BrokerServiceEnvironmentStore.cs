using System.Text;
using PipeMux.Shared;

namespace PipeMux.Broker;

/// <summary>
/// 管理 broker 的 systemd EnvironmentFile 及其 drop-in。
/// </summary>
internal sealed class BrokerServiceEnvironmentStore {
    private const string DropInContent =
        """
        [Service]
        EnvironmentFile=-%h/.config/pipemux/broker.env
        """;

    private readonly string _environmentFilePath;
    private readonly string _dropInFilePath;

    public BrokerServiceEnvironmentStore(string? environmentFilePath = null, string? dropInFilePath = null) {
        _environmentFilePath = environmentFilePath ?? BrokerConnectionDefaults.GetBrokerEnvironmentFilePath();
        _dropInFilePath = dropInFilePath ?? BrokerConnectionDefaults.GetBrokerEnvironmentDropInPath();
    }

    public BrokerServiceEnvironmentUpdateResult CopyFromCliEnvironment(
        IReadOnlyList<string> requestedNames,
        IReadOnlyDictionary<string, string>? copiedValues
    ) {
        var normalizedNames = requestedNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (normalizedNames.Length == 0) {
            throw new InvalidOperationException("At least one environment variable name is required.");
        }

        var invalidNames = normalizedNames
            .Where(name => !IsValidEnvironmentVariableName(name))
            .ToArray();
        if (invalidNames.Length > 0) {
            throw new InvalidOperationException(
                $"Invalid environment variable name(s): {string.Join(", ", invalidNames)}");
        }

        var availableValues = copiedValues ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var namesToWrite = normalizedNames
            .Where(availableValues.ContainsKey)
            .ToArray();
        var missingNames = normalizedNames
            .Where(name => !availableValues.ContainsKey(name))
            .ToArray();

        if (namesToWrite.Length == 0) {
            throw new InvalidOperationException(
                $"None of the requested environment variables are set in the current CLI environment: {string.Join(", ", normalizedNames)}");
        }

        var currentValues = LoadEnvironmentFile();
        foreach (var name in namesToWrite) {
            currentValues[name] = availableValues[name];
        }

        SaveEnvironmentFile(currentValues);
        var dropInCreated = EnsureDropInFile();

        return new BrokerServiceEnvironmentUpdateResult {
            EnvironmentFilePath = _environmentFilePath,
            DropInFilePath = _dropInFilePath,
            CopiedNames = namesToWrite,
            MissingNames = missingNames,
            DropInCreated = dropInCreated
        };
    }

    private Dictionary<string, string> LoadEnvironmentFile() {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        if (!File.Exists(_environmentFilePath)) {
            return values;
        }

        foreach (var rawLine in File.ReadAllLines(_environmentFilePath)) {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0) {
                continue;
            }

            var name = line[..separator].Trim();
            var valueText = line[(separator + 1)..].Trim();

            if (!IsValidEnvironmentVariableName(name)) {
                continue;
            }

            values[name] = UnescapeEnvironmentValue(valueText);
        }

        return values;
    }

    private void SaveEnvironmentFile(Dictionary<string, string> values) {
        var builder = new StringBuilder();
        foreach (var (name, value) in values.OrderBy(kv => kv.Key, StringComparer.Ordinal)) {
            builder.Append(name);
            builder.Append('=');
            builder.Append('"');
            builder.Append(EscapeEnvironmentValue(value));
            builder.AppendLine("\"");
        }

        WriteFileAtomically(_environmentFilePath, builder.ToString(), ownerReadWriteOnly: true);
    }

    private bool EnsureDropInFile() {
        var existingContent = File.Exists(_dropInFilePath)
            ? File.ReadAllText(_dropInFilePath)
            : null;

        if (string.Equals(existingContent, DropInContent, StringComparison.Ordinal)) {
            return false;
        }

        WriteFileAtomically(_dropInFilePath, DropInContent, ownerReadWriteOnly: false);
        return true;
    }

    private static void WriteFileAtomically(string path, string content, bool ownerReadWriteOnly) {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) {
            Directory.CreateDirectory(directory);
        }

        var tempFile = Path.Combine(directory ?? ".", $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try {
            File.WriteAllText(tempFile, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            if (File.Exists(path)) {
                File.Replace(tempFile, path, destinationBackupFileName: null);
            }
            else {
                File.Move(tempFile, path);
            }

            if (ownerReadWriteOnly && !OperatingSystem.IsWindows()) {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        finally {
            if (File.Exists(tempFile)) {
                File.Delete(tempFile);
            }
        }
    }

    private static bool IsValidEnvironmentVariableName(string name) {
        if (string.IsNullOrWhiteSpace(name)) {
            return false;
        }

        if (!(char.IsLetter(name[0]) || name[0] == '_')) {
            return false;
        }

        return name.All(ch => char.IsLetterOrDigit(ch) || ch == '_');
    }

    private static string EscapeEnvironmentValue(string value) {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value) {
            builder.Append(ch switch {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ => ch
            });
        }

        return builder.ToString();
    }

    private static string UnescapeEnvironmentValue(string valueText) {
        if (valueText.Length >= 2 && valueText[0] == '"' && valueText[^1] == '"') {
            var inner = valueText[1..^1];
            var builder = new StringBuilder(inner.Length);

            for (var i = 0; i < inner.Length; i++) {
                var ch = inner[i];
                if (ch != '\\' || i + 1 >= inner.Length) {
                    builder.Append(ch);
                    continue;
                }

                i++;
                builder.Append(inner[i] switch {
                    '\\' => '\\',
                    '"' => '"',
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    _ => inner[i]
                });
            }

            return builder.ToString();
        }

        return valueText;
    }
}

internal sealed class BrokerServiceEnvironmentUpdateResult {
    public required string EnvironmentFilePath { get; init; }
    public required string DropInFilePath { get; init; }
    public required IReadOnlyList<string> CopiedNames { get; init; }
    public required IReadOnlyList<string> MissingNames { get; init; }
    public required bool DropInCreated { get; init; }
}
