namespace Atelier.Build.Services;

public interface IWatchService : IDisposable
{
        public event EventHandler<FileChangeEventArgs>? FilesChanged;

        public void Start();

        public void Stop();
}

public class FileChangeEventArgs : EventArgs
{
    public IReadOnlyList<string> ChangedFiles { get; init; } = [];
}
