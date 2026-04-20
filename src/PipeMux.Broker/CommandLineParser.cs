using System.Text;

namespace PipeMux.Broker;

/// <summary>
/// 解析配置中的命令行，支持基本的引号和转义。
/// </summary>
internal static class CommandLineParser {
    public static IReadOnlyList<string> Parse(string commandLine) {
        if (string.IsNullOrWhiteSpace(commandLine)) {
            throw new ArgumentException("Command line cannot be empty.", nameof(commandLine));
        }

        var arguments = new List<string>();
        var current = new StringBuilder();
        var inSingleQuotes = false;
        var inDoubleQuotes = false;
        var isEscaping = false;
        var tokenStarted = false;

        foreach (var ch in commandLine) {
            if (isEscaping) {
                current.Append(ch);
                isEscaping = false;
                tokenStarted = true;
                continue;
            }

            if (ch == '\\' && !inSingleQuotes) {
                isEscaping = true;
                tokenStarted = true;
                continue;
            }

            if (inSingleQuotes) {
                if (ch == '\'') {
                    inSingleQuotes = false;
                }
                else {
                    current.Append(ch);
                }
                tokenStarted = true;
                continue;
            }

            if (inDoubleQuotes) {
                if (ch == '"') {
                    inDoubleQuotes = false;
                }
                else {
                    current.Append(ch);
                }
                tokenStarted = true;
                continue;
            }

            if (char.IsWhiteSpace(ch)) {
                if (tokenStarted) {
                    arguments.Add(current.ToString());
                    current.Clear();
                    tokenStarted = false;
                }
                continue;
            }

            if (ch == '\'') {
                inSingleQuotes = true;
                tokenStarted = true;
                continue;
            }

            if (ch == '"') {
                inDoubleQuotes = true;
                tokenStarted = true;
                continue;
            }

            current.Append(ch);
            tokenStarted = true;
        }

        if (isEscaping) {
            current.Append('\\');
        }

        if (inSingleQuotes || inDoubleQuotes) {
            throw new FormatException("Unterminated quoted string in command line.");
        }

        if (tokenStarted) {
            arguments.Add(current.ToString());
        }

        if (arguments.Count == 0) {
            throw new ArgumentException("Command line cannot be empty.", nameof(commandLine));
        }

        return arguments;
    }
}
