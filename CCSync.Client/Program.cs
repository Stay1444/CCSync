using CCSync.Client;
using CCSync.RPC;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using Spectre.Console;

if (args.Length > 0)
{
    Directory.SetCurrentDirectory(args[0]);
}

var project = await ProjectLoader.LoadProjectAsync() ?? await ProjectSetup.Setup();

AnsiConsole.MarkupLine("[green]Connecting...[/]");

var channel = GrpcChannel.ForAddress(project.Origin);

var fileClient = new FileService.FileServiceClient(channel);

var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, _) =>
{
    cts.Cancel();
};

_ = async () =>
{
    var streamingCall = fileClient.OnServerFileChanged(new Empty(), new Metadata()
    {
        {"WorldId", project.World},
        {"AuthId", project.Auth.ToString()}
    });

    await foreach (var fileChange in streamingCall.ResponseStream.ReadAllAsync(cts.Token))
    {
        if (!File.Exists(fileChange.OldPath) && fileChange.NewPath is not null) // CREATED
        {
            File.WriteAllBytes(fileChange.NewPath, fileChange.Contents.ToByteArray());
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

        await File.WriteAllBytesAsync(fileChange.NewPath!, fileChange.Contents.ToByteArray());
    }
    AnsiConsole.MarkupLine("[orange]Listener for server changes stopped[/]");
};
