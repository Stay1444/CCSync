using CCSync.RPC;
using CCSync.Server.Services;
using Grpc.Core;
using Serilog;

namespace CCSync.Server.Controllers;

public sealed class HandshakeController : HandshakeService.HandshakeServiceBase
{
    private readonly WorldProvider _worldService;
    private readonly AuthWaiterService _authWaiterService;
    
    public HandshakeController(WorldProvider worldService, AuthWaiterService authWaiterService)
    {
        _worldService = worldService;
        _authWaiterService = authWaiterService;
    }

    public override Task<GetWorldsResponse> GetWorlds(GetWorldsRequest request, ServerCallContext context)
    {
        var response = new GetWorldsResponse();

        var worlds = _worldService.Worlds.Values;

        foreach (var world in worlds)
        {
            response.Worlds.Add(new World()
            {
                Id = world.Id,
                Name = world.Name
            });
        }

        return Task.FromResult(response);
    }

    public override async Task<WaitForAuthResponse> WaitForAuth(WaitForAuthRequest request, ServerCallContext context)
    {
        try
        {
            var world = _worldService.GetWorld(request.WorldId);
            if (world is null)
            {
                return new WaitForAuthResponse()
                {
                    Success = false,
                    Error = $"World with id {request.WorldId} not found"
                };
            }
            
            await _authWaiterService.WaitForAsync(world, Guid.Parse(request.Auth), context.CancellationToken);

            return new WaitForAuthResponse()
            {
                Success = true
            };
        }
        catch(Exception error)
        {
            Log.Error("{0}", error);
            return new WaitForAuthResponse()
            {
                Success = false,
                Error = "Unexpected server-side error"
            };
        }
    }
}