using CCSync.RPC;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace CCSync.Client;

sealed class LocalListener
{
    private FileListenerService _fileListenerService = new FileListenerService();
    
    public async Task ListenAsync(CCSyncProject project, FileService.FileServiceClient fileClient, List<string> protectedFiles, CancellationTokenSource cts)
    {
        var token = cts.Token;
        try
        {
            var stream = fileClient.OnClientFileChanged(new Metadata()
            {
                {"WorldId", project.World},
                {"AuthId", project.Auth.ToString()}
            });

            _fileListenerService.Start(Directory.GetCurrentDirectory(), token);
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var (oldPath, newPath, sendContent) = await _fileListenerService.ListenAsync();
                    await Task.Delay(250, token);
                    lock (protectedFiles)
                    {
                        if (oldPath is not null && protectedFiles.Contains(Path.GetRelativePath(Directory.GetCurrentDirectory(), oldPath)))
                        {
                            protectedFiles.Remove(Path.GetRelativePath(Directory.GetCurrentDirectory(), oldPath));
                            Console.WriteLine($"{oldPath} was in protected files so we skip it");
                            continue;
                        }

                        if (newPath is not null && protectedFiles.Contains(Path.GetRelativePath(Directory.GetCurrentDirectory(), newPath)))
                        {
                            protectedFiles.Remove(Path.GetRelativePath(Directory.GetCurrentDirectory(), newPath));
                            Console.WriteLine($"{newPath} was in protected files so we skip it");
                            continue;
                        }
                    }
                    Console.WriteLine("Local file update detected");

                    var msg = new FileChanged();

                    if (oldPath is not null)
                    {
                        msg.OldPath = Path.GetRelativePath(Directory.GetCurrentDirectory(), oldPath);
                    }

                    if (newPath is not null)
                    {
                        msg.NewPath = Path.GetRelativePath(Directory.GetCurrentDirectory(), newPath);
                    }

                    if (sendContent && newPath is not null)
                    {
                        var fileInfo = new FileInfo(newPath);
                        while (true)
                        {
                            try
                            {
                                var fs = fileInfo.OpenRead();
                                msg.Contents = await ByteString.FromStreamAsync(fs, token);
                                fs.Close();
                                await fs.DisposeAsync();
                                break;
                            }
                            catch
                            {
                                Console.WriteLine("File is locked, waiting 50ms");
                                await Task.Delay(50, token);
                            }
                        }
                    }
                    await stream.RequestStream.WriteAsync(msg, token);
                }
                catch (TaskCanceledException)
                {

                }
            }

            var streamingCall = fileClient.OnServerFileChanged(new Empty(), new Metadata()
            {
                {"WorldId", project.World},
                {"AuthId", project.Auth.ToString()}
            });

            await foreach (var fileChange in streamingCall.ResponseStream.ReadAllAsync(token))
            {
                if (!File.Exists(fileChange.OldPath) && fileChange.NewPath is not null) // CREATED
                {
                    await File.WriteAllBytesAsync(fileChange.NewPath, fileChange.Contents.ToByteArray(), token);
                    continue;
                }

                if (File.Exists(fileChange.OldPath) && !File.Exists(fileChange.NewPath)) // DELETED
                {
                    File.Delete(fileChange.OldPath);
                    continue;
                }

                if (File.Exists(fileChange.OldPath) && fileChange.OldPath != fileChange.NewPath) // MOVED
                {
                    File.Move(fileChange.OldPath, fileChange.NewPath ?? fileChange.OldPath);
                    continue;
                }

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
            Console.WriteLine("Listener for local changes stopped");
        }
    }
}