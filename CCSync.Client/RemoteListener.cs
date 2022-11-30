using CCSync.RPC;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Spectre.Console;

namespace CCSync.Client;

sealed class RemoteListener
{
    public async Task ListenAsync(CCSyncProject project, FileService.FileServiceClient fileClient, List<string> protectedFiles, CancellationTokenSource cts)
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
                if (!File.Exists(fileChange.OldPath) && !string.IsNullOrEmpty(fileChange.NewPath)) // CREATED
                {
                    lock (protectedFiles)
                    {
                        protectedFiles.Add(fileChange.NewPath);
                    }
                    AnsiConsole.WriteLine($"Remote created new file: {fileChange.NewPath}");
                    await File.WriteAllBytesAsync(fileChange.NewPath, fileChange.Contents.ToByteArray(), token);
                    continue;
                }

                if (File.Exists(fileChange.OldPath) && !File.Exists(fileChange.NewPath)) // DELETED
                {
                    lock (protectedFiles)
                    {
                        protectedFiles.Add(fileChange.OldPath);
                    }
                    AnsiConsole.WriteLine($"Remote deleted file: {fileChange.OldPath}");
                    File.Delete(fileChange.OldPath);
                    continue;
                }

                if (File.Exists(fileChange.OldPath) && fileChange.OldPath != fileChange.NewPath) // MOVED
                {
                    lock (protectedFiles)
                    {
                        protectedFiles.Add(fileChange.NewPath!);
                    }
                    AnsiConsole.WriteLine($"Remote moved file from {fileChange.OldPath} to {fileChange.NewPath}");
                    File.Move(fileChange.OldPath, fileChange.NewPath ?? fileChange.OldPath);
                    continue;
                }

                lock (protectedFiles)
                {
                    protectedFiles.Add(fileChange.NewPath!);
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