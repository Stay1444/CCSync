using CCSync.RPC;
using Grpc.Net.Client;
using Spectre.Console;

namespace CCSync.Client;

sealed class ProjectSetup
{
    public static async Task<CCSyncProject> Setup()
    {
        if (!AnsiConsole.Confirm("[red]No CCSync project found in current directory.[/] Do you want to create one?"))
        {
            Environment.Exit(0);
        }

        string origin = AnsiConsole.Ask<string>("What is the [green]origin[/] of the project? Example: [cyan]https://ccsync.myserver.tdl[/] ");
        World[] worlds = Array.Empty<World>();
    
        await AnsiConsole.Status()
            .StartAsync("Connecting...", async ctx =>
            {
                var cts = new CancellationTokenSource();
                try
                {
                    var channel = GrpcChannel.ForAddress(origin);

                    cts.CancelAfter(5000);
                    
                    await channel.ConnectAsync(cts.Token);
                    
                    ctx.Status("Requesting worlds...");
                    var handshakeClient = new Handshake.HandshakeClient(channel);
                    var worldsResponse = await handshakeClient.GetWorldsAsync(new GetWorldsRequest());
                    worlds = worldsResponse.Worlds.ToArray();
                }
                catch (Exception error)
                {
                    if (cts.IsCancellationRequested)
                    {
                        AnsiConsole.MarkupLine($"[red]Could not connect to the server:[/] Timeout");
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[red]Could not connect to the server:[/] {error}");
                    }
                    Environment.Exit(1);
                }
            });

        if (worlds.Length == 0)
        {
            AnsiConsole.MarkupLine(
                """
                Server returned [red]no worlds[/].
                
                Contact the server administrators if the problem persists.
                """
                );
            Environment.Exit(1);
        }

        var world = AnsiConsole.Prompt(new SelectionPrompt<World>()
            .Title("Select the [green]world[/] where [blue]your computer[/] is located: ")
            .PageSize(10)
            .MoreChoicesText("[gray]Move up and down to reveal more worlds[/]")
            .AddChoices(worlds)
            .UseConverter(x => x.Name)
        );

        var auth = Guid.NewGuid();
        
        AnsiConsole.MarkupLine(
                $"""
        
                Create a file named [blue]ccsync[/] in the root of   
                your computer with the following text inside:
                
                    [magenta]{auth.ToString()}[/]
        
                """
            );
        
        await AnsiConsole.Status()
            .StartAsync("Connecting...", async ctx =>
            {
                try
                {
                    var channel = GrpcChannel.ForAddress(origin);

                    await channel.ConnectAsync();
                    
                    ctx.Status("Waiting...");
                    var handshakeClient = new Handshake.HandshakeClient(channel);
                    var result = await handshakeClient.WaitForAuthAsync(new WaitForAuthRequest()
                    {
                        Auth = auth.ToString(),
                        WorldId = world.Id
                    });

                    if (!result.Success)
                    {
                        AnsiConsole.MarkupLine($"[red]Error while waiting for authentication:[/] {result.Error}");
                        Environment.Exit(1);
                    }
                }
                catch (Exception error)
                {
                    AnsiConsole.MarkupLine($"[red]Could not connect to the server:[/] {error}");

                    Environment.Exit(1);
                }
            });
        
        await ProjectLoader.CreateDefaultProjectAsync(origin, auth, world.Id);
        var project = await ProjectLoader.LoadProjectAsync();
        
        if (project is null)
        {
            AnsiConsole.MarkupLine("[red]Project creation failed");
            Environment.Exit(1);
        }
        
        AnsiConsole.MarkupLine("Project created [green]successfully[/].");
        return project;
    }
}