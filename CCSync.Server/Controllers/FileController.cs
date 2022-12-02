using CCSync.RPC;
using CCSync.Server.Services;
using CCSync.Server.Utils;
using CCSync.Shared.Utils;
using CCSync.Shared.Utils.Services;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Serilog;

namespace CCSync.Server.Controllers;

public sealed class FileController : FileService.FileServiceBase
{
    private readonly WorldProvider _worldProvider;
    private readonly FileListenerService _fileListenerService;
    private readonly ProtectedFilesService _protectedFilesService;
    public FileController(WorldProvider worldProvider, FileListenerService fileListenerService, ProtectedFilesService protectedFilesService)
    {
        _worldProvider = worldProvider;
        _fileListenerService = fileListenerService;
        _protectedFilesService = protectedFilesService;
    }

    public override async Task<Empty> OnClientFileChanged(IAsyncStreamReader<FileChanged> requestStream, ServerCallContext context)
    {
        try
        {
            var world = _worldProvider.GetWorld(context.RequestHeaders.GetValue("WorldId") ?? "");
            var authId = context.RequestHeaders.GetValue("AuthId");
            if (world is null)
            {
                context.Status = new Status(StatusCode.NotFound, "World not found");
                return new Empty();
            }

            if (string.IsNullOrEmpty(authId) || string.IsNullOrWhiteSpace(authId))
            {
                context.Status = new Status(StatusCode.Unauthenticated, "AuthId invalid");
                return new Empty();
            }

            var pathToComputer =
                await ComputerFinder.FindAsync(world.Path,
                    authId) + "/."; // This feels dirty (?). If i don't add this Uri#MakeRelativeUri returns the computer id

            string? GetPath(string? path)
            {
                if (string.IsNullOrEmpty(path)) return null;
                var t = Path.Combine(pathToComputer, path);

                if (!t.IsSubPathOf(pathToComputer))
                {
                    return null;
                }

                return t;
            }

            await foreach (var fileChange in requestStream.ReadAllAsync(context.CancellationToken))
            {
                var oldPathAbs = GetPath(fileChange.OldPath);
                var newPathAbs = GetPath(fileChange.NewPath);
                
                if (File.Exists(oldPathAbs) && !File.Exists(newPathAbs)) // DELETED
                {
                    if (Directory.Exists(oldPathAbs))
                    {
                        Log.Debug($"[->{fileChange.ChangeId}] Client deleted a remote directory {0}", fileChange.OldPath);

                        Directory.Delete(oldPathAbs, true);
                        continue;
                    }
                    Log.Debug($"[->{fileChange.ChangeId}] Client deleted a remote file {0}", fileChange.OldPath);

                    File.Delete(oldPathAbs);
                    continue;
                }
                
                if (_protectedFilesService.IsLocked(GetPath(fileChange.OldPath)))
                {
                    Log.Debug($"[->{fileChange.ChangeId}] Client modify request targets a locked file, skipping");
                    _protectedFilesService.UnlockPath(GetPath(fileChange.OldPath)!);
                    continue;    
                }

                if (_protectedFilesService.IsLocked(GetPath(fileChange.NewPath)))
                {
                    Log.Debug($"[->{fileChange.ChangeId}] Client modify request targets a locked file, skipping");
                    _protectedFilesService.UnlockPath(GetPath(fileChange.NewPath)!);
                    continue;
                }

                
                if (!File.Exists(oldPathAbs) && !string.IsNullOrEmpty(newPathAbs)) // CREATED
                {
                    _protectedFilesService.LockFile(newPathAbs);
                    
                    if (fileChange.IsDirectory == 1)
                    {
                        Log.Debug($"[->{fileChange.ChangeId}] Client creating a remote file {0}", fileChange.NewPath);
                        Directory.CreateDirectory(newPathAbs);
                        continue;
                    }
                    Log.Debug($"[->{fileChange.ChangeId}] Client creating a remote file {0}", fileChange.NewPath);
                    
                    await File.WriteAllBytesAsync(newPathAbs, fileChange.Contents.ToByteArray(),
                        context.CancellationToken);
                    continue;
                }



                if (File.Exists(oldPathAbs) && fileChange.OldPath != fileChange.NewPath) // MOVED
                {
                    Log.Debug($"[->{fileChange.ChangeId}] Client moved a remote file from {0} to {1}", fileChange.OldPath, fileChange.NewPath);
                    if (newPathAbs is null) continue;
                    _protectedFilesService.LockFile(newPathAbs);

                    File.Move(oldPathAbs, newPathAbs);
                    continue;
                }

                if (Directory.Exists(oldPathAbs) && fileChange.OldPath != fileChange.NewPath)
                {
                    Log.Debug($"[->{fileChange.ChangeId}] Client moved a remote directory from {0} to {1}", fileChange.OldPath, fileChange.NewPath);
                    if (newPathAbs is null) continue;
                    _protectedFilesService.LockFile(newPathAbs);

                    Directory.Move(oldPathAbs, newPathAbs);
                }
                
                if (string.IsNullOrEmpty(newPathAbs)) continue;
                
                _protectedFilesService.LockFile(newPathAbs);

                if (Directory.Exists(newPathAbs))
                {
                    Directory.CreateDirectory(newPathAbs);
                    continue;
                }
                
                await File.WriteAllBytesAsync(newPathAbs, fileChange.Contents.ToByteArray(),
                    context.CancellationToken);
            }
            Log.Information("Stopped listening for client changes");
        }
        catch(Exception error)
        {
            Log.Error(error, "Something has gone wrong");
            context.Status = new Status(StatusCode.Aborted, "Something has gone wrong");
        }
        
        return new Empty();
    }

