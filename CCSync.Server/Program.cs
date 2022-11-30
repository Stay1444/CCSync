using System.Net;
using AutoMapper;
using CCSync.Server.Services;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Serilog;

#if RELEASE
using Serilog.Events;
#endif

#if DEBUG
Log.Logger = new LoggerConfiguration().WriteTo.Console().MinimumLevel.Debug()
    .CreateLogger();
#else
Directory.CreateDirectory("logs");
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Hour, fileSizeLimitBytes: null, retainedFileCountLimit: null)
    .CreateLogger();
#endif

var port = 5000;
var host = IPAddress.Parse("0.0.0.0");

if (int.TryParse(Environment.GetEnvironmentVariable("LISTEN_PORT"), out var envPort))
{
    port = envPort;
}

if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LISTEN_HOST")))
{
    host = IPAddress.Parse(Environment.GetEnvironmentVariable("LISTEN_HOST")!);
}

Log.Information("Starting");

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();

#if DEBUG
builder.Logging.AddSerilog();
#else
builder.Logging.AddSerilog(new LoggerConfiguration().WriteTo.Console(LogEventLevel.Warning).WriteTo.File("logs/asp-.txt", rollingInterval: RollingInterval.Hour, fileSizeLimitBytes: null, retainedFileCountLimit: null).MinimumLevel
    .Information().CreateLogger());
#endif

builder.WebHost.UseKestrel(c =>
{
    c.Listen(host, port, options =>
    {
        options.Protocols = HttpProtocols.Http2;
    });
});

var autoMapperConfig = new MapperConfiguration(cfg =>
{

});

builder.Services.AddSingleton(autoMapperConfig.CreateMapper());

builder.Services.AddHostedService<WorldProvider>();

builder.Services.AddGrpc();

var app = builder.Build();

Log.Information("Ready on {0}:{1}", host, port);

app.Run();