namespace CCSync.Shared.Utils.Services;

public sealed class ProtectedFilesService
{
    private readonly List<string> _lockedFiles = new List<string>();
    
    public void LockFile(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        lock (_lockedFiles)
        {
            _lockedFiles.Add(path);
        }
    }

    public bool IsLocked(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        lock (_lockedFiles)
        {
            return _lockedFiles.Contains(path);
        }
    }

    public void UnlockPath(string path)
    {
        lock (_lockedFiles)
        {
            _lockedFiles.Remove(path);
        }
    }
}