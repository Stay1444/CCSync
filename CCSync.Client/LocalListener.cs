using CCSync.RPC;
using CCSync.Shared.Utils.Services;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace CCSync.Client;

sealed class LocalListener
{
    private FileListenerService _fileListenerService = new FileListenerService();
    private ProtectedFilesService _protectedFilesService;

    public LocalListener(ProtectedFilesService protectedFilesService)
    {
        _protectedFilesService = protectedFilesService;
    }

    public async Task ListenAsync(CCSyncProject project, FileService.FileServiceClient fileClient, CancellationTokenSource cts)
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
                    var (oldPath, newPath, isDirectory, sendContent) = await _fileListenerService.ListenAsync();
                    await Task.Delay(50, token);

                    var msg = new FileChanged();
                    msg.IsDirectory = isDirectory;
                    
                    if (!string.IsNullOrEmpty(oldPath))
                    {
                        msg.OldPath = Path.GetRelativePath(Directory.GetCurrentDirectory(), oldPath);
                    }

                    if (!string.IsNullOrEmpty(newPath))
                    {
                        msg.NewPath = Path.GetRelativePath(Directory.GetCurrentDirectory(), newPath);
                    }
                    
                    if (_protectedFilesService.IsLocked(msg.OldPath))
                    {
                        Console.WriteLine($"{msg.OldPath} was in protected files so we skip it");
                        _protectedFilesService.UnlockPath(msg.OldPath);
                        continue;
                    }

                    if (_protectedFilesService.IsLocked(msg.NewPath))
                    {
                        Console.WriteLine($"{newPath} was in protected files so we skip it");
                        _protectedFilesService.UnlockPath(msg.NewPath);
                        continue;
                    }

                    if (sendContent && !string.IsNullOrEmpty(newPath))
                    {
                        if (!Path.EndsInDirectorySeparator(newPath))
                        {
                            var fileInfo = new FileInfo(newPath);
                            while (!cts.IsCancellationRequested)
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
                                    if (!File.Exists(newPath)) return;
                                    Console.WriteLine("File is locked, waiting 50ms");
                                    await Task.Delay(50, token);
                                }
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(msg.OldPath) && !string.IsNullOrEmpty(msg.NewPath))
                    {
                        Console.WriteLine($"Local file {msg.NewPath} updated");
                    }else if (string.IsNullOrEmpty(msg.NewPath))
                    {
                        Console.WriteLine($"Local file {msg.OldPath} deleted");
                    }else if (string.IsNullOrEmpty(msg.OldPath))
                    {
                        Console.WriteLine($"Local file {msg.NewPath} created");
                    }
                    
                    await stream.RequestStream.WriteAsync(msg, token);
                }
                catch (TaskCanceledException)
                {

                }
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