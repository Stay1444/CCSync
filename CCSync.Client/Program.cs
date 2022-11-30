using CCSync.Client;

if (args.Length > 0)
{
    Directory.SetCurrentDirectory(args[0]);
}

var project = await ProjectLoader.LoadProjectAsync() ?? await ProjectSetup.Setup();

