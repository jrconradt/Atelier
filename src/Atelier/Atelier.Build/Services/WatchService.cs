using System.Collections.Concurrent;

namespace Atelier.Build.Services;

public class WatchService : IWatchService
{
    private readonly string _directory;
    private readonly string[] _patterns;
    private readonly int _debounceMs;
    private readonly ConcurrentBag<FileSystemWatcher> _watchers = [];
    private readonly System.Timers.Timer _debounceTimer;
    private readonly ConcurrentBag<string> _changedFiles = [];
    private bool _isDisposed;

    public event EventHandler<FileChangeEventArgs>? FilesChanged;

        public WatchService(string directory, string[] patterns, int debounceMs = 500)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _patterns = patterns ?? throw new ArgumentNullException(nameof(patterns));
        _debounceMs = debounceMs;

        _debounceTimer = new System.Timers.Timer(_debounceMs);
        _debounceTimer.AutoReset = false;
        _debounceTimer.Elapsed += OnDebounceElapsed;
    }

    public void Start()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(WatchService));
        }

        foreach (var pattern in _patterns)
        {
            var watcher = new FileSystemWatcher(_directory, pattern)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };

            watcher.Changed += OnFileChanged;
            watcher.Created += OnFileChanged;
            watcher.Deleted += OnFileChanged;
            watcher.Renamed += OnFileRenamed;

            _watchers.Add(watcher);
        }
    }

    public void Stop()
    {
        _debounceTimer.Stop();

        while (_watchers.TryTake(out var watcher))
        {
            watcher.EnableRaisingEvents = false;
            watcher.Changed -= OnFileChanged;
            watcher.Created -= OnFileChanged;
            watcher.Deleted -= OnFileChanged;
            watcher.Renamed -= OnFileRenamed;
            watcher.Dispose();
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {

        if (ShouldIgnoreFile(e.FullPath))
        {
            return;
        }

        _changedFiles.Add(e.FullPath);
        RestartDebounceTimer();
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        if (ShouldIgnoreFile(e.FullPath))
        {
            return;
        }

        _changedFiles.Add(e.FullPath);
        RestartDebounceTimer();
    }

    private void RestartDebounceTimer()
    {
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void OnDebounceElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {

        var changedFiles = new List<string>();
        while (_changedFiles.TryTake(out var file))
        {
            if (!changedFiles.Contains(file))
            {
                changedFiles.Add(file);
            }
        }

        if (changedFiles.Count > 0)
        {
            FilesChanged?.Invoke(this, new FileChangeEventArgs { ChangedFiles = changedFiles });
        }
    }

    private static bool ShouldIgnoreFile(string path)
    {
        var fileName = Path.GetFileName(path);

        if (fileName.StartsWith('.')
            || fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith("~", StringComparison.OrdinalIgnoreCase)
            || Atelier.Build.Utils.PathSegments.ContainsSegment(path, "obj")
            || Atelier.Build.Utils.PathSegments.ContainsSegment(path, "bin")
            || Atelier.Build.Utils.PathSegments.ContainsSegment(path, ".artifacts"))
        {
            return true;
        }

        return false;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        Stop();
        _debounceTimer.Dispose();
        _isDisposed = true;

        GC.SuppressFinalize(this);
    }
}
