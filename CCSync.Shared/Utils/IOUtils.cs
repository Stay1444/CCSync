namespace CCSync.Shared.Utils;

public static class IOUtils
{
    public static async Task WaitForUnlock(string path, CancellationToken token)
    {
        var fileInfo = new FileInfo(path);
        while (!token.IsCancellationRequested)
        {
            try
            {
                var fs = fileInfo.OpenRead();
                fs.Close();
                await fs.DisposeAsync();
                break;
            }
            catch
            {
                if (!File.Exists(path)) break;
                Console.WriteLine("File is locked, waiting 50ms");
                await Task.Delay(50, token);
            }
        }
    }
    
    private static bool IsFileLocked(FileInfo file)
    {
        if (!file.Exists) return false;
        
        FileStream stream = null;

        try
        {
            stream = file.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            //the file is unavailable because it is:
            //still being written to
            //or being processed by another thread
            //or does not exist (has already been processed)
            return true;
        }
        finally
        {
            stream?.Close();
        }

        //file is not locked
        return false;
    }
    
    public static bool IsSubPathOf(this string subPath, string basePath) {
        var rel = Path.GetRelativePath(basePath, subPath);
        return rel != "."
               && rel != ".."
               && !rel.StartsWith("../")
               && !rel.StartsWith(@"..\")
               && !Path.IsPathRooted(rel);
    }
}