using RecuperaDocumentoFinale.Services;
using Microsoft.EntityFrameworkCore;
using Serilog;
using SharedLib.Config;
using SharedLib.Db;
using SharedLib.Services;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using SharedLib.Messaging;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("sharedsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

builder.Host.UseWindowsService();

// ================= SERILOG =================
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("Logs/log-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// ================= DATABASE =================
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

// ================= CONFIG =================
builder.Services.Configure<LolServiceOptions>(
    builder.Configuration.GetSection("LOLService"));

// ================= SERVICES =================
builder.Services.AddSingleton<IServiceSoapClient, ServiceSoapClient>();

builder.Services.AddSingleton<IRecuperaDocumentoFinaleQueue, RecuperaDocumentoFinaleQueue>();
builder.Services.AddSingleton<IRecuperaDocumentoFinaleQueueTracker, RecuperaDocumentoFinaleQueueTracker>();

builder.Services.AddHostedService<RecuperaDocumentoFinaleProcessor>();
builder.Services.AddHostedService<RecuperaDocumentoFinaleWatcher>();

builder.Services.AddScoped<ILogService, LogService>();
builder.Services.AddSingleton<IRabbitPublisher, RabbitPublisher>();

// ================= RABBITMQ =================
builder.Services.AddSingleton<IConnection>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<Program>>();
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

    const int maxRetries = 10;
    const int delayMs = 5000;

    for (int attempt = 1; attempt <= maxRetries; attempt++)
    {
        try
        {
            logger.LogInformation(
                "Tentativo connessione RabbitMQ {Attempt}/{MaxRetries}",
                attempt, maxRetries);

            return factory.CreateConnection();
        }
        catch (BrokerUnreachableException ex)
        {
            logger.LogWarning(
                ex,
                "RabbitMQ non raggiungibile. Retry tra {Delay} secondi",
                delayMs / 1000);

            Thread.Sleep(delayMs);
        }
    }

    throw new Exception("Impossibile connettersi a RabbitMQ.");
});

// ================= BUILD =================
var app = builder.Build();

// ================= RUN =================
app.Run();