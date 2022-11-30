using System.Collections.Immutable;
using CCSync.Server.Entities;
using CCSync.Server.Utils;
using Serilog;

namespace CCSync.Server.Services;

public sealed class WorldProvider : IHostedService, IDisposable
{
    private const string FILE_NAME = "worlds.yaml";
    
    public ImmutableDictionary<string, World> Worlds { get; private set; } = ImmutableDictionary<string, World>.Empty;

    private FileSystemWatcher? _fileSystemWatcher;
    
    public World? GetWorld(string id)
        => Worlds.GetValueOrDefault(id);
    
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        Log.Information("World Service: Loading Worlds");
        await ReloadWorlds(cancellationToken);
        
        Log.Information("World Service: Creating File Watcher");
        _fileSystemWatcher = new FileSystemWatcher(".", FILE_NAME);
        _fileSystemWatcher.EnableRaisingEvents = true;
        _fileSystemWatcher.IncludeSubdirectories = false;
        Log.Information("World Service: Ready");
        
        _fileSystemWatcher.Error += FileSystemWatcherOnError;
        _fileSystemWatcher.Changed += FileSystemWatcherOnChanged;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_fileSystemWatcher is not null)
        {
            _fileSystemWatcher.Error -= FileSystemWatcherOnError;
            _fileSystemWatcher.Changed -= FileSystemWatcherOnChanged;
        }

        return Task.CompletedTask;
    }
    
    private async Task ReloadWorlds(CancellationToken cancellationToken)
    {
        try
        {
            var fileInfo = new FileInfo(FILE_NAME);
            while (IOUtils.IsFileLocked(fileInfo))
            {
                await Task.Delay(50, cancellationToken);
            }

            var worlds = await CCSyncYaml.ReadYamlAsync(FILE_NAME, () =>
            {
                var model = new Dictionary<string, World>
                {
                    {
                        "example-world", new World()
                        {
                            Name = "Example World",
                            Path = "/worlds/example-world"
                        }
                    }
                };
                return model;

            }, cancellationToken);
            foreach (var (id, world) in worlds)
            {
                var remove = false;

                if (string.IsNullOrEmpty(world.Name) || string.IsNullOrWhiteSpace(world.Name))
                {
                    Log.Error("World Service: Failed to load world {0}: {1}", id, "World name can't be blank");
                    remove = true;
                }
                else if (string.IsNullOrEmpty(world.Path) || string.IsNullOrWhiteSpace(world.Path))
                {
                    Log.Error("World Service: Failed to load world {0}: {1}", id, "World path can't be blank");
                    remove = true;
                }
                else if (!Directory.Exists(world.Path))
                {
                    Log.Error("World Service: Failed to load world {0}: {1}", id, "World folder does not exist");
                    remove = true;
                }
                else if (string.IsNullOrEmpty(id) || string.IsNullOrWhiteSpace(id))
                {
                    Log.Error("World Service: Failed to load world: {0}", "World id can't be blank");
                    remove = true;
                }

                if (remove)
                {
                    worlds.Remove(id);
                }
            }

            Worlds = worlds.ToImmutableDictionary();
            Log.Information("World Service: {0} world (re)loaded correctly", Worlds.Count);
        }
        catch (Exception error)
        {
            Log.Error("World Service: Unexpected error while loading worlds {0}", error);
        }
    }

    private void FileSystemWatcherOnError(object sender, ErrorEventArgs e)
    {
        Log.Information("World Service: File Watcher Error {0}", e.GetException());
    }
    
    private void FileSystemWatcherOnChanged(object sender, FileSystemEventArgs e)
    {
        // Event may trigger twice but there is nothing that i can do about that
        if (e.Name != FILE_NAME) return;
        ReloadWorlds(CancellationToken.None).Wait();
    }
    
    public void Dispose()
    {
        if (_fileSystemWatcher is null) return;
        _fileSystemWatcher.EnableRaisingEvents = false;
        _fileSystemWatcher.Dispose();
    }
}