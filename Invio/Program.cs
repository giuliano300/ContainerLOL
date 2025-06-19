using Invio.Services;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Serilog;
using SharedLib.Config;
using SharedLib.Db;
using SharedLib.Messaging;
using SharedLib.Services;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("Logs/log-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.Configure<LolServiceOptions>(
    builder.Configuration.GetSection("LOLService"));

builder.Services.AddSingleton<IServiceSoapClient, ServiceSoapClient>();
builder.Services.AddSingleton<IInvioQueue, InvioQueue>();
builder.Services.AddSingleton<IInvioQueueTracker, InvioQueueTracker>();
builder.Services.AddHostedService<InvioProcessor>();
builder.Services.AddHostedService<InvioWatcher>();
builder.Services.AddScoped<ILogService, LogService>();

builder.Services.AddSingleton<IRabbitPublisher, RabbitPublisher>();

builder.Services.AddSingleton<IConnection>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<Program>>();

    var factory = new ConnectionFactory()
    {
        HostName = "rabbitmq",
        UserName = "guest",
        Password = "guest",
        DispatchConsumersAsync = true
    };

    const int maxRetries = 10;
    const int delayMs = 5000;

    for (int attempt = 1; attempt <= maxRetries; attempt++)
    {
        try
        {
            logger.LogInformation("Tentativo di connessione a RabbitMQ (tentativo {Attempt})...", attempt);
            return factory.CreateConnection();
        }
        catch (BrokerUnreachableException ex)
        {
            logger.LogWarning(ex, "Tentativo {Attempt} fallito. RabbitMQ non raggiungibile, retry in {Delay}s...", attempt, delayMs / 1000);
            Thread.Sleep(delayMs);
        }
    }

    throw new Exception("❌ Impossibile connettersi a RabbitMQ dopo vari tentativi.");
});

builder.Services.AddControllers();

var app = builder.Build();

app.Run();
