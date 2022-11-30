namespace CCSync.Client;

static class ProjectLoader
{
    public const string PROJECT_FOLDER = ".ccsync";

    public static async Task<CCSyncProject?> LoadProjectAsync()
    {
        if (!Directory.Exists(PROJECT_FOLDER))
        {
            return null;
        }

        var auth = Guid.Empty;
        var world = "";
        var origin = "";
        
        if (File.Exists(GetProjectFile("auth")))
        {
            auth = Guid.Parse(await File.ReadAllTextAsync(GetProjectFile("auth")));
        }

        if (File.Exists(GetProjectFile("world")))
        {
            world = await File.ReadAllTextAsync(GetProjectFile("world"));
        }

        if (File.Exists(GetProjectFile("origin")))
        {
            origin = await File.ReadAllTextAsync(GetProjectFile("origin"));
        }
        
        return new CCSyncProject()
        {
            Auth = auth,
            World = world,
            Origin = origin
        };
    }

    private static string GetProjectFile(params string[] args)
    {
        return Path.Combine(PROJECT_FOLDER, Path.Combine(args));
    }

    public static async Task CreateDefaultProjectAsync(string origin, Guid auth, string world)
    {
        var projectFolder = new DirectoryInfo(PROJECT_FOLDER);
        projectFolder.Create();
        projectFolder.Attributes = FileAttributes.Hidden;
        
        await File.WriteAllTextAsync(GetProjectFile("auth"), auth.ToString());
        await File.WriteAllTextAsync(GetProjectFile("world"), world);
        await File.WriteAllTextAsync(GetProjectFile("origin"), origin);
    }
}