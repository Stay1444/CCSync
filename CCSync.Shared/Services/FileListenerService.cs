namespace CCSync.Shared.Utils.Services;

public sealed class FileListenerService : IDisposable
{
    private CancellationToken _token;
    private FileSystemWatcher? _fileSystemWatcher;
    private TaskCompletionSource<(string? oldPath, string? newPath, int isDirectory, bool sendChanges)>? _taskCompletionSource;

    private readonly List<FileSystemEventArgs> _queuedEventArgs = new List<FileSystemEventArgs>();

    private readonly SemaphoreSlim _handleSemaphore = new SemaphoreSlim(1);
    private readonly SemaphoreSlim _listenSemaphore = new SemaphoreSlim(1);
    public void Start(string directory, CancellationToken token)
    {
        _token = token;
        token.Register(OnCancellationRequested);
        
        this._fileSystemWatcher = new FileSystemWatcher(directory, "*.*");
        _fileSystemWatcher.IncludeSubdirectories = true;
        _fileSystemWatcher.EnableRaisingEvents = true;
        _fileSystemWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName |
                                          NotifyFilters.DirectoryName | NotifyFilters.CreationTime |
                                          NotifyFilters.Size | NotifyFilters.LastAccess;
        
        _fileSystemWatcher.Deleted += FileSystemWatcherOnDeleted;
        _fileSystemWatcher.Renamed += FileSystemWatcherOnRenamed;
        _fileSystemWatcher.Changed += FileSystemWatcherOnChanged;
    }

    private void Begin()
    {
        _taskCompletionSource = new();
    }

    private void End()
    {
        
        _taskCompletionSource = null;
    }
    
    public async Task<(string? oldPath, string? newPath, int isDirectory, bool sendContent)> ListenAsync()
    {
        try
        {
            await _listenSemaphore.WaitAsync(_token);
            Begin();
            
            Task? previousHandle = null;
            lock (_queuedEventArgs)
            {
                if (_queuedEventArgs.Count > 0)
                {
                    previousHandle = HandleChangesAsync(_queuedEventArgs[0]);
                    _queuedEventArgs.RemoveAt(0);
                }
            }

            if (previousHandle is not null) await previousHandle;

            
            return await _taskCompletionSource!.Task;
        }
        finally
        {
            End();
            _listenSemaphore.Release();
        }
    }

    private void OnCancellationRequested()
    {
        _taskCompletionSource?.TrySetCanceled(CancellationToken.None); // Don't pass _token  because it's canceled
    }
    
    private async Task HandleChangesAsync(FileSystemEventArgs e)
    {
        try
        {
            await _handleSemaphore.WaitAsync(_token);

            if (_taskCompletionSource is null) return;

            if (e is RenamedEventArgs renamedEventArgs)
            {
                _taskCompletionSource.TrySetResult((renamedEventArgs.OldFullPath, renamedEventArgs.FullPath, IsDirectory(renamedEventArgs.FullPath) ? 1 : 0, false));
                return;
            }
        
            if (e.ChangeType == WatcherChangeTypes.Deleted)
            {
                _taskCompletionSource.TrySetResult((e.FullPath, null, 2, false));
                return;
            }

            if (e.ChangeType == WatcherChangeTypes.Created)
            {
                _taskCompletionSource.TrySetResult((null, e.FullPath, IsDirectory(e.FullPath) ? 1 : 0, true));
                return;
            }

            if (e.ChangeType == WatcherChangeTypes.Changed)
            {
                _taskCompletionSource.TrySetResult((e.FullPath, e.FullPath, IsDirectory(e.FullPath) ? 1 : 0, true));
            }
        }
        finally
        {
            _handleSemaphore.Release();
        }
    }

    private static bool IsDirectory(string path)
    {
        try
        {
            var info = File.GetAttributes(path);
            return info.HasFlag(FileAttributes.Directory);
        }
        catch
        {
            return false;
        }
    }

    private void FileSystemWatcherOnChanged(object sender, FileSystemEventArgs e)
    {
        lock (_queuedEventArgs)
        {
            if (_taskCompletionSource is null)
            {
                _queuedEventArgs.Add(e);
                return;
            }
        }
        HandleChangesAsync(e).Wait(_token);
    }

    private void FileSystemWatcherOnRenamed(object sender, RenamedEventArgs e)
    {
        lock (_queuedEventArgs)
        {
            if (_taskCompletionSource is null)
            {
                _queuedEventArgs.Add(e);
                return;
            }
        }
        HandleChangesAsync(e).Wait(_token);
    }

    private void FileSystemWatcherOnDeleted(object sender, FileSystemEventArgs e)
    {
        lock (_queuedEventArgs)
        {
            if (_taskCompletionSource is null)
            {
                _queuedEventArgs.Add(e);
                return;
            }
        }
        HandleChangesAsync(e).Wait(_token);
    }
    
    public void Dispose()
    {
        _handleSemaphore.Dispose();
        
        if (_taskCompletionSource?.Task.Status != TaskStatus.Canceled)
        {
            _taskCompletionSource?.TrySetCanceled();
        }
        
        if (_fileSystemWatcher is not null)
        {
            _fileSystemWatcher.EnableRaisingEvents = false;
            _fileSystemWatcher.Dispose();
        }
    }
}