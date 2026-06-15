using Invio.Services;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using Serilog;
using SharedLib.Config;
using SharedLib.Db;
using SharedLib.Messaging;
using SharedLib.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("sharedsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

builder.Host.UseWindowsService();

// Serilog
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("Logs/log-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// Database
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

// LOL Config
builder.Services.Configure<LolServiceOptions>(
    builder.Configuration.GetSection("LOLService"));

// Services
builder.Services.AddSingleton<IServiceSoapClient, ServiceSoapClient>();
builder.Services.AddSingleton<IInvioQueue, InvioQueue>();
builder.Services.AddSingleton<IInvioQueueTracker, InvioQueueTracker>();
builder.Services.AddHostedService<InvioProcessor>();
builder.Services.AddHostedService<InvioWatcher>();
builder.Services.AddScoped<ILogService, LogService>();
builder.Services.AddSingleton<IRabbitPublisher, RabbitPublisher>();

// RabbitMQ
builder.Services.AddSingleton<IConnection>(sp =>
{
    var rabbit = builder.Configuration.GetSection("RabbitMQ");

    var factory = new ConnectionFactory()
    {
        HostName = rabbit["Host"],
        UserName = rabbit["User"],
        Password = rabbit["Password"],
        DispatchConsumersAsync = true,
        AutomaticRecoveryEnabled = true,
        NetworkRecoveryInterval = TimeSpan.FromSeconds(30)
    };

    return factory.CreateConnection();
});

var app = builder.Build();

app.Run();