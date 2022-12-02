using CCSync.RPC;
using CCSync.Shared.Utils;
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
                if (string.IsNullOrEmpty(fileChange.NewPath?.Trim())) // DELETED
                {
                    if (Directory.Exists(fileChange.NewPath))
                    {
                        AnsiConsole.WriteLine(
                            $"[->{fileChange.ChangeId}] Remote deleted directory: {fileChange.OldPath}");
                        Directory.Delete(fileChange.NewPath);
                        continue;
                    }

                    if (!File.Exists(fileChange.OldPath)) continue;

                    AnsiConsole.WriteLine($"[->{fileChange.ChangeId}] Remote deleted file: {fileChange.OldPath}");


                    File.Delete(fileChange.OldPath);
                    continue;
                }
                
                if (_protectedFilesService.IsLocked(fileChange.OldPath))
                {
                    Console.WriteLine(
                        $"[->{fileChange.ChangeId}] Remote modify request targets a locked file, skipping");
                    continue;
                }

                if (_protectedFilesService.IsLocked(fileChange.NewPath))
                {
                    Console.WriteLine(
                        $"[->{fileChange.ChangeId}] Remote modify request targets a locked file, skipping");
                    continue;
                }



                if (!File.Exists(fileChange.OldPath) && !string.IsNullOrEmpty(fileChange.NewPath)) // CREATED
                {
                    _protectedFilesService.LockFile(fileChange.NewPath);

                    if (fileChange.IsDirectory == 1)
                    {
                        Directory.CreateDirectory(fileChange.NewPath);
                        AnsiConsole.WriteLine(
                            $"[->{fileChange.ChangeId}] Remote created new directory: {fileChange.NewPath}");
                        continue;
                    }

                    if (!Directory.Exists(Path.GetDirectoryName(fileChange.NewPath)) &&
                        !string.IsNullOrEmpty(Path.GetDirectoryName(fileChange.NewPath)))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(fileChange.NewPath)!);
                        await Task.Delay(50, token);
                    }

                    AnsiConsole.WriteLine($"[->{fileChange.ChangeId}] Remote created new file: {fileChange.NewPath}");
                    await File.WriteAllBytesAsync(fileChange.NewPath, fileChange.Contents.ToByteArray(), token);
                    continue;
                }


                if (File.Exists(fileChange.OldPath) && fileChange.OldPath != fileChange.NewPath) // MOVED
                {
                    try
                    {
                        _protectedFilesService.LockFile(fileChange.NewPath);
                        AnsiConsole.WriteLine(
                            $"[->{fileChange.ChangeId}] Remote moved file from {fileChange.OldPath} to {fileChange.NewPath}");
                        if (!Directory.Exists(Path.GetDirectoryName(fileChange.NewPath)) &&
                            !string.IsNullOrEmpty(Path.GetDirectoryName(fileChange.NewPath)))
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(fileChange.NewPath)!);
                            await Task.Delay(50, token);
                        }

                        File.Move(fileChange.OldPath, fileChange.NewPath ?? fileChange.OldPath);
                        continue;
                    }
                    finally
                    {
                        await Task.Delay(550, token);
                        _protectedFilesService.UnlockPath(fileChange.NewPath!);
                    }
                }

                if (Directory.Exists(fileChange.OldPath) && fileChange.OldPath != fileChange.NewPath)
                {
                    try
                    {
                        _protectedFilesService.LockFile(fileChange.NewPath);
                        AnsiConsole.WriteLine(
                            $"[->{fileChange.ChangeId}] Remote moved directory from {fileChange.OldPath} to {fileChange.NewPath}");
                        Directory.Move(fileChange.OldPath, fileChange.NewPath);
                        continue;
                    }
                    finally
                    {
                        await Task.Delay(550, token);
                        _protectedFilesService.UnlockPath(fileChange.NewPath);
                    }
                }

                try
                {
                    _protectedFilesService.LockFile(fileChange.NewPath);
                    if (fileChange.IsDirectory == 1)
                    {
                        AnsiConsole.WriteLine(
                            $"[->{fileChange.ChangeId}] Remote updated directory {fileChange.OldPath}");
                        Directory.CreateDirectory(fileChange.NewPath);
                        continue;
                    }

                    if (!Directory.Exists(Path.GetDirectoryName(fileChange.NewPath)) &&
                        !string.IsNullOrEmpty(Path.GetDirectoryName(fileChange.NewPath)))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(fileChange.NewPath)!);
                        await Task.Delay(50, token);
                    }

                    AnsiConsole.WriteLine($"[->{fileChange.ChangeId}] Remote updated file {fileChange.OldPath}");
                    await IOUtils.WaitForUnlock(fileChange.NewPath!, token);
                    await Task.Delay(250);
                    await File.WriteAllBytesAsync(fileChange.NewPath!, fileChange.Contents.ToByteArray(), token);
                }
                finally
                {
                    await Task.Delay(505, token);
                    _protectedFilesService.UnlockPath(fileChange.NewPath);
                }
            }
        }
        catch (TaskCanceledException)
        {
            Console.WriteLine("Listener for remote changes stopped because the token was cancelled");
        }
        catch(Exception error)
        {
            Console.WriteLine(error);
        }
        finally
        {
            if (cts.IsCancellationRequested)
            {
                Console.WriteLine("Listener for remote changes stopped");
            }
            else
            {
                Console.WriteLine("Listener for remote changes stopped, canceling");
                cts.Cancel();
            }
        }
    }
}