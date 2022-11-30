using CCSync.RPC;
using CCSync.Server.Services;
using CCSync.Server.Utils;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace CCSync.Server.Controllers;

public sealed class FileController : FileService.FileServiceBase
{
    private WorldProvider _worldProvider;
    private FileListenerService _fileListenerService;
    
    public FileController(WorldProvider worldProvider, FileListenerService fileListenerService)
    {
        _worldProvider = worldProvider;
        _fileListenerService = fileListenerService;
    }

    public override Task<Empty> OnClientFileChanged(IAsyncStreamReader<FileChanged> requestStream, ServerCallContext context)
    {
        return base.OnClientFileChanged(requestStream, context);
    }

    public override async Task OnServerFileChanged(Empty request, IServerStreamWriter<FileChanged> responseStream, ServerCallContext context)
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

        _fileListenerService.Start(pathToComputer);
        
        while (!context.CancellationToken.IsCancellationRequested)
        {
            var (oldPath, newPath, sendContent) = await _fileListenerService.Listen();

            var msg = new FileChanged
            {
                OldPath = oldPath,
                NewPath = newPath
            };
            
            if (sendContent)
            {
                var fs = File.OpenRead(newPath);
                msg.Contents = await ByteString.FromStreamAsync(fs);
                fs.Close();
                await fs.DisposeAsync();
            }

            await responseStream.WriteAsync(msg);
        }
    }
}