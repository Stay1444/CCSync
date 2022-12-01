using CCSync.RPC;
using CCSync.Shared.Utils.Services;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Spectre.Console;

namespace CCSync.Client;

sealed class RemoteListener
{
    private ProtectedFilesService _protectedFilesService;

    public RemoteListener(ProtectedFilesService protectedFilesService)
    {
        _protectedFilesService = protectedFilesService;
    }

    public async Task ListenAsync(CCSyncProject project, FileService.FileServiceClient fileClient, CancellationTokenSource cts)
    {
        var token = cts.Token;
        try
        {
            var streamingCall = fileClient.OnServerFileChanged(new Empty(), new Metadata()
            {
                {"WorldId", project.World},
                {"AuthId", project.Auth.ToString()}
            });
            await foreach (var fileChange in streamingCall.ResponseStream.ReadAllAsync(token))
            {
                if (_protectedFilesService.IsLocked(fileChange.OldPath))
                {
                    Console.WriteLine("Remote modify request targets a locked file, skipping");
                    continue;    
                }

                if (_protectedFilesService.IsLocked(fileChange.NewPath))
                {
                    Console.WriteLine("Remote modify request targets a locked file, skipping");
                    continue;
                }
                
                if (!File.Exists(fileChange.OldPath) && !string.IsNullOrEmpty(fileChange.NewPath)) // CREATED
                {
                    _protectedFilesService.LockFile(fileChange.NewPath);


                    if (fileChange.IsDirectory == 1)
                    {
                        Directory.CreateDirectory(fileChange.NewPath);
                        AnsiConsole.WriteLine($"Remote created new directory: {fileChange.NewPath}");
                        continue;
                    }
                    
                    AnsiConsole.WriteLine($"Remote created new file: {fileChange.NewPath}");
                    await File.WriteAllBytesAsync(fileChange.NewPath, fileChange.Contents.ToByteArray(), token);
                    continue;
                }

                if (string.IsNullOrEmpty(fileChange.NewPath?.Trim())) // DELETED
                {
                    _protectedFilesService.LockFile(fileChange.OldPath);
                    
                    if (Directory.Exists(fileChange.NewPath))
                    {
                        AnsiConsole.WriteLine($"Remote deleted directory: {fileChange.OldPath}");
                        Directory.Delete(fileChange.NewPath);
                        continue;                        
                    }
                    
                    if (!File.Exists(fileChange.OldPath)) continue;

                    AnsiConsole.WriteLine($"Remote deleted file: {fileChange.OldPath}");

                    
                    File.Delete(fileChange.OldPath);
                    continue;
                }

                if (File.Exists(fileChange.OldPath) && fileChange.OldPath != fileChange.NewPath) // MOVED
                {
                    _protectedFilesService.LockFile(fileChange.NewPath);

                    AnsiConsole.WriteLine($"Remote moved file from {fileChange.OldPath} to {fileChange.NewPath}");

                    File.Move(fileChange.OldPath, fileChange.NewPath ?? fileChange.OldPath);
                    continue;
                }
                
                if (Directory.Exists(fileChange.OldPath)  && fileChange.OldPath != fileChange.NewPath)
                {
                    _protectedFilesService.LockFile(fileChange.NewPath);

                    AnsiConsole.WriteLine($"Remote moved directory from {fileChange.OldPath} to {fileChange.NewPath}");

                    Directory.Move(fileChange.OldPath, fileChange.NewPath);
                    continue;
                }
                
                _protectedFilesService.LockFile(fileChange.NewPath);

                if (fileChange.IsDirectory == 1)
                {
                    AnsiConsole.WriteLine($"Remote updated directory {fileChange.OldPath}");
                    Directory.CreateDirectory(fileChange.NewPath);
                    continue;
                }
                
                AnsiConsole.WriteLine($"Remote updated file {fileChange.OldPath}");
                await File.WriteAllBytesAsync(fileChange.NewPath!, fileChange.Contents.ToByteArray(), token);
            }
        }
        catch(Exception error)
        {
            Console.WriteLine(error);
        }
        finally
        {
            cts.Cancel();
            Console.WriteLine("Listener for remote changes stopped");
        }
    }
}