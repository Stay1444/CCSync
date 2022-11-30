using CCSync.Client;
using CCSync.RPC;
using Grpc.Net.Client;
using Spectre.Console;

if (args.Length > 0)
{
    Directory.SetCurrentDirectory(args[0]);
}

var project = await ProjectLoader.LoadProjectAsync() ?? await ProjectSetup.Setup();

AnsiConsole.MarkupLine("[green]Connecting...[/]");

var channel = GrpcChannel.ForAddress(project.Origin);
await channel.ConnectAsync();
AnsiConsole.MarkupLine("[green]Ready[/]");
var fileClient = new FileService.FileServiceClient(channel);

var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, _) =>
{
    cts.Cancel();
};

var protectedFiles = new List<string>();

_ = new RemoteListener().ListenAsync(project, fileClient, protectedFiles, cts);
_ = new LocalListener().ListenAsync(project, fileClient, protectedFiles, cts);

await Task.Delay(-1, cts.Token);