    public override async Task OnServerFileChanged(Empty request, IServerStreamWriter<FileChanged> responseStream, ServerCallContext context)
    {
        try
        {
            var world = _worldProvider.GetWorld(context.RequestHeaders.GetValue("WorldId") ?? "");
            var authId = context.RequestHeaders.GetValue("AuthId");
            if (world is null)
            {
                context.Status = new Status(StatusCode.NotFound, "World not found");
                return;
            }

            if (string.IsNullOrEmpty(authId) || string.IsNullOrWhiteSpace(authId))
            {
                context.Status = new Status(StatusCode.Unauthenticated, "AuthId invalid");
                return;
            }

            var pathToComputer = await ComputerFinder.FindAsync(world.Path, authId);
            
            if (pathToComputer is null)
            {
                context.Status = new Status(StatusCode.Unauthenticated, "AuthId invalid");
                return;
            }

            var parentUri = new Uri(pathToComputer + "/.");
            _fileListenerService.Start(pathToComputer, context.CancellationToken);
            Log.Information("{0}:{1} is now listening for file changes", authId, world.Id);
            var changeId = 0;
            while (!context.CancellationToken.IsCancellationRequested)
            {
                try
                {
                    var (oldPath, newPath, isDirectory, sendContent) = await _fileListenerService.ListenAsync();
                    var msg = new FileChanged
                    {
                        IsDirectory = isDirectory,
                        ChangeId = changeId
                    };
                    changeId++;
                    if (oldPath is not null)
                    {
                        msg.OldPath = parentUri.MakeRelativeUri(new Uri(oldPath)).ToString();
                    }

                    if (newPath is not null)
                    {
                        msg.NewPath = parentUri.MakeRelativeUri(new Uri(newPath)).ToString();
                    }

                    if (_protectedFilesService.IsLocked(msg.OldPath))
                    {
                        Log.Debug($"[{msg.ChangeId}] Detected local change but file was locked, skipping");
                        _protectedFilesService.UnlockPath(msg.OldPath);
                        continue;
                    }

                    if (_protectedFilesService.IsLocked(msg.NewPath))
                    {
                        Log.Debug($"[{msg.ChangeId}] Detected local change but file was locked, skipping");
                        _protectedFilesService.UnlockPath(msg.NewPath);
                        continue;
                    }
                    
                    if (sendContent && !string.IsNullOrEmpty(newPath))
                    {
                        if (!File.Exists(newPath))
                        {
                            msg.NewPath = "";
                            // How did this happen?
                        }
                        else
                        {
                            await IOUtils.WaitForUnlock(newPath, context.CancellationToken);

                            var fileInfo = new FileInfo(newPath);
                            var fs = fileInfo.OpenRead();
                            msg.Contents = await ByteString.FromStreamAsync(fs);
                            fs.Close();
                            await fs.DisposeAsync();
                        }
                    }

                    await responseStream.WriteAsync(msg);
                }
                catch (TaskCanceledException)
                {

                }
            }
            Log.Information("Client stopped listening for changes");
        }
        catch(Exception error)
        {
            Log.Error(error, "Something has gone wrong");
            context.Status = new Status(StatusCode.Aborted, "Something has gone wrong");
        }
    }
}