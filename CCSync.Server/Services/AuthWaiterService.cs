using CCSync.Server.Entities;
using CCSync.Shared.Utils;

namespace CCSync.Server.Services;

public sealed class AuthWaiterService : IDisposable
{
    public const string AUTH_FILE_NAME = "ccsync";
    private readonly TaskCompletionSource _taskCompletionSource = new TaskCompletionSource();
    private FileSystemWatcher? _fileSystemWatcher;
    private CancellationToken _token = CancellationToken.None;
    private readonly SemaphoreSlim _checkSemaphore = new SemaphoreSlim(1);
    private Guid _authKey;
    
    public async Task WaitForAsync(World world, Guid authKey, CancellationToken token)
    {
        Init(world, authKey, token);
        try
        {
            await _taskCompletionSource.Task;
        }
        catch (TaskCanceledException)
        {
            // ignore
        }
    }
    
    private void Init(World world, Guid authKey, CancellationToken token)
    {
        token.Register(OnCancellationRequested);
        _token = token;
        _authKey = authKey;
        
        _fileSystemWatcher = new FileSystemWatcher(world.Path);
        _fileSystemWatcher.IncludeSubdirectories = true;
        _fileSystemWatcher.EnableRaisingEvents = true;
        
        _fileSystemWatcher.Changed += FileSystemWatcherOnChanged;
    }
    
    private async Task CheckChangesAsync(FileInfo fileInfo)
    {
        try
        {
            await _checkSemaphore.WaitAsync(_token);
            await IOUtils.WaitForUnlock(fileInfo.FullName, _token);

            var contents = await File.ReadAllTextAsync(fileInfo.FullName, _token);
            if (contents.Replace("\n", "") == _authKey.ToString())
            {
                _taskCompletionSource.SetResult();
            }
        }
        finally
        {
            _checkSemaphore.Release();
        }
    }
    
    private void FileSystemWatcherOnChanged(object sender, FileSystemEventArgs e)
    {
        Console.WriteLine(e.FullPath);
        if (_taskCompletionSource.Task.IsCompleted) return;
        if (Path.GetFileName(e.FullPath) != AUTH_FILE_NAME) return;
        _ = CheckChangesAsync(new FileInfo(e.FullPath));
    }

    private void OnCancellationRequested()
    {
        _taskCompletionSource.SetCanceled(CancellationToken.None); // Don't pass _token  because it's canceled
    }
    
    public void Dispose()
    {
        _checkSemaphore.Dispose();

        if (_taskCompletionSource.Task.Status != TaskStatus.Canceled)
        {
            _taskCompletionSource.TrySetCanceled();
        }
        
        if (_fileSystemWatcher is not null)
        {
            _fileSystemWatcher.EnableRaisingEvents = false;
            _fileSystemWatcher.Dispose();
        }
    }
}