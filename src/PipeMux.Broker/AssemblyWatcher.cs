namespace PipeMux.Broker;

/// <summary>
/// 监视程序集文件变化，防抖后触发回调。用于 auto_restart 功能。
/// </summary>
public sealed class AssemblyWatcher : IDisposable {
    public const int DefaultDebounceMs = 2000;

    private readonly string _assemblyPath;
    private readonly Func<Task> _onChanged;
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _debounceCts;
    private readonly object _lock = new();
    private volatile bool _disposed;

    public string AssemblyPath => _assemblyPath;

    public AssemblyWatcher(string assemblyPath, Func<Task> onChanged) {
        _assemblyPath = assemblyPath;
        _onChanged = onChanged;
    }

    public void Start() {
        if (_disposed) return;

        var directory = Path.GetDirectoryName(_assemblyPath);
        var filename = Path.GetFileName(_assemblyPath);

        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(filename)) {
            Console.Error.WriteLine($"[WARN] AssemblyWatcher: invalid path '{_assemblyPath}', skipping watch");
            return;
        }

        if (!Directory.Exists(directory)) {
            Console.Error.WriteLine($"[WARN] AssemblyWatcher: directory not found '{directory}', will watch when created");
        }

        lock (_lock) {
            if (_disposed) return;

            _watcher = new FileSystemWatcher(directory, filename) {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            _watcher.Changed += OnFileEvent;
            _watcher.Created += OnFileEvent;
            _watcher.Deleted += OnFileEvent;
            _watcher.Renamed += OnRenamed;
            _watcher.Error += OnWatcherError;

            Console.Error.WriteLine($"[INFO] AssemblyWatcher started for: {_assemblyPath} (debounce: {DefaultDebounceMs}ms)");
        }
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e) {
        if (_disposed) return;
        Console.Error.WriteLine($"[INFO] AssemblyWatcher: detected change in {_assemblyPath} ({e.ChangeType})");
        ScheduleDebouncedRestart();
    }

    private void OnRenamed(object sender, RenamedEventArgs e) {
        if (_disposed) return;
        Console.Error.WriteLine($"[INFO] AssemblyWatcher: detected rename in {_assemblyPath} ({e.OldName} -> {e.Name})");
        ScheduleDebouncedRestart();
    }

    private void OnWatcherError(object sender, ErrorEventArgs e) {
        Console.Error.WriteLine($"[WARN] AssemblyWatcher: internal error for {_assemblyPath}: {e.GetException().Message}");
    }

    private void ScheduleDebouncedRestart() {
        lock (_lock) {
            if (_disposed) return;

            _debounceCts?.Cancel();
            _debounceCts = new CancellationTokenSource();
            var token = _debounceCts.Token;

            _ = Task.Run(async () => {
                try {
                    await Task.Delay(DefaultDebounceMs, token);
                    if (!token.IsCancellationRequested && !_disposed) {
                        Console.Error.WriteLine($"[INFO] AssemblyWatcher: debounce elapsed, triggering restart for {_assemblyPath}");
                        await _onChanged();
                    }
                }
                catch (OperationCanceledException) {
                    // 被新的变化事件取消，正常行为
                }
                catch (Exception ex) {
                    Console.Error.WriteLine($"[ERROR] AssemblyWatcher: restart callback failed for {_assemblyPath}: {ex.Message}");
                }
            }, token);
        }
    }

    public void Dispose() {
        if (_disposed) return;

        lock (_lock) {
            if (_disposed) return;
            _disposed = true;

            _debounceCts?.Cancel();
            _debounceCts?.Dispose();

            if (_watcher != null) {
                _watcher.EnableRaisingEvents = false;
                _watcher.Changed -= OnFileEvent;
                _watcher.Created -= OnFileEvent;
                _watcher.Deleted -= OnFileEvent;
                _watcher.Renamed -= OnRenamed;
                _watcher.Error -= OnWatcherError;
                _watcher.Dispose();
                _watcher = null;
            }

            Console.Error.WriteLine($"[INFO] AssemblyWatcher stopped for: {_assemblyPath}");
        }
    }
}
