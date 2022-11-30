using CCSync.Server.Services;

namespace CCSync.Server.Utils;

public static class ComputerFinder
{
    public static async Task<string?> FindAsync(string root, string authId)
    {
        foreach (var directory in Directory.GetDirectories(root))
        {
            var path = Path.Combine(root, directory, AuthWaiterService.AUTH_FILE_NAME);
            if (File.Exists(path))
            {
                if ((await File.ReadAllTextAsync(path)).Replace("\n","") == authId)
                {
                    return path;
                }
            }
        }

        return null;
    }
}