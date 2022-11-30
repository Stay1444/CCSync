using CCSync.RPC;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace CCSync.Server.Controllers;

public sealed class FileController : FileService.FileServiceBase
{
    public override Task<Empty> OnClientFileChanged(IAsyncStreamReader<FileChanged> requestStream, ServerCallContext context)
    {
        return base.OnClientFileChanged(requestStream, context);
    }

    public override Task OnServerFileChanged(Empty request, IServerStreamWriter<FileChanged> responseStream, ServerCallContext context)
    {
        return base.OnServerFileChanged(request, responseStream, context);
    }
}