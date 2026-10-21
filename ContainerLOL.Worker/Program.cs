using Conferma.Services;
using Invio.Services;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using RecuperaDocumentoFinale.Services;
using Serilog;
using SharedLib.Config;
using SharedLib.Db;
using SharedLib.Messaging;
using SharedLib.Services;
using Valorizza.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("sharedsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("Logs/log-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Services.AddSerilog();
builder.Services.AddWindowsService(options => options.ServiceName = "EasySender.ContainerLOL");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.Configure<LolServiceOptions>(
    builder.Configuration.GetSection("LOLService"));

builder.Services.AddSingleton<IServiceSoapClient, ServiceSoapClient>();
builder.Services.AddScoped<ILogService, LogService>();
builder.Services.AddSingleton<IRabbitPublisher, RabbitPublisher>();
builder.Services.AddSingleton<IConnection>(sp =>
{
    var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("RabbitMQ");
    var rabbit = builder.Configuration.GetSection("RabbitMQ");

    var factory = new ConnectionFactory
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

    for (var attempt = 1; attempt <= maxRetries; attempt++)
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

    throw new InvalidOperationException("Impossibile connettersi a RabbitMQ dopo vari tentativi.");
});

builder.Services.AddSingleton<IInvioQueue, InvioQueue>();
builder.Services.AddSingleton<IInvioQueueTracker, InvioQueueTracker>();
builder.Services.AddHostedService<InvioProcessor>();
builder.Services.AddHostedService<InvioWatcher>();

builder.Services.AddSingleton<IValorizzaQueue, ValorizzaQueue>();
builder.Services.AddSingleton<IValorizzaQueueTracker, ValorizzaQueueTracker>();
builder.Services.AddHostedService<ValorizzaProcessor>();
builder.Services.AddHostedService<ValorizzaWatcher>();

builder.Services.AddSingleton<IConfermaQueue, ConfermaQueue>();
builder.Services.AddSingleton<IConfermaQueueTracker, ConfermaQueueTracker>();
builder.Services.AddHostedService<ConfermaProcessor>();
builder.Services.AddHostedService<ConfermaWatcher>();

builder.Services.AddSingleton<IRecuperaDocumentoFinaleQueue, RecuperaDocumentoFinaleQueue>();
builder.Services.AddSingleton<IRecuperaDocumentoFinaleQueueTracker, RecuperaDocumentoFinaleQueueTracker>();
builder.Services.AddHostedService<RecuperaDocumentoFinaleProcessor>();
builder.Services.AddHostedService<RecuperaDocumentoFinaleWatcher>();

var app = builder.Build();

await app.RunAsync();
