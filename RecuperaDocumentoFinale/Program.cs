using RecuperaDocumentoFinale.Services;
using Microsoft.EntityFrameworkCore;
using Serilog;
using SharedLib.Config;
using SharedLib.Db;
using SharedLib.Models;
using SharedLib.Services;
using RabbitMQ.Client.Exceptions;
using RabbitMQ.Client;
using SharedLib.Messaging;

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
builder.Services.AddSingleton<IRecuperaDocumentoFinaleQueue, RecuperaDocumentoFinaleQueue>();
builder.Services.AddSingleton<IRecuperaDocumentoFinaleQueueTracker, RecuperaDocumentoFinaleQueueTracker>();
builder.Services.AddHostedService<RecuperaDocumentoFinaleProcessor>();
builder.Services.AddHostedService<RecuperaDocumentoFinaleWatcher>();
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
